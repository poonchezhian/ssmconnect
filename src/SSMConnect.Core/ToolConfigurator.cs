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
using Microsoft.Win32;

namespace SSMConnect.Core;

/// <summary>
/// Creates (and removes) saved sessions in PuTTY, WinSCP and MobaXterm that point at the local SSM tunnel ports.
/// Every session we create is prefixed with "SSC - " so cleanup never touches the user's own sessions.
/// </summary>
public static class ToolConfigurator
{
    public const string Prefix = "SSC - ";
    /// <summary>Name prefix used by earlier versions; those sessions are removed whenever sessions are refreshed or cleaned up.</summary>
    public const string LegacyPrefix = "SSMConnect - ";
    const string PuttyKey = @"Software\SimonTatham\PuTTY\Sessions";
    const string WinScpKey = @"Software\Martin Prikryl\WinSCP 2\Sessions";

    public static string SessionName(TunnelConfig t)
    {
        var clean = new string(t.Name.Select(c => "\\/:*?\"<>|".Contains(c) ? '-' : c).ToArray()).Trim();
        return Prefix + (clean.Length == 0 ? t.InstanceId : clean);
    }

    /// <summary>
    /// After a reinstall with the saved settings kept, the PuTTY / WinSCP sessions may be gone (uninstall removes them).
    /// Recreates the missing ones from the saved servers. Tools that were never used on this PC are left alone.
    /// </summary>
    public static void RestoreMissingSessions(IEnumerable<TunnelConfig> tunnels)
    {
        var ssh = tunnels.Where(t => !t.IsRdp).ToList();
        if (ssh.Count == 0) return;
        foreach (var (root, apply) in new (string, Action<IEnumerable<TunnelConfig>>)[] { (PuttyKey, ConfigurePutty), (WinScpKey, ConfigureWinScp) })
        {
            try
            {
                string[] have;
                using (var k = Registry.CurrentUser.OpenSubKey(root)) { if (k == null) continue; have = k.GetSubKeyNames(); }
                var missing = ssh.Where(t => !have.Contains(Munge(SessionName(t)), StringComparer.OrdinalIgnoreCase)).ToList();
                if (missing.Count > 0) apply(missing);
                // sessions created by an older version get the keep-alive setting too (only where it was never set)
                foreach (var t in ssh.Except(missing))
                {
                    using var sk = Registry.CurrentUser.OpenSubKey(root + "\\" + Munge(SessionName(t)), writable: true);
                    if (sk == null) continue;
                    if (root == PuttyKey && sk.GetValue("PingIntervalSecs") == null) sk.SetValue("PingIntervalSecs", 60, RegistryValueKind.DWord);
                    if (root == WinScpKey && sk.GetValue("PingType") == null) { sk.SetValue("PingType", 1, RegistryValueKind.DWord); sk.SetValue("PingInterval", 60, RegistryValueKind.DWord); }
                }
            }
            catch { /* best effort */ }
        }
    }

    // ---- PuTTY -------------------------------------------------------------------------------
    public static void ConfigurePutty(IEnumerable<TunnelConfig> tunnels)
    {
        foreach (var t in tunnels.Where(x => !x.IsRdp))
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{PuttyKey}\{Munge(SessionName(t))}");
            k.SetValue("HostName", "127.0.0.1");
            k.SetValue("PortNumber", t.LocalPort, RegistryValueKind.DWord);
            k.SetValue("Protocol", "ssh");
            k.SetValue("UserName", t.UserName);
            k.SetValue("UserNameFromEnvironment", 0, RegistryValueKind.DWord); // always use the stored login user
            k.SetValue("PingIntervalSecs", 60, RegistryValueKind.DWord); // keep-alive: stops AWS closing the tunnel when the session is idle
            // PuTTY only reads .ppk files; a .pem is converted with PuTTYgen when that is installed
            if (!string.IsNullOrWhiteSpace(t.KeyFile) && KeyConverter.EnsurePpk(t.KeyFile!) is { } ppk) k.SetValue("PublicKeyFile", ppk);
        }
    }

    // ---- WinSCP ------------------------------------------------------------------------------
    public static void ConfigureWinScp(IEnumerable<TunnelConfig> tunnels)
    {
        foreach (var t in tunnels.Where(x => !x.IsRdp))
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{WinScpKey}\{Munge(SessionName(t))}");
            k.SetValue("HostName", "127.0.0.1");
            k.SetValue("PortNumber", t.LocalPort, RegistryValueKind.DWord);
            k.SetValue("UserName", t.UserName);
            k.SetValue("FSProtocol", 2, RegistryValueKind.DWord); // SFTP only
            k.SetValue("PingType", 1, RegistryValueKind.DWord); k.SetValue("PingInterval", 60, RegistryValueKind.DWord); // keep-alive, as above
            if (!string.IsNullOrWhiteSpace(t.KeyFile)) k.SetValue("PublicKeyFile", KeyConverter.EnsurePpk(t.KeyFile!) ?? t.KeyFile!);
        }
    }

    // ---- MobaXterm (best effort: edits MobaXterm.ini after taking a backup) -----------------
    public static string MobaIniPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobaXterm", "MobaXterm.ini");

    public static bool ConfigureMobaXterm(IEnumerable<TunnelConfig> tunnels)
    {
        var path = MobaIniPath;
        if (!File.Exists(path)) return false;

        var (lines, enc, bom) = ReadIni(path);
        File.Copy(path, path + ".ssmconnect.bak", overwrite: true);
        var wanted = tunnels.Where(x => !x.IsRdp).ToList();
        RemoveOurMobaEntries(lines, keep: wanted.Select(SessionName).ToHashSet(StringComparer.Ordinal)); // only sessions no longer wanted

        var section = FindOurMobaSection(lines);
        if (section < 0)
        {
            var next = lines.Select(l => l.StartsWith("[Bookmarks_", StringComparison.OrdinalIgnoreCase) &&
                                         int.TryParse(l.Trim('[', ']').Substring("Bookmarks_".Length), out var n) ? n : 0)
                            .DefaultIfEmpty(0).Max() + 1;
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
            lines.Add($"[Bookmarks_{next}]");
            lines.Add("SubRep=SSMConnect");
            lines.Add("ImgNum=41");
            section = lines.Count - 3;
        }

        var insertAt = section + 1;
        while (insertAt < lines.Count && !lines[insertAt].StartsWith("[")) insertAt++;
        foreach (var t in wanted)
        {
            var key = SessionName(t) + "=";
            var idx = lines.FindIndex(l => l.StartsWith(key, StringComparison.Ordinal));
            if (idx >= 0)
            {
                // Update host, port and user in place so anything the person changed in MobaXterm survives.
                var parts = lines[idx].Substring(key.Length).Split('%');
                if (parts.Length > 3)
                {
                    parts[1] = "127.0.0.1"; parts[2] = t.LocalPort.ToString(); parts[3] = t.UserName;
                    if (!string.IsNullOrWhiteSpace(t.KeyFile) && parts.Length > 14) parts[14] = t.KeyFile!; // MobaXterm reads .pem and .ppk
                    lines[idx] = key + string.Join("%", parts);
                    continue;
                }
                lines.RemoveAt(idx); if (idx < insertAt) insertAt--;
            }
            lines.Insert(insertAt++, $"{key}#109#0%127.0.0.1%{t.LocalPort}%{t.UserName}%%-1%-1%%%%%0%0%0%{t.KeyFile}%%-1%0%0%0%%1080%%0%0%1#MobaXterm classic#-1#0# #-1");
        }
        WriteIni(path, lines, enc, bom);
        return true;
    }

    static int FindOurMobaSection(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("[Bookmarks", StringComparison.OrdinalIgnoreCase)) continue;
            for (var j = i + 1; j < lines.Count && !lines[j].StartsWith("["); j++)
                if (lines[j].Trim().Equals("SubRep=SSMConnect", StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    static void RemoveOurMobaEntries(List<string> lines, HashSet<string>? keep = null) =>
        lines.RemoveAll(l => (l.StartsWith(Prefix, StringComparison.Ordinal) || l.StartsWith(LegacyPrefix, StringComparison.Ordinal)) &&
                             (keep == null || !keep.Contains(l.Substring(0, Math.Max(0, l.IndexOf('=')))))); 

    /// <summary>
    /// MobaXterm.ini is normally Windows-1252 (ANSI). It is read as UTF-8 only when the bytes are valid UTF-8
    /// (or carry a BOM); otherwise as Windows-1252, so existing non-ASCII characters are written back unchanged.
    /// </summary>
    static (List<string> Lines, Encoding Enc, bool Bom) ReadIni(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text;
        Encoding enc;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            enc = new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            enc = Encoding.GetEncoding(1252);
            text = enc.GetString(bytes);
            bom = false;
        }
        return (text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList(), enc, bom);
    }

    static void WriteIni(string path, List<string> lines, Encoding enc, bool bom)
    {
        var body = enc.GetBytes(string.Join("\r\n", lines));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        if (bom) fs.Write(new byte[] { 0xEF, 0xBB, 0xBF });
        fs.Write(body);
    }

    // ---- Cleanup -----------------------------------------------------------------------------
    /// <summary>Removes every session SSMConnect created, in all three tools. Safe to call repeatedly.</summary>
    public static void RemoveAll() => RemoveExcept(null);

    /// <summary>
    /// Removes SSMConnect sessions that are not in <paramref name="keep"/> (all of them when null).
    /// Sessions that stay are left alone, so settings the person added in PuTTY / WinSCP survive.
    /// </summary>
    public static void RemoveExcept(IEnumerable<TunnelConfig>? keep)
    {
        var keepNames = keep?.Select(t => Munge(SessionName(t))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prefixes = new[] { Munge(Prefix), Munge(LegacyPrefix) };
        foreach (var root in new[] { PuttyKey, WinScpKey })
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(root, writable: true);
                if (k == null) continue;
                foreach (var name in k.GetSubKeyNames().Where(n => prefixes.Any(pf => n.StartsWith(pf, StringComparison.OrdinalIgnoreCase)) && (keepNames == null || !keepNames.Contains(n))))
                    k.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
            catch { /* best effort */ }
        }

        try
        {
            var path = MobaIniPath;
            if (!File.Exists(path)) return;
            var (lines, enc, bom) = ReadIni(path);
            RemoveOurMobaEntries(lines);
            var s = FindOurMobaSection(lines);
            if (s >= 0)
            {
                var end = s + 1;
                while (end < lines.Count && !lines[end].StartsWith("[")) end++;
                var body = lines.Skip(s + 1).Take(end - s - 1).Where(l => l.Trim().Length > 0).ToList();
                if (body.Count <= 2) lines.RemoveRange(s, end - s); // only SubRep/ImgNum left
            }
            WriteIni(path, lines, enc, bom);
        }
        catch { /* best effort */ }
    }

    /// <summary>PuTTY/WinSCP registry-safe session name (spaces, %, \, *, ? and non-ASCII are %XX-escaped).</summary>
    static string Munge(string name)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            var c = (char)b;
            if (b < 32 || b > 126 || " \\*?%/".Contains(c)) sb.Append('%').Append(b.ToString("X2"));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
