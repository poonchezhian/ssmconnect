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

namespace SSMConnect.Core;

/// <summary>Opens Windows Remote Desktop to a server through its SSM tunnel (127.0.0.1:local port).</summary>
public static class RdpLauncher
{
    public static string RdpFile(TunnelConfig t)
    {
        var dir = Path.Combine(ConfigStore.DataDir, "rdp");
        Directory.CreateDirectory(dir);
        var safe = string.Concat(t.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ' ? c : '_'));
        return Path.Combine(dir, safe + ".rdp");
    }

    public static void Open(TunnelConfig t)
    {
        var user = string.IsNullOrWhiteSpace(t.UserName) ? "Administrator" : t.UserName;
        var server = $"127.0.0.1:{t.LocalPort}";
        try
        {
            // pre-fill the user name; launching without an .rdp file also avoids the "unknown publisher" warning
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Terminal Server Client\Servers\" + server);
            key?.SetValue("UsernameHint", user);
        }
        catch { }
        Process.Start(new ProcessStartInfo("mstsc.exe") { ArgumentList = { "/v:" + server }, UseShellExecute = true });
    }
}
