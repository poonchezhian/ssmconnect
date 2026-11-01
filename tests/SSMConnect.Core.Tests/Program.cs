using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using SSMConnect.Core;

if (args.Contains("--worker")) { Thread.Sleep(60000); return; }

void Assert(bool ok, string why) { if (!ok) throw new Exception(why); }
var temp = Path.Combine(Path.GetTempPath(), "ssmconnect-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    using var rsa = RSA.Create(2048);
    var pem = Path.Combine(temp, "test.pem");
    File.WriteAllText(pem, rsa.ExportRSAPrivateKeyPem());
    var keys = Path.Combine(temp, "keys");
    var ppk = KeyConverter.EnsurePpkInDirectory(pem, keys);
    Assert(ppk != null && File.ReadAllText(ppk).Contains("Private-MAC:"), "RSA conversion must produce a complete PuTTY key");
    var sid = WindowsIdentity.GetCurrent().User!;
    var rules = FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(keys)).GetAccessRules(true, true, typeof(SecurityIdentifier));
    Assert(rules.Cast<FileSystemAccessRule>().Any(r => r.IdentityReference.Equals(sid) && r.AccessControlType == AccessControlType.Allow), "Converted key directory must grant the actual account SID");
    Assert(FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(keys)).AreAccessRulesProtected, "Key directory must not inherit broad grants");
    var brokenAcl = new FileSecurity();
    brokenAcl.SetAccessRuleProtection(true, false);
    FileSystemAclExtensions.SetAccessControl(new FileInfo(ppk!), brokenAcl);
    Assert(KeyConverter.EnsurePpkInDirectory(pem, keys) == ppk && File.ReadAllText(ppk!).Contains("Private-MAC:"), "Existing converted keys with broken permissions must be repaired");
    File.WriteAllText(ppk!, "truncated");
    Parallel.For(0, 12, _ => Assert(KeyConverter.EnsurePpkInDirectory(pem, keys) == ppk, "Concurrent conversions must reuse a complete key"));
    Assert(File.ReadAllText(ppk!).StartsWith("PuTTY-User-Key-File-2:"), "A corrupted cache must be regenerated");
    Assert(!Directory.GetFiles(keys, "*.tmp").Any(), "Key conversion must clean staging files");
    Assert(KeyConverter.EnsurePpkInDirectory(Path.Combine(temp, "missing.ppk"), keys) == null, "Missing .ppk paths must not be accepted");
    using var other = RSA.Create(2048);
    File.WriteAllText(pem, other.ExportRSAPrivateKeyPem());
    var before = File.ReadAllText(ppk!);
    KeyConverter.EnsurePpkInDirectory(pem, keys);
    Assert(before != File.ReadAllText(ppk!), "A changed source key must replace cached key material");
    Console.WriteLine("PASS: SID permissions, ACL repair, atomic concurrent key conversion, cache repair and source rotation");

    Assert(TunnelRetryPolicy.IsTransient("The connection dropped after 3 seconds. Check the server and your network."), "Network interruptions must retry");
    Assert(TunnelRetryPolicy.IsTransient("Cannot reach AWS. Check your internet connection"), "Offline network must recover automatically");
    Assert(TunnelRetryPolicy.IsTransient(AwsCli.FriendlyError("ThrottlingException")), "Temporary AWS throttling must retry automatically");
    Assert(TunnelRetryPolicy.IsTransient("It did not connect within 45 seconds. Check the server is online in SSM and you are signed in to AWS."), "Slow startup must not permanently disable the tunnel");
    Assert(!TunnelRetryPolicy.IsTransient("Your AWS sign-in has expired. Check your network."), "Authentication must not become an endless network retry");
    Assert(!TunnelRetryPolicy.IsTransient("Local port 2201 is already used by another program."), "Occupied ports must not be retried forever");
    Assert(TunnelRetryPolicy.DelaySeconds(1) == 2 && TunnelRetryPolicy.DelaySeconds(100) == 60, "Backoff must be bounded");
    Console.WriteLine("PASS: transient retry and authentication/port-conflict boundaries");

    using var worker = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--worker") { UseShellExecute = false, CreateNoWindow = true })!;
    try
    {
        var failQuery = false;
        var hasListener = true;
        var queryCount = 0;
        var tunnel = new TunnelConfig { Name = "test", InstanceId = "i-0123456789abcdef0", LocalPort = 2201, RemotePort = 22, Profile = "test", Region = "us-east-1", UserName = "ubuntu" };
        var config = new AppConfig { Tunnels = new() { tunnel } };
        using var manager = new TunnelManager(() => { queryCount++; if (failQuery) throw new NetworkInformationException(); return hasListener ? new[] { new IPEndPoint(IPAddress.Loopback, 2201) } : Array.Empty<IPEndPoint>(); },
            () => Array.Empty<TcpConnectionInformation>(), _ => { });
        var type = typeof(TunnelManager);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var slotType = type.GetNestedType("Slot", BindingFlags.NonPublic)!;
        var slot = Activator.CreateInstance(slotType)!;
        void Set(string field, object? value) => slotType.GetField(field)!.SetValue(slot, value);
        Set("Tunnel", tunnel); Set("Proc", worker); Set("State", TunnelStates.Connected); Set("ReadyReported", true); Set("StartedAt", DateTime.UtcNow.AddMinutes(-1));
        type.GetField("_cfg", flags)!.SetValue(manager, config);
        ((IDictionary)type.GetField("_slots", flags)!.GetValue(manager)!).Add($"{tunnel.InstanceId}|22|2201|test|us-east-1", slot);
        void Reconcile() => type.GetMethod("Reconcile", flags)!.Invoke(manager, new object[] { config });
        Reconcile();
        Assert(manager.IsOpen(tunnel), "Observed healthy tunnel must be open");
        failQuery = true;
        Reconcile();
        Assert(manager.IsOpen(tunnel), "A Windows TCP-table query failure must not downgrade a healthy tunnel");
        failQuery = false; hasListener = false;
        Reconcile();
        Assert(!manager.IsOpen(tunnel) && !worker.HasExited, "Missing listener must block new launches without killing on one sample");
        hasListener = true;
        Reconcile();
        Assert(manager.IsOpen(tunnel) && queryCount == 4, "Listener recovery must clear uncertainty; query only once per pass");
        using var staleProcess = Process.GetCurrentProcess();
        Set("State", TunnelStates.Retrying);
        type.GetMethod("OnLine", flags)!.Invoke(manager, new object[] { slot, staleProcess, "Waiting for connections...", false });
        Assert(manager.StateOf(tunnel, out _) == TunnelStates.Retrying, "A retired subprocess callback must not restore Connected");
        Console.WriteLine("PASS: transient Windows TCP-table failure, listener recovery, bounded polling and stale subprocess events");
    }
    finally { if (!worker.HasExited) worker.Kill(entireProcessTree: true); worker.WaitForExit(); }
}
finally { Directory.Delete(temp, recursive: true); }
