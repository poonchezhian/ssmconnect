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

    /// <summary>
    /// Signs in to the server. The server's default user (the one saved with the server) always uses the saved key.
    /// Any other user signs in with a password only; no key is tried for it.
    /// </summary>
    public static SftpFs Open(TunnelConfig t, string user, string? password)
    {
        var isDefault = string.Equals(user, t.UserName, StringComparison.Ordinal);
        var methods = new List<AuthenticationMethod>();
        if (isDefault)
        {
            var key = t.KeyFile;
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No key file is saved for this server. Edit the server in SSMConnect and choose its key.");
            if (key!.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("This server's key is a .ppk file, which the file browser cannot read. Edit the server and choose the .pem version of the key.");
            if (!File.Exists(key)) throw new InvalidOperationException("The key file " + key + " was not found.");
            PrivateKeyFile pk;
            try { pk = new PrivateKeyFile(key); }
            catch (Exception ex) { throw new InvalidOperationException("The key file could not be read (" + ex.Message + ")."); }
            methods.Add(new PrivateKeyAuthenticationMethod(user, pk));
        }
        else
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException($"Enter the password for {user}.");
            methods.Add(new PasswordAuthenticationMethod(user, password));
            var ki = new KeyboardInteractiveAuthenticationMethod(user);
            ki.AuthenticationPrompt += (_, e) =>
            {
                foreach (var pr in e.Prompts)
                    if (pr.Request.Contains("assword", StringComparison.OrdinalIgnoreCase)) pr.Response = password;
            };
            methods.Add(ki);
        }

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
