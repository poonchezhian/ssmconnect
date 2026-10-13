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

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SSMConnect.Core;

/// <summary>A company sign-in (IAM Identity Center) address found without the person typing it.</summary>
public sealed record SsoCandidate(string StartUrl, string Region, string Source, bool Guess = false);

/// <summary>
/// Finds the company sign-in URL and region on its own. Looks, in order, at: settings the company's IT team pushed to this PC
/// (registry policy or ProgramData\SSMConnect\sso.json or environment variables), existing AWS CLI settings, sign-in tokens
/// the AWS CLI already cached, and finally (as a clearly-labelled guess) the company's own domain.
/// </summary>
public static class SsoDiscovery
{
    static readonly Regex Url = new(@"^https://[A-Za-z0-9.\-]+\.(awsapps\.com|amazonaws\.com\.cn)(/start)?/?(#/?)?$|^https://[A-Za-z0-9.\-/]+$", RegexOptions.Compiled);

    public static List<SsoCandidate> Find()
    {
        var all = new List<SsoCandidate>();
        void Add(string? url, string? region, string source, bool guess = false)
        {
            url = Clean(url);
            if (url.Length == 0 || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !Url.IsMatch(url)) return;
            region = (region ?? "").Trim();
            if (all.Any(c => string.Equals(c.StartUrl, url, StringComparison.OrdinalIgnoreCase)))
            {
                // keep the first source, but learn a region if the earlier one lacked it
                var i = all.FindIndex(c => string.Equals(c.StartUrl, url, StringComparison.OrdinalIgnoreCase));
                if (all[i].Region.Length == 0 && region.Length > 0) all[i] = all[i] with { Region = region };
                return;
            }
            all.Add(new SsoCandidate(url, region, source, guess));
        }

        // 1) pushed by the company's IT (Group Policy / Intune / a file in ProgramData)
        foreach (var (hive, name) in new[] { (Registry.LocalMachine, "company policy"), (Registry.CurrentUser, "your user policy") })
        {
            try
            {
                using var k = hive.OpenSubKey(@"SOFTWARE\Policies\SSMConnect");
                if (k != null) Add(k.GetValue("SsoStartUrl") as string, k.GetValue("SsoRegion") as string, "Set by your IT team (" + name + ")");
            }
            catch { }
        }
        try
        {
            var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SSMConnect", "sso.json");
            if (File.Exists(f))
            {
                using var d = JsonDocument.Parse(File.ReadAllText(f));
                Add(Str(d.RootElement, "startUrl"), Str(d.RootElement, "region"), "Set by your IT team (sso.json)");
            }
        }
        catch { }
        Add(Environment.GetEnvironmentVariable("SSMCONNECT_SSO_URL"), Environment.GetEnvironmentVariable("SSMCONNECT_SSO_REGION"), "Environment setting");
        Add(Environment.GetEnvironmentVariable("AWS_SSO_START_URL"), Environment.GetEnvironmentVariable("AWS_SSO_REGION"), "Environment setting");

        // 2) AWS CLI configuration already on this PC (all profile files, including a custom AWS_CONFIG_FILE)
        foreach (var cfg in ConfigFiles())
        {
            try
            {
                string section = ""; var url = new Dictionary<string, string>(); var reg = new Dictionary<string, string>();
                foreach (var raw in File.ReadAllLines(cfg))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("[")) { section = line.Trim('[', ']'); continue; }
                    var eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    var key = line.Substring(0, eq).Trim(); var val = line.Substring(eq + 1).Trim();
                    if (key == "sso_start_url") url[section] = val;
                    else if (key == "sso_region") reg[section] = val;
                }
                foreach (var kv in url) Add(kv.Value, reg.TryGetValue(kv.Key, out var r) ? r : "", "AWS settings already on this PC");
            }
            catch { }
        }

        // 3) sign-in tokens cached by the AWS CLI or other AWS tools
        try
        {
            var cache = Path.Combine(AwsProfiles.AwsDir, "sso", "cache");
            if (Directory.Exists(cache))
                foreach (var f in Directory.EnumerateFiles(cache, "*.json"))
                {
                    try
                    {
                        using var d = JsonDocument.Parse(File.ReadAllText(f));
                        Add(Str(d.RootElement, "startUrl"), Str(d.RootElement, "region"), "Earlier AWS sign-in on this PC");
                    }
                    catch { }
                }
        }
        catch { }
        return all;
    }

    /// <summary>
    /// For a PC joined to a company domain: tries https://&lt;company&gt;.awsapps.com/start for names taken from the domain
    /// and reports the ones that answer. These are guesses and are labelled so; the person confirms by signing in.
    /// </summary>
    public static async Task<List<SsoCandidate>> GuessFromDomainAsync(CancellationToken ct = default)
    {
        var names = new List<string>();
        void Domain(string? d)
        {
            if (string.IsNullOrWhiteSpace(d) || d.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return;
            d = d.Trim().ToLowerInvariant();
            var parts = d.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                names.Add(parts[^2]);                       // corp.acme.com -> acme
                names.Add(string.Join("-", parts));         // corp-acme-com
                if (parts.Length >= 3) names.Add(parts[^3] + "-" + parts[^2]); // corp-acme
            }
            else if (parts.Length == 1) names.Add(parts[0]); // ACME (NetBIOS domain)
        }
        Domain(Environment.GetEnvironmentVariable("USERDNSDOMAIN"));
        Domain(Environment.UserDomainName);
        try
        {
            var upn = await RunAsync("whoami", "/upn", ct); // user@company.com on domain / Entra-joined PCs
            var at = upn.IndexOf('@');
            if (at > 0) Domain(upn.Substring(at + 1).Trim());
        }
        catch { }

        var result = new List<SsoCandidate>();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(6) };
        foreach (var n in names.Where(x => Regex.IsMatch(x, "^[a-z0-9][a-z0-9-]{1,62}$")).Distinct())
        {
            if (ct.IsCancellationRequested) break;
            var url = $"https://{n}.awsapps.com/start";
            try
            {
                using var resp = await http.GetAsync(url, ct);
                var code = (int)resp.StatusCode;
                var loc = resp.Headers.Location?.ToString() ?? "";
                // a real access portal answers 200 or redirects to its sign-in; unknown names fail or return 4xx
                if (code == 200 || ((code is 301 or 302 or 303 or 307) && (loc.Contains("awsapps.com") || loc.Contains("amazonaws.com") || loc.Contains("signin.aws"))))
                    result.Add(new SsoCandidate(url, "", "Guessed from your company domain (please confirm)", true));
            }
            catch { /* name does not exist or is unreachable from here */ }
        }
        return result;
    }

    static async Task<string> RunAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var text = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return text;
    }

    static IEnumerable<string> ConfigFiles()
    {
        var files = new List<string> { Path.Combine(AwsProfiles.AwsDir, "config") };
        var custom = Environment.GetEnvironmentVariable("AWS_CONFIG_FILE");
        if (!string.IsNullOrWhiteSpace(custom)) files.Add(custom);
        return files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string Clean(string? raw)
    {
        var u = (raw ?? "").Trim();
        var hash = u.IndexOf('#');
        if (hash >= 0) u = u.Substring(0, hash);
        return u.TrimEnd('/');
    }
}
