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

using Renci.SshNet;
using SSMConnect.Core;

namespace SSMConnect.Wizard;

/// <summary>One file or folder in a listing.</summary>
public sealed record FsEntry(string Name, string FullPath, bool IsDir, long Size, DateTime Modified);

/// <summary>A place files can be browsed and copied to or from: this PC or a server reached through an SSM tunnel.</summary>
public abstract class FsBase : IDisposable
{
    public abstract string Label { get; }
    public abstract bool IsLocal { get; }
    public abstract string Home { get; }
    public abstract List<FsEntry> List(string path);
    public abstract string Parent(string path);
    public abstract string Combine(string dir, string name);
    public abstract Stream OpenRead(string path);
    public abstract Stream OpenWrite(string path);
    public abstract void MakeDir(string path);
    public abstract bool Exists(string path);
    public virtual void Dispose() { }

    protected static List<FsEntry> Sorted(IEnumerable<FsEntry> items) =>
        items.OrderByDescending(e => e.IsDir).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>A server, reached over SFTP through its local SSM tunnel port.</summary>
public sealed class SftpFs : FsBase
{
    readonly SftpClient _c;
    readonly string _label;
    string _home = "/";

    SftpFs(SftpClient c, string label) { _c = c; _label = label; }

    public override string Label => _label;
    public override bool IsLocal => false;
    public override string Home => _home;

    /// <summary>Signs in as the server's default user with its saved key.</summary>
    public static SftpFs Open(TunnelConfig t)
    {
        var client = new SftpClient(KeyConnection(t)) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
        try { client.Connect(); }
        catch { client.Dispose(); throw; }
        var fs = new SftpFs(client, $"{t.Name} ({t.UserName})");
        try { fs._home = client.WorkingDirectory; } catch { }
        if (string.IsNullOrEmpty(fs._home)) fs._home = "/";
        return fs;
    }

    /// <summary>Connection details for the server's default user, using the saved key only.</summary>
    internal static ConnectionInfo KeyConnection(TunnelConfig t)
    {
        var key = t.KeyFile;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No key file is saved for this server. Edit the server in SSMConnect and choose its key.");
        if (key!.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("This server's key is a .ppk file, which the file browser cannot read. Edit the server and choose the .pem version of the key.");
        if (!File.Exists(key)) throw new InvalidOperationException("The key file " + key + " was not found.");
        PrivateKeyFile pk;
        try { pk = new PrivateKeyFile(key); }
        catch (Exception ex) { throw new InvalidOperationException("The key file could not be read (" + ex.Message + ")."); }
        return new ConnectionInfo("127.0.0.1", t.LocalPort, t.UserName, new PrivateKeyAuthenticationMethod(t.UserName, pk)) { Timeout = TimeSpan.FromSeconds(20) };
    }

    public override List<FsEntry> List(string path)
    {
        var list = new List<FsEntry>();
        foreach (var f in _c.ListDirectory(path))
        {
            if (f.Name == "." || f.Name == "..") continue;
            list.Add(new FsEntry(f.Name, f.FullName, f.IsDirectory, f.IsDirectory ? 0 : f.Length, f.LastWriteTime));
        }
        return Sorted(list);
    }

    public override string Parent(string path)
    {
        var p = path.TrimEnd('/');
        var i = p.LastIndexOf('/');
        return i <= 0 ? "/" : p.Substring(0, i);
    }

    public override string Combine(string dir, string name) => dir.EndsWith('/') ? dir + name : dir + "/" + name;
    public override Stream OpenRead(string path) => _c.OpenRead(path);
    public override Stream OpenWrite(string path) => _c.Open(path, FileMode.Create, FileAccess.Write);
    public override void MakeDir(string path) => _c.CreateDirectory(path);
    public override bool Exists(string path) => _c.Exists(path);

    public override void Dispose()
    {
        try { if (_c.IsConnected) _c.Disconnect(); } catch { }
        _c.Dispose();
    }
}


/// <summary>
/// Works on a server as ANOTHER user: signs in as the default user with the saved key and runs each file operation
/// through <c>sudo -u user</c>, the way Ubuntu lets its default user become someone else. If sudo asks for a password,
/// the one typed in the window is handed to sudo through a short-lived private helper file that is removed on close.
/// </summary>
public sealed class SudoFs : FsBase
{
    readonly SshClient _c;
    readonly string _name, _user, _prefix;
    readonly List<string> _temp = new();
    string _home = "/";

    SudoFs(SshClient c, string name, string user, string prefix, List<string> temp) { _c = c; _name = name; _user = user; _prefix = prefix; _temp = temp; }

    public override string Label => $"{_name} ({_user}, via sudo)";
    public override bool IsLocal => false;
    public override string Home => _home;

    static string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";

    static (int Code, string Out, string Err) Run(SshClient c, string line, string? stdin = null)
    {
        var cmd = c.CreateCommand(line);
        Stream? input = null;
        if (stdin != null) input = cmd.CreateInputStream();
        var ar = cmd.BeginExecute();
        if (input != null)
        {
            var b = System.Text.Encoding.UTF8.GetBytes(stdin!);
            input.Write(b, 0, b.Length);
            input.Dispose();
        }
        cmd.EndExecute(ar);
        return (Convert.ToInt32((object?)cmd.ExitStatus ?? 0), cmd.Result ?? "", cmd.Error ?? "");
    }

    /// <summary>Signs in as the server's default user (saved key), then checks that sudo can become <paramref name="user"/>.</summary>
    public static SudoFs Open(TunnelConfig t, string user, string? sudoPassword)
    {
        if (user.Equals("root", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("For safety, this tool never works as root.");
        var client = new SshClient(SftpFs.KeyConnection(t)) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
        var temp = new List<string>();
        try
        {
            client.Connect();
            string prefix;
            if (!string.IsNullOrEmpty(sudoPassword))
            {
                var id = Guid.NewGuid().ToString("N").Substring(0, 12);
                var pw = "/tmp/.ssc-p-" + id; var ask = "/tmp/.ssc-a-" + id;
                temp.Add(pw); temp.Add(ask);
                var r1 = Run(client, "umask 077; cat > " + pw, sudoPassword);
                var r2 = Run(client, "umask 077; printf '#!/bin/sh\\ncat " + pw + "\\n' > " + ask + "; chmod 700 " + ask);
                if (r1.Code != 0 || r2.Code != 0) throw new InvalidOperationException("Could not prepare sudo on the server: " + r1.Err + r2.Err);
                prefix = $"SUDO_ASKPASS={ask} sudo -A -u {Q(user)} ";
            }
            else prefix = $"sudo -n -u {Q(user)} ";

            var probe = Run(client, prefix + "true");
            if (probe.Code != 0)
            {
                var e = (probe.Err + probe.Out).Trim();
                if (e.Contains("password is required", StringComparison.OrdinalIgnoreCase) || e.Contains("terminal is required", StringComparison.OrdinalIgnoreCase) || e.Contains("no password was provided", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("sudo is asking for a password. Enter the sudo password of the default user (" + t.UserName + ") in the password box and click Switch user again.");
                if (e.Contains("incorrect password", StringComparison.OrdinalIgnoreCase) || e.Contains("Sorry, try again", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("sudo did not accept that password.");
                if (e.Contains("not in the sudoers", StringComparison.OrdinalIgnoreCase) || e.Contains("may not run sudo", StringComparison.OrdinalIgnoreCase) || e.Contains("not allowed", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"{t.UserName} is not allowed to use sudo as {user} on this server.");
                if (e.Contains("unknown user", StringComparison.OrdinalIgnoreCase) || e.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The user \"{user}\" does not exist on this server.");
                throw new InvalidOperationException("sudo could not switch to " + user + ": " + (e.Length > 0 ? e : "exit code " + probe.Code));
            }
            // Refuse any account that is really root (uid 0), whatever it is called.
            if (Run(client, prefix + "id -u").Out.Trim() == "0") throw new InvalidOperationException("That account has root rights. For safety, this tool never works as root.");
            var fs = new SudoFs(client, t.Name, user, prefix, temp);
            var home = Run(client, "getent passwd " + Q(user) + " | cut -d: -f6").Out.Trim();
            fs._home = home.Length > 0 ? home : "/";
            return fs;
        }
        catch
        {
            try { if (temp.Count > 0 && client.IsConnected) Run(client, "rm -f " + string.Join(" ", temp)); } catch { }
            try { client.Dispose(); } catch { }
            throw;
        }
    }

    public override List<FsEntry> List(string path)
    {
        var r = Run(_c, _prefix + "find -L " + Q(path) + " -maxdepth 1 -mindepth 1 -printf '%y\\t%s\\t%T@\\t%f\\0'");
        if (r.Code != 0 && r.Out.Length == 0) throw new IOException(r.Err.Trim().Length > 0 ? r.Err.Trim() : "Could not list " + path);
        var list = new List<FsEntry>();
        foreach (var rec in r.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = rec.Split('\t', 4);
            if (p.Length < 4) continue;
            long.TryParse(p[1], out var size);
            var when = DateTime.MinValue;
            if (double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs))
                when = DateTimeOffset.FromUnixTimeSeconds((long)secs).LocalDateTime;
            var isDir = p[0] == "d";
            list.Add(new FsEntry(p[3], Combine(path, p[3]), isDir, isDir ? 0 : size, when));
        }
        return Sorted(list);
    }

    public override string Parent(string path)
    {
        var p = path.TrimEnd('/');
        var i = p.LastIndexOf('/');
        return i <= 0 ? "/" : p.Substring(0, i);
    }

    public override string Combine(string dir, string name) => dir.EndsWith('/') ? dir + name : dir + "/" + name;

    public override bool Exists(string path) => Run(_c, _prefix + "test -e " + Q(path)).Code == 0;

    public override void MakeDir(string path)
    {
        var r = Run(_c, _prefix + "mkdir -- " + Q(path));
        if (r.Code != 0) throw new IOException(r.Err.Trim());
    }

    public override Stream OpenRead(string path)
    {
        var cmd = _c.CreateCommand(_prefix + "cat -- " + Q(path));
        var ar = cmd.BeginExecute();
        return new ExecStream(cmd, ar, null);
    }

    public override Stream OpenWrite(string path)
    {
        var cmd = _c.CreateCommand(_prefix + "sh -c 'cat > \"$1\"' sh " + Q(path));
        var input = cmd.CreateInputStream();
        var ar = cmd.BeginExecute();
        return new ExecStream(cmd, ar, input);
    }

    public override void Dispose()
    {
        try { if (_temp.Count > 0 && _c.IsConnected) Run(_c, "rm -f " + string.Join(" ", _temp)); } catch { }
        try { if (_c.IsConnected) _c.Disconnect(); } catch { }
        _c.Dispose();
    }

    /// <summary>Reads the output of, or feeds the input of, a running command; reports a failure when it is closed.</summary>
    sealed class ExecStream : Stream
    {
        readonly SshCommand _cmd; readonly IAsyncResult _ar; readonly Stream? _input;
        bool _eof, _done;
        public ExecStream(SshCommand cmd, IAsyncResult ar, Stream? input) { _cmd = cmd; _ar = ar; _input = input; }
        public override bool CanRead => _input == null;
        public override bool CanWrite => _input != null;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _input?.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _cmd.OutputStream.Read(buffer, offset, count);
            if (n == 0) _eof = true;
            return n;
        }
        public override void Write(byte[] buffer, int offset, int count) => _input!.Write(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_done)
            {
                _done = true;
                if (_input != null) { try { _input.Dispose(); } catch { } }
                else if (!_eof) { try { _cmd.CancelAsync(); } catch { } }
                try { _cmd.EndExecute(_ar); } catch { }
                var code = Convert.ToInt32((object?)_cmd.ExitStatus ?? 0);
                if (code != 0 && (_input != null || _eof))
                {
                    var err = (_cmd.Error ?? "").Trim();
                    base.Dispose(disposing);
                    throw new IOException(err.Length > 0 ? err : "The command failed with exit code " + code);
                }
            }
            base.Dispose(disposing);
        }
    }
}
