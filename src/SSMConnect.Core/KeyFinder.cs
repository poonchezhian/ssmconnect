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

using System.Diagnostics;
using System.Security.Cryptography;

namespace SSMConnect.Core;

/// <summary>A private-key file found on this PC (the key used to sign in to Linux servers).</summary>
public sealed record KeyFile(string Path, string Format)
{
    /// <summary>
    /// The fingerprints AWS would show for this key pair (MD5 of the private key, and of the public key for imported keys),
    /// worked out on this PC. Empty when the key is protected by a passphrase or is not an RSA key.
    /// </summary>
    public HashSet<string> Fingerprints { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>File name without extension; AWS key pairs are normally saved as "&lt;key pair name&gt;.pem".</summary>
    public string Stem => System.IO.Path.GetFileNameWithoutExtension(Path);
    public override string ToString() => $"{System.IO.Path.GetFileName(Path)}   ({System.IO.Path.GetDirectoryName(Path)})";
}

/// <summary>
/// Looks for SSH private keys (.pem, .ppk, id_rsa ...) in the usual places and, on request, in any folder the person picks.
/// Only the first few hundred bytes of a candidate file are read, to confirm it is a key. Nothing is copied or uploaded.
/// </summary>
public static class KeyFinder
{
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information",
        "node_modules", ".git", ".nuget", ".vscode", ".gradle", ".m2", "Recovery", "PerfLogs", "WindowsApps", "Packages", "Microsoft", "Temp",
    };

    /// <summary>The places keys usually live, quickest first.</summary>
    public static List<string> DefaultFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<string>
        {
            System.IO.Path.Combine(home, ".ssh"), System.IO.Path.Combine(home, ".aws"),
            System.IO.Path.Combine(home, "Downloads"), System.IO.Path.Combine(home, "Desktop"), System.IO.Path.Combine(home, "Documents"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        foreach (var v in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var od = Environment.GetEnvironmentVariable(v);
            if (!string.IsNullOrEmpty(od)) list.Add(od);
        }
        list.Add(System.IO.Path.Combine(home, "Google Drive"));
        list.Add(System.IO.Path.Combine(home, "Dropbox"));
        return list.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Scans the usual folders (and, when allDrives is set, a shallow pass over every local drive).</summary>
    public static List<KeyFile> ScanStandard(bool allDrives, TimeSpan limit, CancellationToken ct = default)
    {
        var found = new Dictionary<string, KeyFile>(StringComparer.OrdinalIgnoreCase);
        var stop = DateTime.UtcNow + limit;
        foreach (var f in DefaultFolders()) Walk(f, 5, found, stop, ct);
        if (allDrives)
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType == DriveType.Fixed && d.IsReady) Walk(d.RootDirectory.FullName, 4, found, stop, ct); } catch { }
            }
        }
        return Sorted(found);
    }

    /// <summary>Scans one folder the person chose, deeper than the standard scan.</summary>
    public static List<KeyFile> ScanFolder(string folder, TimeSpan limit, CancellationToken ct = default)
    {
        var found = new Dictionary<string, KeyFile>(StringComparer.OrdinalIgnoreCase);
        Walk(folder, 10, found, DateTime.UtcNow + limit, ct);
        return Sorted(found);
    }

    static List<KeyFile> Sorted(Dictionary<string, KeyFile> d) =>
        d.Values.OrderBy(k => k.Stem, StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Path, StringComparer.OrdinalIgnoreCase).ToList();

    static void Walk(string dir, int depth, Dictionary<string, KeyFile> found, DateTime stop, CancellationToken ct)
    {
        if (depth < 0 || DateTime.UtcNow > stop || ct.IsCancellationRequested) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (ct.IsCancellationRequested) return;
                var fmt = Classify(file);
                if (fmt != null && !found.ContainsKey(file)) found[file] = new KeyFile(file, fmt) { Fingerprints = fmt is "pem" or "ppk" ? KeyFingerprint.Compute(file) : new(StringComparer.OrdinalIgnoreCase) };
            }
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = System.IO.Path.GetFileName(sub);
                if (Skip.Contains(name)) continue;
                try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                Walk(sub, depth - 1, found, stop, ct);
            }
        }
        catch { /* folder not readable: move on */ }
    }

    /// <summary>"pem", "ppk" or "openssh" when the file really is a private key, otherwise null.</summary>
    public static string? Classify(string file)
    {
        try
        {
            var name = System.IO.Path.GetFileName(file);
            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            var plausible = ext is ".pem" or ".ppk" or ".key" or ".cer" ||
                            (ext.Length == 0 && (name.StartsWith("id_", StringComparison.OrdinalIgnoreCase)));
            if (!plausible) return null;
            var info = new FileInfo(file);
            if (info.Length is < 100 or > 32 * 1024) return null;
            var buf = new char[256];
            using var r = new StreamReader(file);
            var n = r.Read(buf, 0, buf.Length);
            var head = new string(buf, 0, n);
            if (head.Contains("PuTTY-User-Key-File", StringComparison.Ordinal)) return "ppk";
            if (head.Contains("BEGIN OPENSSH PRIVATE KEY", StringComparison.Ordinal)) return "openssh";
            if (head.Contains("PRIVATE KEY-----", StringComparison.Ordinal) && !head.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal)) return "pem";
            if (head.Contains("PRIVATE KEY-----", StringComparison.Ordinal)) return "pem";
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Finds the key file for a server. Most reliable first: the key's own fingerprint equals the server's key-pair fingerprint
    /// (works whatever the file is called); then a file named exactly like the key pair; then a similar name
    /// ("my-key (1).pem" for "my-key"). <c>How</c> says which rule matched, in plain words.
    /// </summary>
    public static (KeyFile? Key, string How) Find(IEnumerable<KeyFile> keys, string keyName, string? fingerprint)
    {
        var list = keys.ToList();
        static int Rank(KeyFile k) => k.Format == "pem" ? 0 : k.Format == "openssh" ? 1 : 2;
        if (!string.IsNullOrWhiteSpace(fingerprint))
        {
            var byPrint = list.Where(k => k.Fingerprints.Contains(fingerprint!)).OrderBy(Rank).FirstOrDefault();
            if (byPrint != null) return (byPrint, "verified: this key belongs to the server");
        }
        if (string.IsNullOrWhiteSpace(keyName)) return (null, "");
        var exact = Match(list, keyName);
        if (exact != null) return (exact, "matched by name");
        static string Norm(string s)
        {
            var t = new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            return t.EndsWith("pem") ? t[..^3] : t;
        }
        var want = Norm(keyName);
        if (want.Length >= 4)
        {
            var similar = list.Where(k => { var n = Norm(k.Stem); return n == want || (n.Length >= 4 && (n.StartsWith(want) || want.StartsWith(n))); })
                              .OrderBy(Rank).FirstOrDefault();
            if (similar != null) return (similar, "similar name - please check");
        }
        return (null, "");
    }

    /// <summary>Best key for a server: the file named like the server's key pair, otherwise null.</summary>
    public static KeyFile? Match(IEnumerable<KeyFile> keys, string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return null;
        var list = keys.ToList();
        // prefer .pem (works with the AWS password tool and MobaXterm), then .ppk
        return list.Where(k => string.Equals(k.Stem, keyName, StringComparison.OrdinalIgnoreCase))
                   .OrderBy(k => k.Format == "pem" ? 0 : k.Format == "openssh" ? 1 : 2).FirstOrDefault();
    }
}

/// <summary>
/// Computes the fingerprints AWS displays for a key pair, so a key file can be recognised by its content rather than by its name.
/// AWS shows the SHA-1 of the PKCS#8 private key for key pairs it created, and the MD5 of the public key for imported ones.
/// Works for unencrypted RSA keys in .pem and PuTTY .ppk form.
/// </summary>
public static class KeyFingerprint
{
    public static HashSet<string> Compute(string file)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var text = File.ReadAllText(file);
            if (text.Contains("PuTTY-User-Key-File", StringComparison.Ordinal))
            {
                foreach (var prm in FromPpk(text)) AddAll(set, prm);
                return set;
            }
            if (text.Contains("ENCRYPTED", StringComparison.Ordinal)) return set;
            using var rsa = RSA.Create();
            rsa.ImportFromPem(text);
            AddAll(set, rsa);
        }
        catch { /* not an RSA key we can read: it can still be matched by name */ }
        return set;
    }

    static void AddAll(HashSet<string> set, RSAParameters prm)
    {
        try { using var rsa = RSA.Create(); rsa.ImportParameters(prm); AddAll(set, rsa); } catch { }
    }

    static void AddAll(HashSet<string> set, RSA rsa)
    {
        set.Add(Hex(SHA1.HashData(rsa.ExportPkcs8PrivateKey())));     // key pairs created by AWS
        set.Add(Hex(MD5.HashData(rsa.ExportPkcs8PrivateKey())));
        set.Add(Hex(MD5.HashData(rsa.ExportSubjectPublicKeyInfo()))); // key pairs imported from a public key
    }

    /// <summary>Rebuilds the RSA key from an unencrypted PuTTY file (both prime orderings, as tools differ).</summary>
    static IEnumerable<RSAParameters> FromPpk(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        if (!lines[0].Contains("ssh-rsa", StringComparison.Ordinal)) yield break;
        string? Field(string key) => lines.FirstOrDefault(l => l.StartsWith(key + ":", StringComparison.Ordinal))?.Substring(key.Length + 1).Trim();
        if (!string.Equals(Field("Encryption"), "none", StringComparison.OrdinalIgnoreCase)) yield break;
        byte[] Block(string name)
        {
            var i = Array.FindIndex(lines, l => l.StartsWith(name + ":", StringComparison.Ordinal));
            var n = int.Parse(lines[i].Substring(name.Length + 1).Trim());
            return Convert.FromBase64String(string.Concat(lines.Skip(i + 1).Take(n).Select(x => x.Trim())));
        }
        var pub = new SshReader(Block("Public-Lines"));
        pub.Str();
        var e = pub.Int(); var n2 = pub.Int();
        var priv = new SshReader(Block("Private-Lines"));
        var d = priv.Int(); var p = priv.Int(); var q = priv.Int();
        var len = n2.GetByteCount(isUnsigned: true);
        byte[] B(System.Numerics.BigInteger v, int l)
        {
            var b = v.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (b.Length == l) return b;
            if (b.Length > l) return b[^l..];
            var r = new byte[l]; Array.Copy(b, 0, r, l - b.Length, b.Length); return r;
        }
        foreach (var (pp, qq) in new[] { (p, q), (q, p) })
        {
            var half = (len + 1) / 2;
            var inv = System.Numerics.BigInteger.ModPow(qq, pp - 2, pp); // q^-1 mod p (p is prime)
            yield return new RSAParameters
            {
                Modulus = B(n2, len), Exponent = B(e, e.GetByteCount(isUnsigned: true)), D = B(d, len),
                P = B(pp, half), Q = B(qq, half),
                DP = B(d % (pp - 1), half), DQ = B(d % (qq - 1), half), InverseQ = B(inv, half),
            };
        }
    }

    sealed class SshReader
    {
        readonly byte[] _b; int _o;
        public SshReader(byte[] b) { _b = b; }
        public byte[] Str()
        {
            var l = (_b[_o] << 24) | (_b[_o + 1] << 16) | (_b[_o + 2] << 8) | _b[_o + 3];
            _o += 4;
            var r = _b[_o..(_o + l)]; _o += l; return r;
        }
        public System.Numerics.BigInteger Int() => new(Str(), isUnsigned: true, isBigEndian: true);
    }

    static string Hex(byte[] b) => string.Join(":", b.Select(x => x.ToString("x2")));
}

/// <summary>
/// PuTTY and WinSCP's saved sessions need a .ppk key. A .pem key (RSA, without a passphrase) is converted here, inside SSMConnect,
/// into a .ppk copy under %LOCALAPPDATA%\SSMConnect\keys. The original file is never changed or moved.
/// </summary>
public static class KeyConverter
{
    /// <summary>Returns a .ppk path for the key (the key itself when it already is one), or null when it cannot be converted.</summary>
    public static string? EnsurePpk(string keyPath)
    {
        try
        {
            if (keyPath.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase)) return keyPath;
            if (!File.Exists(keyPath)) return null;
            var dir = System.IO.Path.Combine(ConfigStore.DataDir, "keys");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(System.IO.Path.GetFileNameWithoutExtension(keyPath).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));
            var outFile = System.IO.Path.Combine(dir, safe + ".ppk");
            if (File.Exists(outFile) && File.GetLastWriteTimeUtc(outFile) >= File.GetLastWriteTimeUtc(keyPath)) return outFile;

            var text = File.ReadAllText(keyPath);
            if (text.Contains("ENCRYPTED", StringComparison.Ordinal)) return null; // protected by a passphrase
            using var rsa = System.Security.Cryptography.RSA.Create();
            rsa.ImportFromPem(text);
            File.WriteAllText(outFile, WritePpk(rsa.ExportParameters(true), System.IO.Path.GetFileNameWithoutExtension(keyPath)));
            return outFile;
        }
        catch { return null; }
    }

    // PuTTY private key file, version 2, unencrypted ("Encryption: none").
    static string WritePpk(System.Security.Cryptography.RSAParameters k, string comment)
    {
        var pub = Concat(Str(System.Text.Encoding.ASCII.GetBytes("ssh-rsa")), Mpint(k.Exponent!), Mpint(k.Modulus!));
        var priv = Concat(Mpint(k.D!), Mpint(k.P!), Mpint(k.Q!), Mpint(k.InverseQ!));
        var macKey = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.ASCII.GetBytes("putty-private-key-file-mac-key"));
        var macData = Concat(Str(System.Text.Encoding.ASCII.GetBytes("ssh-rsa")), Str(System.Text.Encoding.ASCII.GetBytes("none")),
                             Str(System.Text.Encoding.UTF8.GetBytes(comment)), Str(pub), Str(priv));
        var mac = Convert.ToHexString(System.Security.Cryptography.HMACSHA1.HashData(macKey, macData)).ToLowerInvariant();

        static IEnumerable<string> Lines(byte[] b)
        {
            var s = Convert.ToBase64String(b);
            for (var i = 0; i < s.Length; i += 64) yield return s.Substring(i, Math.Min(64, s.Length - i));
        }
        var pubLines = Lines(pub).ToList();
        var privLines = Lines(priv).ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append("PuTTY-User-Key-File-2: ssh-rsa\r\nEncryption: none\r\nComment: ").Append(comment).Append("\r\n");
        sb.Append("Public-Lines: ").Append(pubLines.Count).Append("\r\n");
        foreach (var l in pubLines) sb.Append(l).Append("\r\n");
        sb.Append("Private-Lines: ").Append(privLines.Count).Append("\r\n");
        foreach (var l in privLines) sb.Append(l).Append("\r\n");
        sb.Append("Private-MAC: ").Append(mac).Append("\r\n");
        return sb.ToString();
    }

    static byte[] Str(byte[] b) => Concat(new[] { (byte)(b.Length >> 24), (byte)(b.Length >> 16), (byte)(b.Length >> 8), (byte)b.Length }, b);

    static byte[] Mpint(byte[] bigEndian)
    {
        var i = 0;
        while (i < bigEndian.Length - 1 && bigEndian[i] == 0) i++;
        var v = bigEndian[i..];
        return Str(v.Length > 0 && (v[0] & 0x80) != 0 ? Concat(new byte[] { 0 }, v) : v);
    }

    static byte[] Concat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
}
