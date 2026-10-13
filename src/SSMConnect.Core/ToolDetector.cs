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


using Microsoft.Win32;

namespace SSMConnect.Core;

/// <summary>Tells which of the supported connection tools are installed on this PC, so setup only configures those.</summary>
public static class ToolDetector
{
    public static string? PuttyPath => FindExe("putty.exe", @"PuTTY\putty.exe");
    public static string? WinScpPath => FindExe("WinSCP.exe", @"WinSCP\WinSCP.exe");

    public static bool PuttyInstalled =>
        RegKey(Registry.CurrentUser, @"Software\SimonTatham\PuTTY") || PuttyPath != null;

    public static bool WinScpInstalled =>
        RegKey(Registry.CurrentUser, @"Software\Martin Prikryl\WinSCP 2") || WinScpPath != null;

    public static bool MobaXtermInstalled =>
        File.Exists(ToolConfigurator.MobaIniPath) || FindExe("MobaXterm.exe", @"Mobatek\MobaXterm\MobaXterm.exe", @"MobaXterm\MobaXterm.exe") != null;

    static bool RegKey(RegistryKey root, string path)
    {
        try { using var k = root.OpenSubKey(path); return k != null; } catch { return false; }
    }

    /// <summary>Full path of the program if it is installed in a usual place, on PATH, or registered with Windows; otherwise null.</summary>
    public static string? FindExe(string exe, params string[] relativePaths)
    {
        try
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            };
            foreach (var r in roots.Where(x => !string.IsNullOrEmpty(x)))
                foreach (var rel in relativePaths)
                {
                    var f = Path.Combine(r, rel);
                    if (File.Exists(f)) return f;
                }

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(f)) return f;
            }

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                if (k?.GetValue(null) is string v && File.Exists(v.Trim('"'))) return v.Trim('"');
            }
        }
        catch { }
        return null;
    }
}
