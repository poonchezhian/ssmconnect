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

namespace SSMConnect.Core;

public static class Shortcuts
{
    public static string DesktopShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SSMConnect.lnk");

    public static bool DesktopShortcutExists => File.Exists(DesktopShortcutPath);

    /// <summary>Creates (or refreshes) a desktop shortcut that opens the dashboard.</summary>
    public static void CreateDesktopShortcut(string exePath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is not available on this PC.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(DesktopShortcutPath);
        link.TargetPath = exePath;
        link.WorkingDirectory = Path.GetDirectoryName(exePath);
        link.Description = "SSMConnect - SSH over AWS Systems Manager";
        link.IconLocation = exePath + ",0";
        link.Save();
    }

    public static void RemoveDesktopShortcut()
    {
        try { if (File.Exists(DesktopShortcutPath)) File.Delete(DesktopShortcutPath); } catch { }
    }
}
