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
using System.Text;
using System.Text.Json;

namespace SSMConnect.Core;

public sealed record CliResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

public static class AwsCli
{
    public const string AwsDownloadUrl = "https://awscli.amazonaws.com/AWSCLIV2.msi";
    public const string PluginDownloadUrl =
        "https://s3.amazonaws.com/session-manager-downloads/plugin/latest/windows/SessionManagerPluginSetup.exe";

    public static string? FindAws() =>
        FirstExisting(@"C:\Program Files\Amazon\AWSCLIV2\aws.exe") ?? FindOnPath("aws.exe");

    public static string? FindPlugin() =>
        FirstExisting(@"C:\Program Files\Amazon\SessionManagerPlugin\bin\session-manager-plugin.exe")
        ?? FindOnPath("session-manager-plugin.exe");

    static string? FirstExisting(string path) => File.Exists(path) ? path : null;

    static string? FindOnPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    /// <summary>Builds a ProcessStartInfo for "aws ..." with profile, region and user-profile environment applied.</summary>
    public static ProcessStartInfo BuildStartInfo(AppConfig cfg, IEnumerable<string> args, bool redirect = true)
    {
        var aws = FindAws() ?? throw new FileNotFoundException("AWS CLI is not installed.");
        var psi = new ProcessStartInfo(aws)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            StandardOutputEncoding = redirect ? Encoding.UTF8 : null,
            StandardErrorEncoding = redirect ? Encoding.UTF8 : null,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (!string.IsNullOrWhiteSpace(cfg.Profile)) { psi.ArgumentList.Add("--profile"); psi.ArgumentList.Add(cfg.Profile); }
        if (!string.IsNullOrWhiteSpace(cfg.Region)) { psi.ArgumentList.Add("--region"); psi.ArgumentList.Add(cfg.Region); }

        var plugin = FindPlugin();
        if (plugin != null)
        {
            var current = psi.Environment.TryGetValue("PATH", out var p) ? p : "";
            psi.Environment["PATH"] = Path.GetDirectoryName(plugin) + ";" + current;
        }
        psi.Environment["AWS_PAGER"] = "";
        return psi;
    }

    public static async Task<CliResult> RunAsync(AppConfig cfg, IEnumerable<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var proc = new Process { StartInfo = BuildStartInfo(cfg, args) };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new CliResult(-1, stdout.ToString(), "Timed out waiting for the AWS CLI.");
        }
        return new CliResult(proc.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    public static async Task<List<string>> ListProfilesAsync(CancellationToken ct = default)
    {
        if (FindAws() == null) return new List<string>();
        var r = await RunAsync(new AppConfig { Profile = "", Region = "" }, new[] { "configure", "list-profiles" }, TimeSpan.FromSeconds(20), ct);
        return r.Ok
            ? r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string>();
    }

    /// <summary>Opens the browser sign-in flow for an SSO profile.</summary>
    public static Task<CliResult> SsoLoginAsync(AppConfig cfg, CancellationToken ct = default) =>
        RunAsync(new AppConfig { Profile = cfg.Profile, Region = "" },
                 new[] { "sso", "login" }, TimeSpan.FromMinutes(3), ct);

    /// <summary>Checks the sign-in. STS needs some region, so a neutral one is used when none is chosen yet.</summary>
    public static Task<CliResult> WhoAmIAsync(AppConfig cfg, CancellationToken ct = default) =>
        RunAsync(new AppConfig { Profile = cfg.Profile, Region = string.IsNullOrWhiteSpace(cfg.Region) ? "us-east-1" : cfg.Region }, new[] { "sts", "get-caller-identity", "--output", "json" }, TimeSpan.FromSeconds(30), ct);

    /// <summary>Region names this account can use; falls back to the common list when AWS cannot be asked.</summary>
    public static async Task<List<string>> ListRegionsAsync(string profile, CancellationToken ct = default)
    {
        try
        {
            var r = await RunAsync(new AppConfig { Profile = profile, Region = "us-east-1" },
                new[] { "ec2", "describe-regions", "--query", "Regions[].RegionName", "--output", "json" }, TimeSpan.FromSeconds(30), ct);
            if (r.Ok)
            {
                using var doc = JsonDocument.Parse(r.Output);
                var list = doc.RootElement.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => x.Length > 0).OrderBy(x => x).ToList();
                if (list.Count > 0) return list;
            }
        }
        catch { }
        return AwsRegions.Common.OrderBy(x => x).ToList();
    }

    /// <summary>Lists running EC2 instances and marks which ones SSM can actually reach.</summary>
    public static async Task<List<DiscoveredServer>> DiscoverAsync(AppConfig cfg, CancellationToken ct = default)
    {
        var ssm = await RunAsync(cfg, new[]
        {
            "ssm", "describe-instance-information",
            "--query", "InstanceInformationList[].[InstanceId,PingStatus,PlatformType,PlatformName]",
            "--output", "json"
        }, TimeSpan.FromSeconds(60), ct);
        if (!ssm.Ok) throw new InvalidOperationException(FriendlyError(ssm.Error));

        var ec2 = await RunAsync(cfg, new[]
        {
            "ec2", "describe-instances",
            "--filters", "Name=instance-state-name,Values=running",
            "--query", "Reservations[].Instances[].[InstanceId,PrivateIpAddress,Tags[?Key=='Name']|[0].Value,KeyName,Tags]",
            "--output", "json"
        }, TimeSpan.FromSeconds(60), ct);
        if (!ec2.Ok) throw new InvalidOperationException(FriendlyError(ec2.Error));

        // Key-pair fingerprints let a key file be recognised by its content. Optional: without ec2:DescribeKeyPairs we match by name.
        var prints = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var kp = await RunAsync(cfg, new[] { "ec2", "describe-key-pairs", "--query", "KeyPairs[].[KeyName,KeyFingerprint]", "--output", "json" }, TimeSpan.FromSeconds(30), ct);
            if (kp.Ok)
                using (var d = JsonDocument.Parse(kp.Output))
                    foreach (var row in d.RootElement.EnumerateArray()) prints[Str(row, 0)] = Str(row, 1);
        }
        catch { }

        var managed = new Dictionary<string, (bool online, string platform, string os)>();
        using (var doc = JsonDocument.Parse(ssm.Output))
            foreach (var row in doc.RootElement.EnumerateArray())
                managed[Str(row, 0)] = (Str(row, 1) == "Online", Str(row, 2), Str(row, 3));

        var result = new List<DiscoveredServer>();
        using (var doc = JsonDocument.Parse(ec2.Output))
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var id = Str(row, 0);
                managed.TryGetValue(id, out var m);
                result.Add(new DiscoveredServer
                {
                    InstanceId = id,
                    PrivateIp = Str(row, 1),
                    Name = string.IsNullOrWhiteSpace(Str(row, 2)) ? id : Str(row, 2),
                    Environment = EnvironmentClassifier.Classify(TagsOf(row), string.IsNullOrWhiteSpace(Str(row, 2)) ? id : Str(row, 2)),
                    KeyName = Str(row, 3),
                    KeyFingerprint = prints.TryGetValue(Str(row, 3), out var fpr) ? fpr : "",
                    Platform = string.IsNullOrEmpty(m.platform) ? "Linux" : m.platform,
                    SsmOnline = m.online,
                    OsName = m.os ?? "",
                });
            }
        return result.OrderByDescending(s => s.SsmOnline).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>All tags of an instance as key/value pairs (the last column of the describe-instances row).</summary>
    static List<KeyValuePair<string, string>> TagsOf(JsonElement row)
    {
        var list = new List<KeyValuePair<string, string>>();
        try
        {
            if (row.GetArrayLength() > 4 && row[4].ValueKind == JsonValueKind.Array)
                foreach (var t in row[4].EnumerateArray())
                {
                    var k = t.TryGetProperty("Key", out var kk) ? kk.GetString() : null;
                    var v = t.TryGetProperty("Value", out var vv) ? vv.GetString() : null;
                    if (!string.IsNullOrEmpty(k)) list.Add(new(k!, v ?? ""));
                }
        }
        catch { }
        return list;
    }

    static string Str(JsonElement row, int i) =>
        row.GetArrayLength() > i && row[i].ValueKind == JsonValueKind.String ? row[i].GetString() ?? "" : "";

    /// <summary>
    /// Turns raw AWS CLI / Session Manager errors into a plain-language status a developer can act on.
    /// Pass the local port when known so port clashes name the port.
    /// </summary>
    public static string FriendlyError(string raw, int? localPort = null)
    {
        var t = raw ?? "";
        bool Has(string x) => t.Contains(x, StringComparison.OrdinalIgnoreCase);

        if (Has("TargetNotConnected"))
            return "Server is offline in SSM: the SSM agent is not running or the server has no SSM instance role. Check it in AWS Systems Manager > Fleet Manager.";
        if (Has("InvalidInstanceId") || Has("is not valid") && Has("instance"))
            return "That server ID was not found in this AWS account and region. Check the region, or run setup again.";
        if (Has("RegisterClient") || Has("InvalidRequestException") && Has("sso"))
            return "AWS did not accept the company sign-in URL or its region. Check the URL (like https://yourcompany.awsapps.com/start) and the region of the sign-in page with your AWS admin.";
        if (Has("Token has expired") || Has("ExpiredToken") || Has("sso login") || Has("SSO session") || Has("SSO token") || Has("The security token included in the request is expired"))
            return "Your AWS sign-in has expired. Sign in again: AWS > Sign in again in the dashboard.";
        if (Has("Unable to locate credentials") || Has("NoCredentialProviders"))
            return "No AWS credentials found for this account. Use AWS > Sign in again, or add the account with AWS > Add AWS account.";
        if (Has("could not be found") && Has("profile"))
            return "That AWS account profile does not exist on this PC. Open AWS > Add AWS account and add it again.";
        if (Has("ssm:StartSession") || Has("not authorized to perform") && Has("ssm"))
            return "Your AWS role is not allowed to start SSM sessions (needs ssm:StartSession on this server). Ask your AWS admin.";
        if (Has("UnauthorizedOperation") || Has("AccessDenied"))
            return "Your AWS role does not have permission for this action. Ask your AWS admin (needs ec2:DescribeInstances, ssm:DescribeInstanceInformation, ssm:StartSession).";
        if (Has("SessionManagerPlugin is not found") || Has("session-manager-plugin") && (Has("not found") || Has("not recognized")))
            return "The Session Manager plugin is missing. Click Add / change servers to install it.";
        if (Has("address already in use") || Has("only one usage of each socket"))
            return localPort is int p
                ? $"Local port {p} is already used by another program. Run setup again to pick a free port."
                : "The local port is already used by another program. Run setup again to pick a free port.";
        if (Has("Could not connect to the endpoint") || Has("Connect timeout") || Has("Name or service not known") || Has("getaddrinfo"))
            return "Cannot reach AWS. Check your internet connection, VPN or proxy, and the region name.";
        if (Has("ThrottlingException") || Has("Rate exceeded"))
            return "AWS is rate-limiting requests. SSMConnect will retry automatically.";
        var first = t.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? "AWS CLI returned an unknown error." : first;
    }

    /// <summary>Decrypts a Windows server's Administrator password with the key pair's .pem file (AWS stores it encrypted).</summary>
    public static async Task<string> GetWindowsPasswordAsync(TunnelConfig t, string pemFile, CancellationToken ct = default)
    {
        var r = await RunAsync(new AppConfig { Profile = t.Profile, Region = t.Region },
            new[] { "ec2", "get-password-data", "--instance-id", t.InstanceId, "--priv-launch-key", pemFile, "--query", "PasswordData", "--output", "text" },
            TimeSpan.FromSeconds(60), ct);
        if (!r.Ok) throw new InvalidOperationException(FriendlyError(r.Error));
        var pw = r.Output.Trim();
        if (pw.Length == 0 || pw == "None") throw new InvalidOperationException("AWS has no generated password for this server (it may use a domain login or a password you set). Use the password you know.");
        return pw;
    }
}
