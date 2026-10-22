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

/// <summary>The folders and drives of this PC.</summary>
public sealed class LocalFs : FsBase
{
    public override string Label => "This PC";
    public override bool IsLocal => true;
    public override string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public override List<FsEntry> List(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return DriveInfo.GetDrives().Where(d => { try { return d.IsReady; } catch { return false; } })
                .Select(d => new FsEntry(d.Name.TrimEnd('\\'), d.Name, true, 0, DateTime.MinValue)).ToList();
        }
        var list = new List<FsEntry>();
        foreach (var fi in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            try
            {
                var isDir = (fi.Attributes & FileAttributes.Directory) != 0;
                list.Add(new FsEntry(fi.Name, fi.FullName, isDir, isDir ? 0 : ((FileInfo)fi).Length, fi.LastWriteTime));
            }
            catch { /* skip entries that cannot be read */ }
        }
        return Sorted(list);
    }

    public override string Parent(string path) => Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? "";
    public override string Combine(string dir, string name) => Path.Combine(dir, name);
    public override Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920);
    public override Stream OpenWrite(string path) => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
    public override void MakeDir(string path) => Directory.CreateDirectory(path);
    public override bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
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

    /// <summary>Signs in as <paramref name="user"/> with the server's key and/or a password. Throws when it cannot.</summary>
    public static SftpFs Open(TunnelConfig t, string user, string? password)
    {
        var methods = new List<AuthenticationMethod>();
        string? keyProblem = null;
        var key = t.KeyFile;
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (key!.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase)) keyProblem = "This server's key is a .ppk file, which cannot be used here. Edit the server to use the .pem key, or enter a password.";
            else if (!File.Exists(key)) keyProblem = "The key file " + key + " was not found.";
            else
            {
                try
                {
                    var pk = string.IsNullOrEmpty(password) ? new PrivateKeyFile(key) : new PrivateKeyFile(key, password);
                    methods.Add(new PrivateKeyAuthenticationMethod(user, pk));
                }
                catch (Exception ex) { keyProblem = "The key file could not be read (" + ex.Message + ")."; }
            }
        }
        else keyProblem = "No key file is saved for this server.";
        if (!string.IsNullOrEmpty(password)) methods.Add(new PasswordAuthenticationMethod(user, password));
        if (methods.Count == 0) throw new InvalidOperationException(keyProblem ?? "Nothing to sign in with. Enter a password.");

        var info = new ConnectionInfo("127.0.0.1", t.LocalPort, user, methods.ToArray()) { Timeout = TimeSpan.FromSeconds(20) };
        var client = new SftpClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
        try { client.Connect(); }
        catch { client.Dispose(); throw; }
        var fs = new SftpFs(client, $"{t.Name} ({user})");
        try { fs._home = client.WorkingDirectory; } catch { }
        if (string.IsNullOrEmpty(fs._home)) fs._home = "/";
        return fs;
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
