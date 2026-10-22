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

using SSMConnect.Core;

namespace SSMConnect.Wizard;

static class Program
{
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    static int Main(string[] args)
    {
        // Called by the MSI on uninstall (as the installing user) to remove everything SSMConnect put in that user's profile.
        if (args.Contains("--cleanup", StringComparer.OrdinalIgnoreCase))
        {
            try { UserCleanup.RemoveAll(); } catch { }
            return 0;
        }

        // Start-menu "Uninstall SSMConnect": opens the setup's uninstall page (with the keep-my-settings choice)
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            try { Uninstaller.Start(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "SSMConnect"); }
            return 0;
        }

        // Same identity as the Start-menu shortcut, so a taskbar pin keeps pointing at SSMConnect after every update
        try { SetCurrentProcessExplicitAppUserModelID("SSMConnect.Dashboard"); } catch { }
        ApplicationConfiguration.Initialize();

        // One dashboard / tray icon per Windows session. A second launch just brings the first to the front.
        Mutex mutex;
        bool first;
        try { mutex = new Mutex(true, @"Local\SSMConnect.Dashboard", out first); }
        catch (UnauthorizedAccessException) { return 0; }
        if (!first)
        {
            try { using var existing = EventWaitHandle.OpenExisting(@"Local\SSMConnect.Show"); existing.Set(); } catch { }
            return 0;
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SSMConnect.Show");
        var startHidden = args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        var form = new DashboardForm(startHidden);
        var registration = ThreadPool.RegisterWaitForSingleObject(
            showEvent,
            (_, _) => { try { form.BeginInvoke(form.ShowFromAnotherInstance); } catch { } },
            null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(form);

        registration.Unregister(null);
        mutex.ReleaseMutex();
        mutex.Dispose();
        return 0;
    }
}
