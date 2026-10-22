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

using System.Drawing;
using Renci.SshNet;
using Renci.SshNet.Common;
using SSMConnect.Core;

namespace SSMConnect.Wizard;

/// <summary>
/// Makes sure a green server can really be logged in to before PuTTY, WinSCP, MobaXterm or the file browser opens:
/// finds the key that belongs to the server (saved path, then its AWS key pair by fingerprint and name), tests which
/// login user the server accepts (its saved user first, then the user its operating system normally uses), saves what works
/// and refreshes the saved PuTTY / WinSCP sessions. When nothing works the person is asked for the right key file once.
/// </summary>
static class LoginCheck
{
    enum Kind { Ok, Unchecked, NeedsKey, Rejected, Unreachable }
    sealed record Outcome(Kind Kind, string User, string KeyPath, string Detail, List<string> Tried);

    static readonly Dictionary<string, DateTime> Verified = new();
    static readonly object Gate = new();
    static List<KeyFile>? _keys;
    static DateTime _keysAt;

    static List<KeyFile> Keys()
    {
        lock (Gate)
        {
            if (_keys == null || DateTime.UtcNow - _keysAt > TimeSpan.FromMinutes(5))
            {
                _keys = KeyFinder.ScanStandard(false, TimeSpan.FromSeconds(8));
                _keysAt = DateTime.UtcNow;
            }
            return _keys;
        }
    }

    static DiscoveredServer? Known(TunnelConfig t)
    {
        try { return DiscoveryCache.Load().Scans.SelectMany(x => x.Servers).FirstOrDefault(x => x.InstanceId == t.InstanceId); }
        catch { return null; }
    }

    static List<string> CandidateKeys(TunnelConfig t, DiscoveredServer? d)
    {
        var list = new List<string>();
        string? saved = !string.IsNullOrWhiteSpace(t.KeyFile) && File.Exists(t.KeyFile) ? t.KeyFile : null;
        string? found = null;
        // a saved key that is provably not this server's key goes after the one that is
        var savedWrong = false;
        if (saved != null && d != null && !string.IsNullOrWhiteSpace(d.KeyFingerprint))
        {
            try { var pr = KeyFingerprint.Compute(saved); savedWrong = pr.Count > 0 && !pr.Contains(d.KeyFingerprint); } catch { }
        }
        // the disk is only searched when no key is saved, or the saved one provably belongs to another server
        if (d != null && (saved == null || savedWrong))
        {
            try { found = KeyFinder.Find(Keys(), d.KeyName, d.KeyFingerprint).Key?.Path; } catch { }
        }
        if (savedWrong && found != null && !string.Equals(saved, found, StringComparison.OrdinalIgnoreCase)) { list.Add(found!); list.Add(saved!); }
        else
        {
            if (saved != null) list.Add(saved);
            if (found != null && !list.Contains(found, StringComparer.OrdinalIgnoreCase)) list.Add(found);
        }
        return list;
    }

    static List<string> CandidateUsers(TunnelConfig t, DiscoveredServer? d)
    {
        var u = new List<string>();
        void Add(string? x) { if (!string.IsNullOrWhiteSpace(x) && !x!.Equals("root", StringComparison.OrdinalIgnoreCase) && !u.Contains(x, StringComparer.OrdinalIgnoreCase)) u.Add(x); }
        Add(t.UserName);
        Add(d?.SuggestedUser);
        // when the operating system is known, only its own default user is worth trying; guessing other names would only
        // add failed logins to the server's log
        if (d == null || string.IsNullOrWhiteSpace(d.OsName))
            foreach (var x in new[] { "ec2-user", "ubuntu", "admin", "rocky", "centos" }) Add(x);
        return u.Take(5).ToList();
    }

    /// <summary>Instance|user|key combinations the server refused, so they are not tried again for 10 minutes.</summary>
    static readonly Dictionary<string, DateTime> Refused = new();

    static string RefKey(TunnelConfig t, string user, string key) => $"{t.InstanceId}|{user}|{key}|{File.GetLastWriteTimeUtc(key).Ticks}";

    static bool WasRefused(TunnelConfig t, string user, string key)
    {
        try { lock (Gate) return Refused.TryGetValue(RefKey(t, user, key), out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(10); }
        catch { return false; }
    }

    static (bool Ok, bool Auth, string Err) Try(int port, string user, PrivateKeyFile pk)
    {
        try
        {
            using var c = new SshClient(new ConnectionInfo("127.0.0.1", port, user, new PrivateKeyAuthenticationMethod(user, pk)) { Timeout = TimeSpan.FromSeconds(15) });
            c.Connect();
            c.Disconnect();
            return (true, false, "");
        }
        catch (SshAuthenticationException ex) { return (false, true, ex.Message); }
        catch (Exception ex) { return (false, false, ex.Message); }
    }

    static Outcome Check(TunnelConfig t)
    {
        var d = Known(t);
        var keys = CandidateKeys(t, d);
        if (keys.Count == 0) return new(Kind.NeedsKey, t.UserName, "", "", new());
        var users = CandidateUsers(t, d);
        var tried = new List<string>();
        string? unreadable = null;
        var anyReadable = false;
        foreach (var key in keys)
        {
            PrivateKeyFile pk;
            try { pk = new PrivateKeyFile(key); }
            catch { unreadable ??= key; continue; }   // passphrase-protected or a format the check cannot read (.ppk)
            anyReadable = true;
            using (pk)
            {
                foreach (var user in users)
                {
                    if (WasRefused(t, user, key)) { if (!tried.Contains(user)) tried.Add(user); continue; }
                    var r = Try(t.LocalPort, user, pk);
                    if (!r.Ok && !r.Auth) { Thread.Sleep(2000); r = Try(t.LocalPort, user, pk); }   // the tunnel may have only just opened
                    if (r.Ok) return new(Kind.Ok, user, key, "", tried);
                    if (!r.Auth) return new(Kind.Unreachable, t.UserName, key, r.Err, tried);
                    try { lock (Gate) Refused[RefKey(t, user, key)] = DateTime.UtcNow; } catch { }
                    if (!tried.Contains(user)) tried.Add(user);
                }
            }
        }
        if (!anyReadable && unreadable != null) return new(Kind.Unchecked, t.UserName, unreadable, "", tried);
        return new(Kind.Rejected, t.UserName, keys[0], "", tried);
    }

    static string? Sig(TunnelConfig t)
    {
        try { return string.IsNullOrWhiteSpace(t.KeyFile) || !File.Exists(t.KeyFile) ? null : $"{t.InstanceId}|{t.LocalPort}|{t.UserName}|{t.KeyFile}|{File.GetLastWriteTimeUtc(t.KeyFile).Ticks}"; }
        catch { return null; }
    }

    /// <summary>Saves only this server's user and key into the config on disk, so nothing else changed meanwhile is overwritten.</summary>
    static void SaveLogin(TunnelConfig t, AppConfig cfg)
    {
        try
        {
            var c = ConfigStore.Load() ?? cfg;
            var m = c.Tunnels.FirstOrDefault(x => x.InstanceId == t.InstanceId && x.Profile == t.Profile) ?? c.Tunnels.FirstOrDefault(x => x.InstanceId == t.InstanceId && x.LocalPort == t.LocalPort);
            if (m == null) { ConfigStore.Save(cfg); return; }
            m.UserName = t.UserName;
            m.KeyFile = t.KeyFile;
            ConfigStore.Save(c);
        }
        catch (Exception ex) { AppLog.Warn("Could not save the corrected login: " + ex.Message); }
    }

    /// <summary>A small "Checking login..." window so the wait never looks like a freeze.</summary>
    static Form? ShowBusy(IWin32Window? owner, string text)
    {
        try
        {
            var f = new Form
            {
                Text = "SSMConnect", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                ControlBox = false, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(380, 72),
            };
            f.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
            if (owner is Form of) f.Show(of); else f.Show();
            f.Refresh();
            return f;
        }
        catch { return null; }
    }

    /// <summary>Rewrites this server's saved PuTTY and WinSCP sessions from the current user and key.</summary>
    static void RefreshSessions(TunnelConfig t)
    {
        try { ToolConfigurator.ConfigurePutty(new[] { t }); } catch (Exception ex) { AppLog.Warn("PuTTY session refresh failed: " + ex.Message); }
        try { ToolConfigurator.ConfigureWinScp(new[] { t }); } catch (Exception ex) { AppLog.Warn("WinSCP session refresh failed: " + ex.Message); }
    }

    /// <summary>True when the server can be logged in to (or could not be tested but nothing is known to be wrong).</summary>
    public static async Task<bool> EnsureAsync(IWin32Window? owner, TunnelConfig t, AppConfig cfg)
    {
        for (var round = 0; round < 4; round++)
        {
            var sig = Sig(t);
            if (sig != null && Verified.TryGetValue(sig, out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(30)) { RefreshSessions(t); return true; }

            var busy = ShowBusy(owner, $"Checking the login to \"{t.Name}\"...");
            Outcome o;
            try { o = await Task.Run(() => Check(t)); }
            finally { try { busy?.Close(); busy?.Dispose(); } catch { } }
            switch (o.Kind)
            {
                case Kind.Ok:
                case Kind.Unchecked:
                    var changed = false;
                    if (!string.Equals(o.User, t.UserName, StringComparison.Ordinal)) { AppLog.Info($"Login check: {t.Name} accepts user '{o.User}' (was '{t.UserName}'). Saved."); t.UserName = o.User; changed = true; }
                    if (!string.Equals(o.KeyPath, t.KeyFile, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(o.KeyPath)) { AppLog.Info($"Login check: {t.Name} uses key {o.KeyPath}. Saved."); t.KeyFile = o.KeyPath; changed = true; }
                    if (changed) SaveLogin(t, cfg);
                    RefreshSessions(t);
                    if (o.Kind == Kind.Ok) { var s2 = Sig(t); if (s2 != null) Verified[s2] = DateTime.UtcNow; }
                    return true;

                case Kind.Unreachable:
                    AppLog.Warn($"Login check: {t.Name} did not answer on its local port: {o.Detail}");
                    return MessageBox.Show(owner, $"The secure connection to \"{t.Name}\" is open, but the server did not answer the SSH login.\n\nCheck that the SSH service is running on the server and that its port 22 is not blocked on the server itself.\n\n{o.Detail}\n\nOpen the tool anyway?", "SSMConnect", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

                default:
                    var d = Known(t);
                    var keyName = string.IsNullOrWhiteSpace(d?.KeyName) ? "" : $" (AWS key pair \"{d!.KeyName}\")";
                    var msg = o.Kind == Kind.NeedsKey
                        ? $"No key file is saved for \"{t.Name}\"{keyName}, and none was found on this PC.\n\nChoose its key file now? It is saved, so you only do this once."
                        : $"\"{t.Name}\" is connected, but the server did not accept the key {Path.GetFileName(o.KeyPath)} for the users {string.Join(", ", o.Tried)}.\n\nThe key probably belongs to a different server{keyName}. Choose the right key file now?";
                    if (MessageBox.Show(owner, msg, "SSMConnect", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
                    var picked = PickKey(owner, t, d);
                    if (picked == null) return false;
                    t.KeyFile = picked;
                    SaveLogin(t, cfg);
                    break;
            }
        }
        return false;
    }

    static string? PickKey(IWin32Window? owner, TunnelConfig t, DiscoveredServer? d)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var dlg = new OpenFileDialog
        {
            Title = "Choose the key file for " + t.Name + (string.IsNullOrWhiteSpace(d?.KeyName) ? "" : "  (AWS key pair: " + d!.KeyName + ")"),
            Filter = "SSH key files (*.pem;*.ppk;*.key)|*.pem;*.ppk;*.key|All files|*.*",
            InitialDirectory = Directory.Exists(Path.Combine(home, ".ssh")) ? Path.Combine(home, ".ssh") : Path.Combine(home, "Downloads"),
        };
        return dlg.ShowDialog(owner as IWin32Window) == DialogResult.OK ? dlg.FileName : null;
    }
}
