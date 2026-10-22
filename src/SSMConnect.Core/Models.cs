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

using System.Text.Json.Serialization;

namespace SSMConnect.Core;

/// <summary>One SSH-over-SSM tunnel: local 127.0.0.1:LocalPort -> instance:RemotePort.</summary>
public sealed class TunnelConfig
{
    public string Name { get; set; } = "";
    public string InstanceId { get; set; } = "";
    /// <summary>AWS account profile (from ~/.aws) this server is reached with. Different servers can use different accounts.</summary>
    public string Profile { get; set; } = "";
    /// <summary>AWS region the server lives in.</summary>
    public string Region { get; set; } = "";
    public int LocalPort { get; set; }
    public int RemotePort { get; set; } = 22;
    /// <summary>"ssh" (Linux, PuTTY / WinSCP / MobaXterm) or "rdp" (Windows, Remote Desktop).</summary>
    public string Protocol { get; set; } = "ssh";
    [JsonIgnore] public bool IsRdp => string.Equals(Protocol, "rdp", StringComparison.OrdinalIgnoreCase);
    public string UserName { get; set; } = "ec2-user";
    /// <summary>What kind of server this is (Production, Staging, ...), taken from the server's AWS tags when it was found.</summary>
    public string Environment { get; set; } = "";
    public string? KeyFile { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class AppConfig
{
    /// <summary>The account and region last chosen in setup (only pre-selects the lists; each tunnel stores its own).</summary>
    public string Profile { get; set; } = "";
    public string Region { get; set; } = "";

    public List<TunnelConfig> Tunnels { get; set; } = new();
    /// <summary>Friendly names the person gave to accounts. Key: the primary account key or a profile name.</summary>
    public Dictionary<string, string> Aliases { get; set; } = new();

    /// <summary>Minutes a connection stays open after the last tool closed and no data moves (15, 30 or 60).</summary>
    public int IdleMinutes { get; set; } = 15;
    /// <summary>Browser used for company sign-in: "chrome", "edge", "firefox", "brave", or empty for the PC's default.</summary>
    public string PreferredBrowser { get; set; } = "";

    /// <summary>The friendly name for an account key (or the given fallback when none was set).</summary>
    public string AliasOr(string key, string fallback) =>
        Aliases != null && Aliases.TryGetValue(key, out var a) && !string.IsNullOrWhiteSpace(a) ? a : fallback;
}

public sealed class DiscoveredServer
{
    public string Name { get; set; } = "";
    /// <summary>The saved AWS profile (account) this server was found in.</summary>
    public string Profile { get; set; } = "";
    public string Environment { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string PrivateIp { get; set; } = "";
    public string Platform { get; set; } = "Linux";
    /// <summary>Name of the EC2 key pair the server was launched with (the key file is usually called the same).</summary>
    public string KeyName { get; set; } = "";
    /// <summary>AWS's fingerprint of that key pair (empty when the account does not allow reading it).</summary>
    public string KeyFingerprint { get; set; } = "";
    public bool SsmOnline { get; set; }
    /// <summary>Operating system name reported by the SSM agent, e.g. "Ubuntu", "Amazon Linux", "Debian GNU/Linux".</summary>
    public string OsName { get; set; } = "";

    /// <summary>The login user that distribution's cloud images use by default.</summary>
    public string SuggestedUser
    {
        get
        {
            if (IsWindows) return "Administrator";
            var n = OsName ?? "";
            bool Has(string x) => n.Contains(x, StringComparison.OrdinalIgnoreCase);
            if (Has("ubuntu")) return "ubuntu";
            if (Has("debian")) return "admin";
            if (Has("centos")) return "centos";
            if (Has("fedora")) return "fedora";
            if (Has("rocky")) return "rocky";
            return "ec2-user"; // Amazon Linux, Red Hat, SUSE, AlmaLinux, Oracle Linux and unknown
        }
    }

    public bool IsProd => EnvironmentClassifier.IsProd(Environment);
    public bool IsWindows => Platform.Contains("windows", StringComparison.OrdinalIgnoreCase);
}

public static class TunnelStates
{
    /// <summary>Signed in and nothing open yet: the connection opens by itself when a tool is clicked.</summary>
    public const string Ready = "Ready";
    /// <summary>Gave up after 3 tries. Stays down until the person signs in again.</summary>
    public const string Stopped = "Stopped";
    public const string Starting = "Starting";
    public const string Connected = "Connected";
    public const string Retrying = "Retrying";
    public const string Error = "Error";
    public const string Disabled = "Disabled";
}

/// <summary>Live state of one tunnel, written by the tunnel manager and read by the dashboard.</summary>
public sealed class TunnelStatus
{
    public string Name { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public int LocalPort { get; set; }
    public string State { get; set; } = TunnelStates.Starting;
    public string Message { get; set; } = "";
    public DateTime SinceUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Tools currently attached to the tunnel (established connections on its local port).</summary>
    public int Clients { get; set; }
    /// <summary>Seconds until an unused connection closes; -1 while a tool is attached or nothing is open.</summary>
    public int IdleLeftSeconds { get; set; } = -1;
    /// <summary>Which try the manager is on while connecting (1 to 3).</summary>
    public int Attempt { get; set; }
}

public sealed class StatusFile
{
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<TunnelStatus> Tunnels { get; set; } = new();
}
