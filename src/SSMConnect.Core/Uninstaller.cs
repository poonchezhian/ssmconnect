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
using Microsoft.Win32;

namespace SSMConnect.Core;

/// <summary>Finds how SSMConnect was installed (one-click setup bundle or plain MSI) and starts the matching uninstall.</summary>
public static class Uninstaller
{
    const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Returns the program and arguments that uninstall SSMConnect, or null if no installation is registered.</summary>
    public static (string File, string Args)? FindUninstallCommand()
    {
        (string File, string Args)? msi = null;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var root = hklm.OpenSubKey(UninstallRoot);
            if (root == null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                if (k == null || !string.Equals(k.GetValue("DisplayName") as string, "SSMConnect", StringComparison.OrdinalIgnoreCase)) continue;

                // The one-click setup (Burn bundle) registers BundleVersion + an UninstallString; prefer it.
                if (k.GetValue("BundleVersion") != null && k.GetValue("UninstallString") is string us && !string.IsNullOrWhiteSpace(us))
                    return SplitCommand(us);

                if (msi == null && (k.GetValue("WindowsInstaller") is int wi && wi == 1) && name.StartsWith("{"))
                    msi = ("msiexec.exe", $"/x {name}");
            }
        }
        return msi;
    }

    /// <summary>Starts the uninstaller elevated. Throws if SSMConnect is not registered as installed.</summary>
    public static void Start()
    {
        var cmd = FindUninstallCommand()
            ?? throw new InvalidOperationException("SSMConnect is not registered as installed on this PC (running from a build folder?). Use Windows Settings > Apps to remove it.");
        Process.Start(new ProcessStartInfo(cmd.File, cmd.Args) { UseShellExecute = true, Verb = "runas" });
    }

    static (string File, string Args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end > 0) return (command.Substring(1, end - 1), command.Substring(end + 1).Trim());
        }
        var sp = command.IndexOf(' ');
        return sp < 0 ? (command, "") : (command.Substring(0, sp), command.Substring(sp + 1).Trim());
    }
}
