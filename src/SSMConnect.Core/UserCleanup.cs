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

/// <summary>Everything SSMConnect puts in the current user's profile that the MSI cannot remove itself.</summary>
public static class UserCleanup
{
    public static void RemoveAll()
    {
        try { ToolConfigurator.RemoveAll(); } catch { }
        Shortcuts.RemoveDesktopShortcut();
        // Saved settings, status, logs and the first-run marker for this user.
        try { if (Directory.Exists(ConfigStore.DataDir)) Directory.Delete(ConfigStore.DataDir, recursive: true); } catch { }
        // Tray auto-start for this user is a machine-wide Run entry removed by the installer itself.
    }

    public static string FirstRunMarker =>
        Path.Combine(ConfigStore.DataDir, "first-run.done");
}
