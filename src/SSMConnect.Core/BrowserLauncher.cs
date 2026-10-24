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

/// <summary>A web browser installed on this PC.</summary>
public sealed record BrowserInfo(string Id, string Name, string? Exe)
{
    public override string ToString() => Name;
    /// <summary>Chrome, Edge and Brave can show a page as a small app-style window with no tabs or address bar.</summary>
    public bool CanAppWindow => Id is "chrome" or "edge" or "brave";
}

/// <summary>Finds the browser the person signs in to AWS with and opens the sign-in page in it.</summary>
public static class BrowserLauncher
{
    static readonly (string Id, string Name, string Exe, string[] Dirs)[] Known =
    {
        ("chrome", "Google Chrome", "chrome.exe", new[] { @"Google\Chrome\Application\chrome.exe" }),
        ("edge", "Microsoft Edge", "msedge.exe", new[] { @"Microsoft\Edge\Application\msedge.exe" }),
        ("firefox", "Mozilla Firefox", "firefox.exe", new[] { @"Mozilla Firefox\firefox.exe" }),
        ("brave", "Brave", "brave.exe", new[] { @"BraveSoftware\Brave-Browser\Application\brave.exe" }),
    };

    static string? AppPath(string exe)
    {
        foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            try
            {
                using var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                var v = k?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(v))
                {
                    v = v.Trim().Trim('"');
                    if (File.Exists(v)) return v;
                }
            }
            catch { }
        }
        return null;
    }

    static string? FindExe(string exe, string[] dirs)
    {
        var viaRegistry = AppPath(exe);
        if (viaRegistry != null) return viaRegistry;
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetEnvironmentVariable("LocalAppData"),
        };
        foreach (var r in roots)
        {
            if (string.IsNullOrEmpty(r)) continue;
            foreach (var d in dirs)
            {
                try { var p = Path.Combine(r, d); if (File.Exists(p)) return p; } catch { }
            }
        }
        return null;
    }

    /// <summary>The browsers found on this PC.</summary>
    public static List<BrowserInfo> Installed()
    {
        var list = new List<BrowserInfo>();
        foreach (var k in Known)
        {
            var exe = FindExe(k.Exe, k.Dirs);
            if (exe != null) list.Add(new BrowserInfo(k.Id, k.Name, exe));
        }
        return list;
    }

    /// <summary>The browser Windows opens web links with (the person's own), or null when it cannot be told.</summary>
    public static BrowserInfo? Default()
    {
        string? prog = null;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            prog = k?.GetValue("ProgId") as string;
        }
        catch { }
        if (string.IsNullOrEmpty(prog)) return null;
        string id;
        if (prog.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) id = "chrome";
        else if (prog.Contains("MSEdge", StringComparison.OrdinalIgnoreCase)) id = "edge";
        else if (prog.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) id = "firefox";
        else if (prog.Contains("Brave", StringComparison.OrdinalIgnoreCase)) id = "brave";
        else return null;
        return Installed().FirstOrDefault(b => b.Id == id);
    }

    /// <summary>The browser to use: the saved choice if it is installed, else the PC's default, else Edge, else the first one found.</summary>
    public static BrowserInfo? Pick(string? preferredId)
    {
        var all = Installed();
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            var chosen = all.FirstOrDefault(b => b.Id == preferredId);
            if (chosen != null) return chosen;
        }
        return Default() ?? all.FirstOrDefault(b => b.Id == "edge") ?? all.FirstOrDefault();
    }

    /// <summary>Opens the page as a new tab in the person's own browser and profile, so an existing sign-in is reused. The PC's default browser is opened the way Windows opens any link.</summary>
    public static void Open(BrowserInfo? browser, string url)
    {
        try
        {
            var isDefault = browser != null && Default()?.Id == browser.Id;
            if (browser?.Exe == null || isDefault) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return; }
            var psi = new ProcessStartInfo(browser.Exe) { UseShellExecute = false };
            psi.ArgumentList.Add(url);
            Process.Start(psi);
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
