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


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace SSMConnect.BA
{
    /// <summary>One SSMConnect installation found in Windows' list of installed programs.</summary>
    internal sealed class InstalledCopy
    {
        public string Key;
        public string Version;
        public bool IsBundle;
        public string UninstallFile;
        public string UninstallArgs;
        public override string ToString() { return "SSMConnect " + (string.IsNullOrEmpty(Version) ? "" : Version); }
    }

    internal static class ExistingInstall
    {
        const string Root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<InstalledCopy> Find()
        {
            var list = new List<InstalledCopy>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var root = hklm.OpenSubKey(Root))
                {
                    if (root == null) continue;
                    foreach (var name in root.GetSubKeyNames())
                    {
                        using (var k = root.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            var dn = k.GetValue("DisplayName") as string;
                            if (!string.Equals(dn, "SSMConnect", StringComparison.OrdinalIgnoreCase)) continue;
                            if (list.Any(x => x.Key == name)) continue;
                            var c = new InstalledCopy { Key = name, Version = k.GetValue("DisplayVersion") as string };
                            if (k.GetValue("BundleVersion") != null)
                            {
                                c.IsBundle = true;
                                var cmd = (k.GetValue("QuietUninstallString") as string) ?? ((k.GetValue("UninstallString") as string) + " /quiet");
                                Split(cmd, out c.UninstallFile, out c.UninstallArgs);
                            }
                            else if (name.StartsWith("{") && k.GetValue("WindowsInstaller") is int wi && wi == 1)
                            {
                                c.UninstallFile = "msiexec.exe";
                                c.UninstallArgs = "/x " + name + " /qn /norestart";
                            }
                            else continue;
                            list.Add(c);
                        }
                    }
                }
            }
            return list;
        }


        const string HideScript = @"param([string]$Exe,[string]$ExeArgs,[int]$Skip)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class SsmWin {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport(""user32.dll"")] static extern bool EnumWindows(CB c, IntPtr l);
  [DllImport(""user32.dll"")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport(""user32.dll"", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport(""user32.dll"")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport(""user32.dll"")] static extern bool ShowWindow(IntPtr h, int c);
  public static void Hide(uint skip) {
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      StringBuilder sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.ToString() == ""SSMConnect Setup"") { uint p; GetWindowThreadProcessId(h, out p); if (p != skip) ShowWindow(h, 0); }
      return true; }, IntPtr.Zero);
  }
}
'@
$p = Start-Process -FilePath $Exe -ArgumentList $ExeArgs -PassThru -WindowStyle Hidden
while (-not $p.HasExited) { [SsmWin]::Hide([uint32]$Skip); Start-Sleep -Milliseconds 25 }
$p.WaitForExit()
[SsmWin]::Hide([uint32]$Skip)
exit $p.ExitCode
";

        static void Split(string command, out string file, out string args)
        {
            command = (command ?? "").Trim();
            if (command.StartsWith("\""))
            {
                var end = command.IndexOf('"', 1);
                if (end > 0) { file = command.Substring(1, end - 1); args = command.Substring(end + 1).Trim(); return; }
            }
            var sp = command.IndexOf(' ');
            if (sp < 0) { file = command; args = ""; } else { file = command.Substring(0, sp); args = command.Substring(sp + 1).Trim(); }
        }

        /// <summary>Removes every SSMConnect found, bundles first. Returns null on success, otherwise a plain-language problem.</summary>
        public static string RemoveAll(Action<string> status)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("SSMConnect.Wizard")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }

                for (var pass = 0; pass < 3; pass++)
                {
                    var found = Find();
                    if (found.Count == 0) return null;
                    var next = found.OrderByDescending(x => x.IsBundle).First();
                    status("Uninstalling " + next + "... (approve the Windows prompt if it appears)");
                    ProcessStartInfo psi;
                    if (next.IsBundle)
                    {
                        // Older setup builds always open their own window, even when told to be quiet. Run them through an
                        // elevated, hidden helper that keeps that window hidden, so this setup stays the only window.
                        var script = Path.Combine(Path.GetTempPath(), "ssmconnect-remove-" + Guid.NewGuid().ToString("N") + ".ps1");
                        File.WriteAllText(script, HideScript);
                        var args = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -Exe \"" + next.UninstallFile +
                                   "\" -ExeArgs \"" + next.UninstallArgs.Replace("\"", "\\\"") + "\" -Skip " + Process.GetCurrentProcess().Id;
                        psi = new ProcessStartInfo("powershell.exe", args)
                        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                    }
                    else
                    {
                        psi = new ProcessStartInfo(next.UninstallFile, next.UninstallArgs)
                        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                    }
                    using (var proc = Process.Start(psi))
                    {
                        if (!proc.WaitForExit(15 * 60 * 1000)) return "Removing the existing copy took too long.";
                        if (proc.ExitCode != 0 && proc.ExitCode != 3010 && proc.ExitCode != 1605 && proc.ExitCode != 1641)
                            return "The existing copy could not be removed (code " + proc.ExitCode + ").";
                    }
                    // the whole set is re-read on the next pass, because removing a bundle removes its MSI too
                    if (Find().Count > 0 && pass == 2) return "A copy of SSMConnect is still installed after removal.";
                }
                return Find().Count == 0 ? null : "A copy of SSMConnect is still installed after removal.";
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return "The Windows permission prompt was cancelled, so the existing copy was not removed.";
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
