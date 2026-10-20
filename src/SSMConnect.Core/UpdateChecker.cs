// Copyright 2026 Poonchezhian Periasamy <poonchezhian@gmail.com>
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SSMConnect.Core;

/// <summary>A newer release found on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, string FileName, string? Sha256, string Notes, string PageUrl);

/// <summary>
/// Looks for a newer SSMConnect on the project's GitHub Releases page (once a day), downloads the setup file, checks its SHA-256
/// against the value published with the release, and hands it to the setup program, which upgrades the installed copy in place.
/// </summary>
public static class UpdateChecker
{
    /// <summary>GitHub "owner/repository" that publishes the releases. Change this one line if the project moves.</summary>
    public const string Repo = "poonchezhian/ssmconnect";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";
    public static string DirectDownload => $"https://github.com/{Repo}/releases/latest/download/SSMConnect-Setup-x64.exe";

    static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    sealed class State
    {
        public DateTime LastCheckUtc { get; set; } = DateTime.MinValue;
        public string NotifiedVersion { get; set; } = "";
    }

    static string StatePath => Path.Combine(ConfigStore.DataDir, "update-check.json");

    static State LoadState()
    {
        try { return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State(); } catch { return new State(); }
    }

    static void SaveState(State s)
    {
        try { Directory.CreateDirectory(ConfigStore.DataDir); File.WriteAllText(StatePath, JsonSerializer.Serialize(s)); } catch { }
    }

    /// <summary>The version of the running program, e.g. 1.0.18.</summary>
    public static Version Current
    {
        get
        {
            try
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
            catch { return new Version(1, 0, 0); }
        }
    }

    /// <summary>True when the last successful check was more than a day ago (or never).</summary>
    public static bool DueForCheck() => DateTime.UtcNow - LoadState().LastCheckUtc >= Interval;

    /// <summary>True the first time a given version is found, so the tray notification appears once per version.</summary>
    public static bool ShouldNotify(Version v)
    {
        var s = LoadState();
        if (s.NotifiedVersion == v.ToString()) return false;
        s.NotifiedVersion = v.ToString();
        SaveState(s);
        return true;
    }

    static HttpClient NewClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SSMConnect/" + Current);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    static bool TrustedHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var h = u.Host.ToLowerInvariant();
        return h == "github.com" || h.EndsWith(".github.com") || h.EndsWith(".githubusercontent.com");
    }

    /// <summary>Returns the newer release, or null when this copy is up to date. Throws when GitHub cannot be reached.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var http = NewClient();
        using var resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) { Touch(); return null; } // no release published yet
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        Touch();
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        if (latest <= Current) return null;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pr) && pr.GetBoolean()) return null;

        string? url = null, name = null, sums = null, digest = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var n = a.GetProperty("name").GetString() ?? "";
            var u = a.GetProperty("browser_download_url").GetString() ?? "";
            if (Regex.IsMatch(n, @"^SSMConnect-Setup-\d+(\.\d+)*-x64\.exe$", RegexOptions.IgnoreCase)) { url = u; name = n; if (a.TryGetProperty("digest", out var dg) && dg.GetString() is { } t) digest = t; }
            else if (n.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sums = u;
        }
        if (url == null || name == null || !TrustedHost(url)) return null;

        string? hash = null;
        if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) hash = digest[7..];
        else if (sums != null && TrustedHost(sums))
        {
            var text = await http.GetStringAsync(sums, ct);
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split(new[] { ' ', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[^1].Equals(name, StringComparison.OrdinalIgnoreCase)) { hash = parts[0]; break; }
            }
        }
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
        return new UpdateInfo(latest, tag, url, name, hash?.Trim().ToLowerInvariant(), notes, page);
    }

    static void Touch()
    {
        var s = LoadState();
        s.LastCheckUtc = DateTime.UtcNow;
        SaveState(s);
    }

    /// <summary>Downloads the setup file and verifies it. Returns the local path; throws if it cannot be verified.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<int>? progress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(info.Sha256)) throw new InvalidOperationException("This release does not publish a checksum, so the download cannot be verified. Please download it from the releases page instead.");
        if (!TrustedHost(info.DownloadUrl)) throw new InvalidOperationException("The download address is not on GitHub.");
        var dir = Path.Combine(Path.GetTempPath(), "SSMConnect-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, info.FileName);
        using var http = NewClient();
        http.Timeout = TimeSpan.FromMinutes(15);
        using var resp = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        using (var src = await resp.Content.ReadAsStreamAsync(ct))
        using (var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buf = new byte[81920];
            long done = 0; int n, last = -1;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) { var pct = (int)(done * 100 / total); if (pct != last) { last = pct; progress?.Report(pct); } }
            }
        }
        string actual;
        using (var fs = File.OpenRead(path)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
        if (actual != info.Sha256)
        {
            try { File.Delete(path); } catch { }
            throw new InvalidOperationException("The downloaded file does not match the checksum published with the release, so it was deleted and nothing was installed.");
        }
        return path;
    }
}
