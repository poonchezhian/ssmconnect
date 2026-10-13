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


using System.Text.RegularExpressions;

namespace SSMConnect.Core;

/// <summary>Checks everything read from config.json before it is used to start a process or write to another tool.</summary>
public static class ConfigValidator
{
    static readonly Regex InstanceId = new(@"^m?i-[0-9a-f]{8,17}$", RegexOptions.Compiled);
    static readonly Regex ProfileName = new(@"^[A-Za-z0-9_.@:+=,\- ]{1,128}$", RegexOptions.Compiled);
    static readonly Regex RegionName = new(@"^[a-z]{2}(-[a-z]+)+-\d$", RegexOptions.Compiled);
    static readonly Regex LoginName = new(@"^[A-Za-z0-9_.\-]{1,64}$", RegexOptions.Compiled);

    public static string? ValidateGlobal(AppConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Profile) && !ProfileName.IsMatch(cfg.Profile))
            return "The AWS profile name contains characters that are not allowed.";
        if (!string.IsNullOrWhiteSpace(cfg.Region) && !RegionName.IsMatch(cfg.Region))
            return $"'{cfg.Region}' does not look like an AWS region (for example ap-south-1).";
        return null;
    }

    /// <summary>Returns null when the tunnel is fine, otherwise a plain-language reason.</summary>
    public static string? ValidateTunnel(TunnelConfig t, IEnumerable<TunnelConfig> all)
    {
        if (string.IsNullOrWhiteSpace(t.Profile)) return "No AWS account is chosen for this server. Use Tools > Set up / change connections.";
        if (!ProfileName.IsMatch(t.Profile)) return "The AWS profile name contains characters that are not allowed.";
        if (!RegionName.IsMatch(t.Region ?? "")) return $"'{t.Region}' does not look like an AWS region (for example ap-south-1). Use Tools > Set up / change connections.";
        if (!InstanceId.IsMatch(t.InstanceId ?? "")) return $"'{t.InstanceId}' is not a valid EC2 instance ID (it looks like i-0123456789abcdef0).";
        if (t.LocalPort is < 1024 or > 65535) return $"Local port {t.LocalPort} is not allowed. Use a port from 1024 to 65535.";
        if (t.RemotePort is < 1 or > 65535) return $"Remote port {t.RemotePort} is not valid.";
        if (all.Count(o => o.Enabled && o.LocalPort == t.LocalPort) > 1) return $"Local port {t.LocalPort} is used by more than one connection.";
        if (!LoginName.IsMatch(t.UserName ?? "")) return "The login user name contains characters that are not allowed.";
        return null;
    }
}
