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


using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SSMConnect.Core;

public sealed record SsoAccount(string Id, string Name)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name}  ({Id})";
}

/// <summary>Guided company-sign-in (IAM Identity Center) setup: sign in, then list the accounts and roles the person may use.</summary>
public static class AwsSso
{
    /// <summary>Opens the browser sign-in for a named sso-session.</summary>
    public static Task<CliResult> LoginAsync(string session, CancellationToken ct = default) =>
        AwsCli.RunAsync(new AppConfig { Profile = "", Region = "" }, new[] { "sso", "login", "--sso-session", session }, TimeSpan.FromMinutes(3), ct);

    /// <summary>Reads the access token the AWS CLI cached after sign-in (file name = SHA-1 of the session name).</summary>
    public static string? ReadAccessToken(string session)
    {
        try
        {
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(session))).ToLowerInvariant();
            var file = Path.Combine(AwsProfiles.AwsDir, "sso", "cache", hash + ".json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            if (root.TryGetProperty("expiresAt", out var exp) && DateTimeOffset.TryParse(exp.GetString(), out var when) && when < DateTimeOffset.UtcNow) return null;
            return root.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
        }
        catch { return null; }
    }

    public static async Task<List<SsoAccount>> ListAccountsAsync(string token, string ssoRegion, CancellationToken ct = default)
    {
        var r = await AwsCli.RunAsync(new AppConfig { Profile = "", Region = "" },
            new[] { "sso", "list-accounts", "--access-token", token, "--region", ssoRegion, "--query", "accountList[].[accountId,accountName]", "--output", "json" },
            TimeSpan.FromSeconds(60), ct);
        if (!r.Ok) throw new InvalidOperationException(AwsCli.FriendlyError(r.Error));
        var list = new List<SsoAccount>();
        using var doc = JsonDocument.Parse(r.Output);
        foreach (var row in doc.RootElement.EnumerateArray())
            list.Add(new SsoAccount(row[0].GetString() ?? "", row.GetArrayLength() > 1 ? row[1].GetString() ?? "" : ""));
        return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Saves one profile for every account the person can use under this company sign-in, so all of them can be searched for servers.
    /// Accounts that already have a profile are reused. The same role is used everywhere it exists, otherwise the account's first role.
    /// </summary>
    public static async Task<List<(string Profile, SsoAccount Account)>> AddAllAccountsAsync(string session, string token, string ssoRegion, string preferredRole,
        IReadOnlyList<SsoAccount> accounts, Action<string>? progress = null, CancellationToken ct = default)
    {
        var all = AwsProfiles.List();
        // accounts that already have a profile under the same sign-in are reused, even when an older profile used another nickname for it
        var sess = AwsProfiles.SessionInfo(session);
        var key = "sso:" + AwsProfiles.NormaliseUrl(sess.StartUrl);
        var existing = all.Where(p => p.Kind == ProfileKind.Sso && p.AccountId != null &&
                                      (p.SsoSession == session || (sess.StartUrl != null && AwsProfiles.PrimaryKey(p) == key))).ToList();
        var taken = new HashSet<string>(all.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Profile, SsoAccount Account)>();
        var i = 0;
        foreach (var a in accounts)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke($"Setting up account {++i} of {accounts.Count}: {a.Name}...");
            var have = existing.FirstOrDefault(p => p.AccountId == a.Id);
            if (have != null) { result.Add((have.Name, a)); continue; }
            List<string> roles;
            try { roles = await ListRolesAsync(token, ssoRegion, a.Id, ct); }
            catch { continue; }   // no role in this account for this person: skip it
            if (roles.Count == 0) continue;
            var role = roles.Contains(preferredRole) ? preferredRole : roles[0];
            var name = AwsProfiles.ProfileNameFor(a.Name, a.Id, taken);
            AwsProfiles.SaveSsoProfile(name, session, a.Id, role);
            taken.Add(name);
            result.Add((name, a));
        }
        return result;
    }

    /// <summary>What a silent look for new organization accounts found.</summary>
    public sealed record SyncResult(List<string> Added, List<string> NeedSignIn);

    /// <summary>
    /// Looks, for every company sign-in that is still signed in, for AWS accounts the person gained access to since the last time,
    /// and saves a profile for each. Never opens a browser: a sign-in that has expired is reported in NeedSignIn instead.
    /// </summary>
    public static async Task<SyncResult> SyncMembersAsync(CancellationToken ct = default)
    {
        var added = new List<string>();
        var need = new List<string>();
        var all = AwsProfiles.List();
        foreach (var g in all.Where(p => p.Kind == ProfileKind.Sso && !string.IsNullOrEmpty(p.SsoSession)).GroupBy(AwsProfiles.PrimaryKey).ToList())
        {
            ct.ThrowIfCancellationRequested();
            string? session = null, token = null;
            foreach (var s in g.Select(p => p.SsoSession!).Distinct(StringComparer.Ordinal))
            {
                token = ReadAccessToken(s);
                if (token != null) { session = s; break; }
            }
            var label = AwsProfiles.PrimaryName(g.First());
            if (token == null || session == null) { need.Add(label); continue; }
            try
            {
                var region = AwsProfiles.SessionInfo(session).SsoRegion;
                if (string.IsNullOrEmpty(region)) continue;
                var accounts = await ListAccountsAsync(token, region, ct);
                var known = g.Where(p => p.AccountId != null).Select(p => p.AccountId!).ToHashSet(StringComparer.Ordinal);
                var fresh = accounts.Where(a => !known.Contains(a.Id)).ToList();
                if (fresh.Count == 0) continue;
                var role = g.Select(p => p.RoleName).FirstOrDefault(r => !string.IsNullOrEmpty(r)) ?? "";
                var made = await AddAllAccountsAsync(session, token, region, role, fresh, null, ct);
                added.AddRange(made.Select(m => $"{m.Account.Name} ({m.Account.Id})"));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { AppLog.Warn("Could not look for new AWS accounts under " + label + ": " + ex.Message); }
        }
        return new SyncResult(added, need);
    }

    public static async Task<List<string>> ListRolesAsync(string token, string ssoRegion, string accountId, CancellationToken ct = default)
    {
        var r = await AwsCli.RunAsync(new AppConfig { Profile = "", Region = "" },
            new[] { "sso", "list-account-roles", "--access-token", token, "--region", ssoRegion, "--account-id", accountId, "--query", "roleList[].roleName", "--output", "json" },
            TimeSpan.FromSeconds(60), ct);
        if (!r.Ok) throw new InvalidOperationException(AwsCli.FriendlyError(r.Error));
        using var doc = JsonDocument.Parse(r.Output);
        return doc.RootElement.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).OrderBy(s => s).ToList();
    }
}
