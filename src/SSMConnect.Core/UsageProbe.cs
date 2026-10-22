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
using System.Text.RegularExpressions;

namespace SSMConnect.Core;

/// <summary>What is known about one server's size and load. A value of 0 (size) or -1 (usage) means "not known".</summary>
public sealed class UsageInfo
{
    public string Key { get; set; } = "";
    public string InstanceType { get; set; } = "";
    public int Vcpu { get; set; }
    public int RamMb { get; set; }
    public int DiskGb { get; set; }
    public string OsName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public int CpuPct { get; set; } = -1;
    public int RamPct { get; set; } = -1;
    public int DiskPct { get; set; } = -1;
    public DateTime SpecUtc { get; set; }
    public DateTime UsageUtc { get; set; }
    /// <summary>Why a reading is missing (for example the sign-in has no permission to run commands). Shown as a tooltip, never as an error.</summary>
    public string Note { get; set; } = "";

    public UsageInfo Clone() => (UsageInfo)MemberwiseClone();

    public bool HasUsage => CpuPct >= 0 || RamPct >= 0 || DiskPct >= 0;

    /// <summary>"Ubuntu 22.04 · 2 vCPU · 8 GB RAM · 100 GB disk" with whatever is known.</summary>
    public string SpecLine()
    {
        var parts = new List<string>();
        var os = (OsName + " " + OsVersion).Trim();
        if (os.Length > 0) parts.Add(os);
        if (Vcpu > 0) parts.Add(Vcpu + " vCPU");
        if (RamMb > 0) parts.Add((RamMb % 1024 == 0 ? (RamMb / 1024).ToString() : (RamMb / 1024.0).ToString("0.#")) + " GB RAM");
        if (DiskGb > 0) parts.Add(DiskGb + " GB disk");
        return string.Join("  ·  ", parts);
    }
}

/// <summary>
/// Reads server size (from AWS: instance type, volumes, SSM) and usage (a short read-only command run through SSM) and
/// remembers the last values in usage.json. Anything AWS refuses is left blank; nothing here ever raises an error to the person.
/// </summary>
public static class UsageProbe
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    static readonly object Gate = new();
    public static string Path => System.IO.Path.Combine(ConfigStore.DataDir, "usage.json");

    public static string KeyOf(TunnelConfig t) => t.Profile + "|" + t.InstanceId;

    public static Dictionary<string, UsageInfo> Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(Path)) return new Dictionary<string, UsageInfo>(StringComparer.Ordinal);
                var list = JsonSerializer.Deserialize<List<UsageInfo>>(File.ReadAllText(Path), Options) ?? new List<UsageInfo>();
                return list.Where(u => !string.IsNullOrEmpty(u.Key)).GroupBy(u => u.Key).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            }
            catch { return new Dictionary<string, UsageInfo>(StringComparer.Ordinal); }
        }
    }

    public static void Save(Dictionary<string, UsageInfo> all)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.DataDir);
                var temp = Path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(all.Values.ToList(), Options));
                File.Move(temp, Path, overwrite: true);
            }
            catch { }
        }
    }

    /// <summary>Accounts whose role may not run SSM commands: not retried for an hour so the denials do not pile up in CloudTrail.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> Denied = new();
    const string DeniedNote = "Usage is not available: your AWS role is not allowed to run commands through SSM (ssm:SendCommand).";

    static AppConfig Acct(TunnelConfig t) => new() { Profile = t.Profile, Region = t.Region };

    static async Task<string?> Run(TunnelConfig t, string[] args, CancellationToken ct, int seconds = 40)
    {
        try
        {
            var r = await AwsCli.RunAsync(Acct(t), args, TimeSpan.FromSeconds(seconds), ct);
            return r.Ok ? r.Output.Trim() : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    static readonly Dictionary<string, (int Vcpu, int RamMb)> TypeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the server's size and operating system. Fills what AWS allows and keeps older values for the rest.</summary>
    public static async Task<UsageInfo> ReadSpecAsync(TunnelConfig t, UsageInfo? prior, CancellationToken ct = default)
    {
        var info = prior ?? new UsageInfo();
        info.Key = KeyOf(t);
        info.SpecUtc = DateTime.UtcNow;

        var type = await Run(t, new[] { "ec2", "describe-instances", "--instance-ids", t.InstanceId, "--query", "Reservations[0].Instances[0].InstanceType", "--output", "text" }, ct);
        if (!string.IsNullOrWhiteSpace(type) && type != "None") info.InstanceType = type!.Trim();
        if (info.InstanceType.Length > 0)
        {
            (int Vcpu, int RamMb) cached;
            bool have;
            lock (TypeCache) have = TypeCache.TryGetValue(info.InstanceType, out cached);
            if (!have)
            {
                var spec = await Run(t, new[] { "ec2", "describe-instance-types", "--instance-types", info.InstanceType, "--query", "InstanceTypes[0].[VCpuInfo.DefaultVCpus,MemoryInfo.SizeInMiB]", "--output", "text" }, ct);
                var nums = Regex.Matches(spec ?? "", @"\d+").Select(m => int.Parse(m.Value)).ToList();
                if (nums.Count >= 2) { cached = (nums[0], nums[1]); lock (TypeCache) TypeCache[info.InstanceType] = cached; have = true; }
            }
            if (have) { info.Vcpu = cached.Vcpu; info.RamMb = cached.RamMb; }
        }

        var disk = await Run(t, new[] { "ec2", "describe-volumes", "--filters", "Name=attachment.instance-id,Values=" + t.InstanceId, "--query", "sum(Volumes[].Size)", "--output", "text" }, ct);
        if (int.TryParse((disk ?? "").Split('.')[0], out var gb) && gb > 0) info.DiskGb = gb;

        var os = await Run(t, new[] { "ssm", "describe-instance-information", "--filters", "Key=InstanceIds,Values=" + t.InstanceId, "--query", "InstanceInformationList[0].[PlatformName,PlatformVersion]", "--output", "text" }, ct);
        if (!string.IsNullOrWhiteSpace(os) && os != "None")
        {
            var parts = os!.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && parts[0] != "None") info.OsName = ShortOs(parts[0]);
            if (parts.Length > 1 && parts[1] != "None") info.OsVersion = ShortVersion(parts[1]);
        }
        return info;
    }

    static string ShortOs(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("Microsoft Windows Server", StringComparison.OrdinalIgnoreCase)) return "Windows Server";
        if (raw.StartsWith("Red Hat Enterprise Linux", StringComparison.OrdinalIgnoreCase)) return "Red Hat Linux";
        if (raw.StartsWith("Debian GNU/Linux", StringComparison.OrdinalIgnoreCase)) return "Debian";
        return raw;
    }

    static string ShortVersion(string raw)
    {
        raw = raw.Trim();
        var m = Regex.Match(raw, @"^\d+(\.\d+)?");
        return m.Success ? m.Value : raw;
    }

    const string LinuxScript =
        "a=$(awk '/^cpu /{print $2+$3+$4+$5+$6+$7+$8, $5+$6}' /proc/stat); sleep 1; " +
        "b=$(awk '/^cpu /{print $2+$3+$4+$5+$6+$7+$8, $5+$6}' /proc/stat); " +
        "echo \"$a $b\" | awk '{t=$3-$1; i=$4-$2; c=(t>0)?100*(t-i)/t:0; printf \"CPU=%d \", c}'; " +
        "awk '/MemTotal/{t=$2}/MemAvailable/{a=$2}END{if(t>0)printf \"MEM=%d \", 100*(t-a)/t}' /proc/meminfo; " +
        "df -P / | awk 'NR==2{gsub(\"%\",\"\",$5); printf \"DISK=%d\", $5}'";

    const string WindowsScript =
        "$c=(Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average; " +
        "$o=Get-CimInstance Win32_OperatingSystem; $m=[int](100*($o.TotalVisibleMemorySize-$o.FreePhysicalMemory)/$o.TotalVisibleMemorySize); " +
        "$d=Get-CimInstance Win32_LogicalDisk -Filter \"DeviceID='C:'\"; $dp=[int](100*($d.Size-$d.FreeSpace)/$d.Size); " +
        "Write-Output (\"CPU=\" + [int]$c + \" MEM=\" + $m + \" DISK=\" + $dp)";

    /// <summary>Runs the short read-only command on the server through SSM and stores CPU, memory and disk percentages.</summary>
    public static async Task<UsageInfo> ReadUsageAsync(TunnelConfig t, UsageInfo? prior, CancellationToken ct = default)
    {
        var info = prior ?? new UsageInfo();
        info.Key = KeyOf(t);
        var acct = t.Profile + "|" + t.Region;
        if (Denied.TryGetValue(acct, out var until) && DateTime.UtcNow < until) { info.Note = DeniedNote; info.UsageUtc = DateTime.UtcNow; return info; }
        var windows = t.IsRdp;
        var parameters = JsonSerializer.Serialize(new Dictionary<string, string[]> { ["commands"] = new[] { windows ? WindowsScript : LinuxScript } });
        try
        {
            var send = await AwsCli.RunAsync(Acct(t), new[]
            {
                "ssm", "send-command", "--instance-ids", t.InstanceId,
                "--document-name", windows ? "AWS-RunPowerShellScript" : "AWS-RunShellScript",
                "--parameters", parameters, "--query", "Command.CommandId", "--output", "text",
            }, TimeSpan.FromSeconds(40), ct);
            if (!send.Ok)
            {
                var denied = send.Error.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) || send.Error.Contains("not authorized", StringComparison.OrdinalIgnoreCase);
                if (denied) Denied[acct] = DateTime.UtcNow.AddHours(1);
                info.Note = denied ? DeniedNote : "Usage could not be read right now.";
                info.UsageUtc = DateTime.UtcNow;
                return info;
            }
            var id = send.Output.Trim();
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(2000, ct);
                var inv = await AwsCli.RunAsync(Acct(t), new[]
                {
                    "ssm", "get-command-invocation", "--command-id", id, "--instance-id", t.InstanceId,
                    "--query", "{s:Status,o:StandardOutputContent}", "--output", "json",
                }, TimeSpan.FromSeconds(30), ct);
                if (!inv.Ok) continue;   // not visible yet
                using var doc = JsonDocument.Parse(inv.Output);
                var status = doc.RootElement.TryGetProperty("s", out var s) ? s.GetString() ?? "" : "";
                if (status is "Pending" or "InProgress" or "Delayed") continue;
                if (status != "Success") { info.Note = "Usage could not be read right now (" + status + ")."; break; }
                var text = doc.RootElement.TryGetProperty("o", out var o) ? o.GetString() ?? "" : "";
                info.CpuPct = Pct(text, "CPU");
                info.RamPct = Pct(text, "MEM");
                info.DiskPct = Pct(text, "DISK");
                info.Note = info.HasUsage ? "" : "Usage could not be read right now.";
                break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { info.Note = "Usage could not be read right now."; AppLog.Warn("Usage read failed for " + t.Name + ": " + ex.Message); }
        info.UsageUtc = DateTime.UtcNow;
        return info;
    }

    static int Pct(string text, string name)
    {
        var m = Regex.Match(text, name + @"=(\d{1,3})");
        return m.Success ? Math.Clamp(int.Parse(m.Groups[1].Value), 0, 100) : -1;
    }
}
