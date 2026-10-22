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

using System.Reflection;

namespace SSMConnect.Wizard;

/// <summary>The operating-system logos shown on server cards (embedded PNG files in Assets\os).</summary>
static class OsIcons
{
    static readonly Dictionary<string, Image?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Picks the logo for the operating-system name SSM reports ("Ubuntu", "Amazon Linux", "Microsoft Windows Server 2022"...).</summary>
    public static string KeyFor(string? osName, bool windows)
    {
        var n = (osName ?? "").ToLowerInvariant();
        if (n.Contains("windows") || windows && n.Length == 0) return "windows";
        if (n.Contains("ubuntu")) return "ubuntu";
        if (n.Contains("debian")) return "debian";
        if (n.Contains("red hat") || n.Contains("rhel")) return "rhel";
        if (n.Contains("amazon")) return "amazon-linux";
        if (n.Contains("centos")) return "centos";
        if (n.Contains("rocky")) return "rocky";
        if (n.Contains("alma")) return "almalinux";
        if (n.Contains("fedora")) return "fedora";
        if (n.Contains("suse") || n.Contains("sles")) return "suse";
        if (n.Contains("alpine")) return "alpine";
        return windows ? "windows" : "linux";
    }

    public static Image? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var img)) return img;
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("os." + key + ".png");
                img = stream == null ? null : new Bitmap(stream);
            }
            catch { img = null; }
            Cache[key] = img;
            return img;
        }
    }
}
