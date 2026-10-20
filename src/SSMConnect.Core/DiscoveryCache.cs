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


using System.Text.Json;

namespace SSMConnect.Core;

/// <summary>The servers found the last time one AWS account + region was scanned.</summary>
public sealed class ScanRecord
{
    public string Profile { get; set; } = "";
    public string Region { get; set; } = "";
    /// <summary>When the servers were last listed successfully.</summary>
    public DateTime ScannedUtc { get; set; }
    /// <summary>When a scan was last tried (a failed try, such as an expired sign-in, is repeated after an hour, not every minute).</summary>
    public DateTime AttemptedUtc { get; set; }
    public string LastError { get; set; } = "";
    public List<DiscoveredServer> Servers { get; set; } = new();
}

public sealed class DiscoveryState
{
    public List<ScanRecord> Scans { get; set; } = new();
    /// <summary>Differences found by a scan that the person has not looked at yet.</summary>
    public List<string> PendingChanges { get; set; } = new();
    public DateTime? ChangesFoundUtc { get; set; }
}

/// <summary>
/// Remembers what discovery found (discovery.json), so the server list is not lost between runs, and works out what
/// changed since the previous scan.
/// </summary>
public static class DiscoveryCache
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    static readonly object Gate = new();
    public static string Path => System.IO.Path.Combine(ConfigStore.DataDir, "discovery.json");

    public static DiscoveryState Load()
    {
        lock (Gate)
        {
            try { return File.Exists(Path) ? JsonSerializer.Deserialize<DiscoveryState>(File.ReadAllText(Path), Options) ?? new() : new(); }
            catch { return new(); }
        }
    }

    static void Save(DiscoveryState s)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.DataDir);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Options));
            File.Move(tmp, Path, overwrite: true);
        }
        catch (Exception ex) { AppLog.Warn("Could not save the server cache: " + ex.Message); }
    }

    public static ScanRecord? Get(string profile, string region) =>
        Load().Scans.FirstOrDefault(x => Same(x, profile, region));

    static bool Same(ScanRecord r, string profile, string region) =>
        string.Equals(r.Profile, profile, StringComparison.OrdinalIgnoreCase) && string.Equals(r.Region, region, StringComparison.OrdinalIgnoreCase);

    /// <summary>Stores a successful scan. Returns the differences from the previous scan (empty for the very first scan of an account).</summary>
    public static List<string> Update(string profile, string region, List<DiscoveredServer> servers, ISet<string>? configuredIds = null, bool recordChanges = true)
    {
        lock (Gate)
        {
            var state = Load();
            var old = state.Scans.FirstOrDefault(x => Same(x, profile, region));
            var changes = old == null || old.ScannedUtc == default ? new List<string>() : Diff(old.Servers, servers, profile, region, configuredIds);
            if (old == null) { old = new ScanRecord { Profile = profile, Region = region }; state.Scans.Add(old); }
            old.Servers = servers;
            old.ScannedUtc = old.AttemptedUtc = DateTime.UtcNow;
            old.LastError = "";
            if (recordChanges && changes.Count > 0)
            {
                state.PendingChanges.AddRange(changes);
                state.ChangesFoundUtc = DateTime.UtcNow;
            }
            Save(state);
            return changes;
        }
    }

    /// <summary>Drops everything remembered about an account (used when the account is removed).</summary>
    public static void Forget(string profile)
    {
        lock (Gate)
        {
            var state = Load();
            if (state.Scans.RemoveAll(x => string.Equals(x.Profile, profile, StringComparison.OrdinalIgnoreCase)) > 0) Save(state);
        }
    }

    public static void RecordFailure(string profile, string region, string error)
    {
        lock (Gate)
        {
            var state = Load();
            var r = state.Scans.FirstOrDefault(x => Same(x, profile, region));
            if (r == null) { r = new ScanRecord { Profile = profile, Region = region }; state.Scans.Add(r); }
            r.AttemptedUtc = DateTime.UtcNow;
            r.LastError = error;
            Save(state);
        }
    }

    public static List<string> TakePending()
    {
        lock (Gate)
        {
            var state = Load();
            var list = state.PendingChanges.ToList();
            state.PendingChanges.Clear();
            state.ChangesFoundUtc = null;
            Save(state);
            return list;
        }
    }

    public static List<string> Diff(List<DiscoveredServer> before, List<DiscoveredServer> after, string profile, string region, ISet<string>? configuredIds)
    {
        var acct = $"[{profile} / {region}] ";
        string Tag(DiscoveredServer s) => configuredIds != null && configuredIds.Contains(s.InstanceId) ? " (you have a connection for it)" : "";
        var changes = new List<string>();
        var was = before.ToDictionary(s => s.InstanceId);
        var now = after.ToDictionary(s => s.InstanceId);

        foreach (var s in after.Where(s => !was.ContainsKey(s.InstanceId)))
            changes.Add($"{acct}New server: {s.Name} ({s.InstanceId}, {s.Platform}){(s.SsmOnline ? "" : ", not reachable through SSM yet")}");
        foreach (var s in before.Where(s => !now.ContainsKey(s.InstanceId)))
            changes.Add($"{acct}No longer running: {s.Name} ({s.InstanceId}){Tag(s)}");
        foreach (var n in after)
        {
            if (!was.TryGetValue(n.InstanceId, out var o)) continue;
            if (o.SsmOnline != n.SsmOnline)
                changes.Add($"{acct}{n.Name}: {(n.SsmOnline ? "is now reachable through SSM" : "can no longer be reached through SSM")}{Tag(n)}");
            if (!string.Equals(o.KeyName, n.KeyName, StringComparison.Ordinal))
                changes.Add($"{acct}{n.Name}: key pair changed from '{o.KeyName}' to '{n.KeyName}'. The saved key file may no longer work{Tag(n)}");
            if (!string.Equals(o.Name, n.Name, StringComparison.Ordinal))
                changes.Add($"{acct}Server renamed: '{o.Name}' is now '{n.Name}'{Tag(n)}");
            if (!string.Equals(o.PrivateIp, n.PrivateIp, StringComparison.Ordinal))
                changes.Add($"{acct}{n.Name}: private IP changed from {o.PrivateIp} to {n.PrivateIp}");
            if (!string.IsNullOrEmpty(o.OsName) && !string.IsNullOrEmpty(n.OsName) && !string.Equals(o.OsName, n.OsName, StringComparison.OrdinalIgnoreCase))
                changes.Add($"{acct}{n.Name}: operating system changed from {o.OsName} to {n.OsName}. The login user may differ{Tag(n)}");
        }
        return changes;
    }
}

/// <summary>Once-a-day passive rescan of every account SSMConnect knows about.</summary>
public static class DiscoveryMonitor
{
    /// <summary>True when this account has not been scanned today (local time), and no failed try happened within the last hour.</summary>
    public static bool IsDue(ScanRecord? r, DateTime nowLocal)
    {
        if (r == null) return true;
        if (r.ScannedUtc != default && r.ScannedUtc.ToLocalTime().Date == nowLocal.Date) return false;
        if (!string.IsNullOrEmpty(r.LastError) && DateTime.UtcNow - r.AttemptedUtc < TimeSpan.FromHours(1)) return false;
        return true;
    }

    /// <summary>The accounts to watch: those that have saved connections, plus the one last used in setup.</summary>
    public static List<(string Profile, string Region)> Accounts(AppConfig cfg)
    {
        var list = cfg.Tunnels.Where(t => !string.IsNullOrWhiteSpace(t.Profile) && !string.IsNullOrWhiteSpace(t.Region))
                              .Select(t => (t.Profile, t.Region)).ToList();
        if (!string.IsNullOrWhiteSpace(cfg.Profile) && !string.IsNullOrWhiteSpace(cfg.Region)) list.Add((cfg.Profile, cfg.Region));
        // every other AWS account under the same company sign-in is watched too, not only those that already have saved connections
        try
        {
            var profiles = AwsProfiles.List();
            var preferred = cfg.Tunnels.Select(t => t.Profile).ToHashSet(StringComparer.Ordinal);
            foreach (var (profile, region) in list.ToList())
            {
                var p = profiles.FirstOrDefault(x => x.Name == profile);
                if (p == null || p.Kind != ProfileKind.Sso) continue;
                foreach (var m in AwsProfiles.SameSignIn(p, profiles, preferred)) list.Add((m.Name, region));
            }
        }
        catch (Exception ex) { AppLog.Warn("Could not list the organization accounts to watch: " + ex.Message); }
        return list.Distinct().ToList();
    }

    /// <summary>
    /// Scans every watched account that is due (all of them when <paramref name="force"/>). Never prompts and never opens
    /// a browser: an expired sign-in is simply skipped and tried again later. Returns the new differences found.
    /// </summary>
    static DateTime _lastSync = DateTime.MinValue;

    /// <summary>Company sign-ins that had expired, so their organization accounts could not be checked in the last run.</summary>
    public static IReadOnlyList<string> NeedSignIn { get; private set; } = Array.Empty<string>();

    public static async Task<List<string>> RunAsync(AppConfig cfg, bool force, CancellationToken ct = default)
    {
        var all = new List<string>();
        // first look for AWS accounts added under the existing sign-ins (organizations), once a day or on a manual scan
        if (force || _lastSync.Date != DateTime.Now.Date)
        {
            try
            {
                var sync = await AwsSso.SyncMembersAsync(ct);
                NeedSignIn = sync.NeedSignIn;
                if (sync.NeedSignIn.Count == 0) _lastSync = DateTime.Now;
                foreach (var a in sync.Added) all.Add("New AWS account found under your sign-in: " + a);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { AppLog.Warn("Looking for new AWS accounts failed: " + ex.Message); }
        }
        var configured = cfg.Tunnels.Select(t => t.InstanceId).ToHashSet(StringComparer.Ordinal);
        foreach (var (profile, region) in Accounts(cfg))
        {
            if (ct.IsCancellationRequested) break;
            if (!force && !IsDue(DiscoveryCache.Get(profile, region), DateTime.Now)) continue;
            try
            {
                var servers = await AwsCli.DiscoverAsync(new AppConfig { Profile = profile, Region = region }, ct);
                all.AddRange(DiscoveryCache.Update(profile, region, servers, configured));
                AppLog.Info($"Server scan {profile}/{region}: {servers.Count} servers.");
            }
            catch (Exception ex)
            {
                DiscoveryCache.RecordFailure(profile, region, ex.Message);
                AppLog.Warn($"Server scan {profile}/{region} skipped: {ex.Message}");
            }
        }
        return all;
    }
}
