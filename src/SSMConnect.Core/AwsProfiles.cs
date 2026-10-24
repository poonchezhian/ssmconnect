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


using System.Text;
using System.Text.RegularExpressions;

namespace SSMConnect.Core;

public enum ProfileKind { Sso, AccessKeys, Other }

/// <summary>One named AWS account login stored in the person's ~/.aws files (not a server profile).</summary>
public sealed record ProfileInfo(string Name, ProfileKind Kind, string? Region, string? AccountId, string? RoleName, string? SsoSession, string? StartUrl)
{
    public string Display => Kind switch
    {
        ProfileKind.Sso => $"{Name}   (company sign-in{(AccountId != null ? ", account " + AccountId : "")})",
        ProfileKind.AccessKeys => $"{Name}   (access keys)",
        _ => Name,
    };
    public override string ToString() => Display;
}

/// <summary>
/// Reads and writes AWS profiles directly in ~/.aws/config and ~/.aws/credentials, so profiles can be listed and
/// created without the AWS CLI and without any interactive "aws configure" prompt. Only the sections SSMConnect
/// touches are rewritten; everything else in those files is preserved, and a .ssmconnect.bak copy is kept.
/// </summary>
public static class AwsProfiles
{
    static readonly Regex NameRule = new(@"^[A-Za-z0-9_.@\-]{1,64}$", RegexOptions.Compiled);

    public static string AwsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aws");
    // an environment setting is followed only while the file it names exists; a leftover value (for example from a removed local AWS emulator) falls back to the standard file
    public static string ConfigPath => Environment.GetEnvironmentVariable("AWS_CONFIG_FILE") is { Length: > 0 } c && File.Exists(c) ? c : Path.Combine(AwsDir, "config");
    public static string CredentialsPath => Environment.GetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE") is { Length: > 0 } c && File.Exists(c) ? c : Path.Combine(AwsDir, "credentials");

    public static bool IsValidName(string? name) => name != null && NameRule.IsMatch(name);

    // ---- reading -----------------------------------------------------------------------------
    public static List<ProfileInfo> List()
    {
        var cfg = ReadIni(ConfigPath);
        var cred = ReadIni(CredentialsPath);
        var names = new List<string>();
        foreach (var s in cfg.Keys)
        {
            if (s == "default") names.Add("default");
            else if (s.StartsWith("profile ", StringComparison.Ordinal)) names.Add(s.Substring(8).Trim());
        }
        foreach (var s in cred.Keys) if (!names.Contains(s)) names.Add(s);

        var list = new List<ProfileInfo>();
        foreach (var n in names.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            cfg.TryGetValue(n == "default" ? "default" : "profile " + n, out var c);
            cred.TryGetValue(n, out var k);
            c ??= new(); k ??= new();
            string? Get(Dictionary<string, string> d, string key) => d.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

            var session = Get(c, "sso_session");
            var start = Get(c, "sso_start_url");
            if (session != null && cfg.TryGetValue("sso-session " + session, out var ss)) start ??= Get(ss, "sso_start_url");
            var kind = session != null || start != null ? ProfileKind.Sso
                     : Get(k, "aws_access_key_id") != null || Get(c, "aws_access_key_id") != null ? ProfileKind.AccessKeys
                     : ProfileKind.Other;
            list.Add(new ProfileInfo(n, kind, Get(c, "region"), Get(c, "sso_account_id"), Get(c, "sso_role_name"), session, start));
        }
        return list;
    }

    public static ProfileInfo? Find(string? name) => name == null ? null : List().FirstOrDefault(p => p.Name == name);

    /// <summary>
    /// The saved profiles that share this profile's company sign-in (the other AWS accounts reachable with it), one per AWS account.
    /// Profiles that point at the same account are counted once; <paramref name="preferred"/> names (those with saved connections) win.
    /// </summary>
    public static List<ProfileInfo> SameSignIn(ProfileInfo p, IEnumerable<ProfileInfo> all, ISet<string>? preferred = null)
    {
        if (p.Kind != ProfileKind.Sso) return new List<ProfileInfo> { p };
        var key = PrimaryKey(p);
        var group = all.Where(x => PrimaryKey(x) == key).ToList();
        if (!group.Any(x => x.Name == p.Name)) group.Add(p);
        return OneEach(group, preferred, p.Name);
    }

    /// <summary>One profile per AWS account: when several profiles point at the same account, the one with saved connections (then the chosen one, then the shortest name) is kept.</summary>
    public static List<ProfileInfo> OneEach(IEnumerable<ProfileInfo> group, ISet<string>? preferred = null, string? favourite = null)
    {
        var result = new List<ProfileInfo>();
        foreach (var set in group.GroupBy(x => string.IsNullOrEmpty(x.AccountId) ? "n:" + x.Name : "a:" + x.AccountId))
        {
            var best = set.OrderByDescending(x => preferred != null && preferred.Contains(x.Name))
                          .ThenByDescending(x => favourite != null && x.Name == favourite)
                          .ThenBy(x => x.Name.Length)
                          .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).First();
            result.Add(best);
        }
        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The sign-in address reduced to what identifies it (its host), so two nicknames that differ only in letter case, or a trailing slash, count as one sign-in.</summary>
    public static string NormaliseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var u = url.Trim();
        try { if (Uri.TryCreate(u, UriKind.Absolute, out var uri)) return uri.Host.ToLowerInvariant(); } catch { }
        return u.TrimEnd('/', '#').ToLowerInvariant();
    }

    /// <summary>Identifies the primary account (the company sign-in, or an access-key login) a profile belongs to.</summary>
    public static string PrimaryKey(ProfileInfo p)
    {
        if (p.Kind != ProfileKind.Sso) return "key:" + p.Name;
        var host = NormaliseUrl(p.StartUrl);
        return "sso:" + (host.Length > 0 ? host : (p.SsoSession ?? p.Name).ToLowerInvariant());
    }

    /// <summary>Keys older versions used for friendly names (the sign-in's nickname or full address).</summary>
    public static IEnumerable<string> LegacyKeys(ProfileInfo p)
    {
        if (p.Kind != ProfileKind.Sso) yield break;
        if (!string.IsNullOrEmpty(p.SsoSession)) yield return "sso:" + p.SsoSession;
        if (!string.IsNullOrEmpty(p.StartUrl)) yield return "sso:" + p.StartUrl;
        yield return "sso:" + p.Name;
    }

    /// <summary>The friendly name of a primary account: the one the person gave it, else the sign-in's own name.</summary>
    public static string PrimaryLabel(AppConfig cfg, ProfileInfo p, IEnumerable<ProfileInfo> all)
    {
        var key = PrimaryKey(p);
        if (cfg.Aliases != null)
        {
            if (cfg.Aliases.TryGetValue(key, out var a) && !string.IsNullOrWhiteSpace(a)) return a;
            foreach (var m in all.Where(x => PrimaryKey(x) == key).Append(p))
                foreach (var k in LegacyKeys(m))
                    if (cfg.Aliases.TryGetValue(k, out var b) && !string.IsNullOrWhiteSpace(b)) return b;
        }
        return PrimaryName(p);
    }

    /// <summary>The address and region a named company sign-in (sso-session) is set up with.</summary>
    public static (string? StartUrl, string? SsoRegion) SessionInfo(string session)
    {
        var cfg = ReadIni(ConfigPath);
        if (cfg.TryGetValue("sso-session " + session, out var d))
            return (d.TryGetValue("sso_start_url", out var u) && u.Length > 0 ? u : null, d.TryGetValue("sso_region", out var r) && r.Length > 0 ? r : null);
        return (null, null);
    }

    /// <summary>A short friendly name for the primary account: the sign-in's nickname, or the company part of its address.</summary>
    public static string PrimaryName(ProfileInfo p)
    {
        if (p.Kind != ProfileKind.Sso) return p.Name;
        if (!string.IsNullOrWhiteSpace(p.SsoSession)) return p.SsoSession!;
        try { if (p.StartUrl != null) return new Uri(p.StartUrl).Host.Split('.')[0]; } catch { }
        return p.Name;
    }

    /// <summary>A valid, unused profile name made from an account's name.</summary>
    public static string ProfileNameFor(string accountName, string accountId, ISet<string> taken)
    {
        var slug = Regex.Replace(accountName ?? "", @"[^A-Za-z0-9_.@\-]+", "-").Trim('-');
        if (slug.Length == 0) slug = "account-" + accountId;
        if (slug.Length > 56) slug = slug.Substring(0, 56).Trim('-');
        var name = slug;
        if (taken.Contains(name)) name = slug + "-" + (accountId.Length >= 4 ? accountId.Substring(accountId.Length - 4) : accountId);
        return name;
    }

    // ---- writing -----------------------------------------------------------------------------
    public static void SaveSsoSession(string session, string startUrl, string ssoRegion, bool withScopes = true)
    {
        var values = new Dictionary<string, string> { ["sso_start_url"] = startUrl, ["sso_region"] = ssoRegion };
        if (withScopes) values["sso_registration_scopes"] = "sso:account:access";
        Upsert(ConfigPath, "sso-session " + session, values, withScopes ? null : new[] { "sso_registration_scopes" });
    }

    public static void SaveSsoProfile(string profile, string session, string accountId, string roleName) =>
        Upsert(ConfigPath, "profile " + profile, new()
        {
            ["sso_session"] = session,
            ["sso_account_id"] = accountId,
            ["sso_role_name"] = roleName,
            ["output"] = "json",
        });

    public static void SaveAccessKeys(string profile, string accessKeyId, string secret)
    {
        if (!Regex.IsMatch(accessKeyId, @"^[A-Z0-9]{16,128}$")) throw new ArgumentException("That does not look like an AWS access key ID (it is 20 letters and digits, starting with AKIA or ASIA).");
        if (!Regex.IsMatch(secret, @"^[A-Za-z0-9/+=]{30,128}$")) throw new ArgumentException("That does not look like an AWS secret access key (about 40 characters).");
        Upsert(CredentialsPath, profile, new() { ["aws_access_key_id"] = accessKeyId, ["aws_secret_access_key"] = secret });
        Upsert(ConfigPath, "profile " + profile, new() { ["output"] = "json" });
    }

    /// <summary>Remembers the region for a profile (so the AWS CLI and other tools agree with SSMConnect).</summary>
    public static void SaveRegion(string profile, string region) =>
        Upsert(ConfigPath, profile == "default" ? "default" : "profile " + profile, new() { ["region"] = region });

    /// <summary>Removes one account profile (and, for access keys, its stored keys) from the AWS files. A copy of each file is kept as .ssmconnect.bak.</summary>
    public static void RemoveProfile(string profile)
    {
        DeleteSection(ConfigPath, profile == "default" ? "default" : "profile " + profile);
        DeleteSection(CredentialsPath, profile);
    }

    /// <summary>Removes a company sign-in definition. Call only when no remaining profile uses it.</summary>
    public static void RemoveSsoSession(string session) => DeleteSection(ConfigPath, "sso-session " + session);

    static void DeleteSection(string path, string section)
    {
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path).ToList();
        var header = "[" + section + "]";
        var start = lines.FindIndex(l => l.Trim() == header);
        if (start < 0) return;
        try { File.Copy(path, path + ".ssmconnect.bak", overwrite: true); } catch { }
        var end = start + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith("[")) end++;
        lines.RemoveRange(start, end - start);
        while (start < lines.Count && start > 0 && lines[start - 1].Trim().Length == 0 && lines[start].Trim().Length == 0) lines.RemoveAt(start);
        var temp = path + ".tmp";
        File.WriteAllText(temp, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    // ---- ini helpers -------------------------------------------------------------------------
    static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path)) return result;
            Dictionary<string, string>? cur = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    var name = line.Substring(1, line.Length - 2).Trim();
                    if (!result.TryGetValue(name, out cur)) result[name] = cur = new(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                var eq = line.IndexOf('=');
                if (cur != null && eq > 0) cur[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }
        catch { /* unreadable file: treat as empty */ }
        return result;
    }

    static void Upsert(string path, string section, Dictionary<string, string> values, string[]? remove = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        foreach (var v in values.Values) if (v.Contains('\n') || v.Contains('\r')) throw new ArgumentException("Line breaks are not allowed in AWS settings.");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        if (File.Exists(path)) { try { File.Copy(path, path + ".ssmconnect.bak", overwrite: true); } catch { } }

        var header = "[" + section + "]";
        var start = lines.FindIndex(l => l.Trim() == header);
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add(header);
            foreach (var kv in values) lines.Add($"{kv.Key} = {kv.Value}");
        }
        else
        {
            var end = start + 1;
            while (end < lines.Count && !lines[end].TrimStart().StartsWith("[")) end++;
            if (remove != null)
                for (var i = end - 1; i > start; i--)
                {
                    var eq = lines[i].IndexOf('=');
                    if (eq > 0 && remove.Contains(lines[i].Substring(0, eq).Trim(), StringComparer.OrdinalIgnoreCase)) { lines.RemoveAt(i); end--; }
                }
            foreach (var kv in values)
            {
                var idx = -1;
                for (var i = start + 1; i < end; i++)
                {
                    var eq = lines[i].IndexOf('=');
                    if (eq > 0 && lines[i].Substring(0, eq).Trim().Equals(kv.Key, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                }
                var text = $"{kv.Key} = {kv.Value}";
                if (idx >= 0) lines[idx] = text;
                else { lines.Insert(end, text); end++; }
            }
        }
        var temp = path + ".tmp";
        File.WriteAllText(temp, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Region names offered when AWS cannot be asked (offline or not yet signed in).</summary>
public static class AwsRegions
{
    public static readonly string[] Common =
    {
        "us-east-1","us-east-2","us-west-1","us-west-2","ap-south-1","ap-south-2","ap-southeast-1","ap-southeast-2","ap-southeast-3",
        "ap-northeast-1","ap-northeast-2","ap-northeast-3","ap-east-1","ca-central-1","eu-central-1","eu-central-2","eu-west-1","eu-west-2",
        "eu-west-3","eu-north-1","eu-south-1","me-south-1","me-central-1","sa-east-1","af-south-1",
    };
}
