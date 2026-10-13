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

using System.ComponentModel;
using System.Diagnostics;

namespace SSMConnect.Core;

/// <summary>
/// Downloads and silently installs the AWS CLI v2 and the AWS Session Manager plugin when they are missing.
/// Every download is checked for a valid Authenticode signature from Amazon before it is run, and the install
/// itself runs elevated (the user sees one Windows UAC prompt per installer).
/// </summary>
public static class PrereqInstaller
{
    static string WorkDir
    {
        get
        {
            var d = Path.Combine(Path.GetTempPath(), "SSMConnect-prereqs");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Installs whatever is missing. Returns true when both tools are present afterwards.</summary>
    public static async Task<bool> InstallMissingAsync(IProgress<string> status, IProgress<int> percent, CancellationToken ct = default)
    {
        if (AwsCli.FindAws() == null)
        {
            var msi = await DownloadAsync(AwsCli.AwsDownloadUrl, "AWSCLIV2.msi", "AWS CLI v2", status, percent, ct);
            await VerifyAsync(msi, status);
            status.Report("Installing AWS CLI v2 (approve the Windows prompt if it appears)...");
            percent.Report(-1); // marquee
            var code = await RunElevatedAsync("msiexec.exe", $"/i \"{msi}\" /qn /norestart");
            if (code is not (0 or 3010 or 1641))
                throw new InvalidOperationException($"The AWS CLI installer failed (exit code {code}).");
        }

        if (AwsCli.FindPlugin() == null)
        {
            var exe = await DownloadAsync(AwsCli.PluginDownloadUrl, "SessionManagerPluginSetup.exe", "Session Manager plugin", status, percent, ct);
            await VerifyAsync(exe, status);
            status.Report("Installing the Session Manager plugin (approve the Windows prompt if it appears)...");
            percent.Report(-1);
            await RunElevatedAsync(exe, "/quiet");

            if (AwsCli.FindPlugin() == null)
            {
                // Silent switch not honoured on this machine: fall back to the normal installer window.
                status.Report("Opening the plugin installer. Click Next / Install / Close to finish...");
                await RunElevatedAsync(exe, "");
            }
        }

        percent.Report(100);
        return AwsCli.FindAws() != null && AwsCli.FindPlugin() != null;
    }

    static async Task<string> DownloadAsync(string url, string fileName, string label, IProgress<string> status, IProgress<int> percent, CancellationToken ct)
    {
        var path = Path.Combine(WorkDir, fileName);
        status.Report($"Downloading {label}...");
        percent.Report(0);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(path))
        {
            var buf = new byte[81920];
            long read = 0;
            int last = -1, n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                read += n;
                if (total > 0)
                {
                    var p = (int)(read * 100 / total);
                    if (p != last) { last = p; percent.Report(p); }
                }
            }
        }
        return path;
    }

    /// <summary>Refuses to run a download unless Windows reports a valid signature issued to Amazon.</summary>
    static async Task VerifyAsync(string path, IProgress<string> status)
    {
        status.Report("Checking the download is signed by Amazon...");
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("$s = Get-AuthenticodeSignature -LiteralPath $env:SSM_FILE; \"$($s.Status)|$($s.SignerCertificate.Subject)\"");
        psi.Environment["SSM_FILE"] = path;

        using var p = Process.Start(psi)!;
        var output = (await p.StandardOutput.ReadToEndAsync()).Trim();
        await p.WaitForExitAsync();

        if (!output.StartsWith("Valid|", StringComparison.Ordinal) || !output.Contains("Amazon", StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidOperationException("The downloaded installer is not signed by Amazon, so it was deleted and not run.");
        }
    }

    static async Task<int> RunElevatedAsync(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("The Windows permission prompt was cancelled, so nothing was installed. Click the button again and choose Yes.");
        }
    }
}
