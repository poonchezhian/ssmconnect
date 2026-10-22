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

/// <summary>
/// Company sign-in without leaving SSMConnect: the sign-in page opens in the person's own browser (a slim window in Chrome / Edge,
/// a tab in Firefox) and this window waits until the AWS CLI reports the sign-in is done.
/// </summary>
sealed class SignInDialog : Form
{
    readonly string _org, _session, _profile;
    readonly Label _title = new() { AutoSize = false, Font = AppTheme.Semi(14f), ForeColor = AppTheme.Teal900 };
    readonly Label _browserLabel = new() { AutoSize = false, Text = "Opens in your browser:" };
    readonly ComboBox _browser = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label _steps = new() { AutoSize = false, ForeColor = Color.FromArgb(60, 72, 78) };
    readonly Label _code = new() { AutoSize = false, Font = AppTheme.Semi(11f), ForeColor = AppTheme.Teal900, Visible = false };
    readonly Label _status = new() { AutoSize = false, ForeColor = Color.FromArgb(60, 72, 78) };
    readonly Button _again = new() { Text = "Open the window again", Enabled = false };
    readonly Button _cancel = new() { Text = "Cancel" };
    readonly List<BrowserInfo?> _items = new();
    CancellationTokenSource? _cts;
    string? _url;
    bool _done;

    /// <summary>The browser chosen in the list ("" = the PC's default).</summary>
    public string ChosenBrowserId { get; private set; } = "";

    public SignInDialog(string orgLabel, string session, string profile, string? preferredBrowser)
    {
        _org = orgLabel; _session = session; _profile = profile;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Sign in to " + orgLabel;
        Font = AppTheme.Body();
        BackColor = AppTheme.Page;
        ClientSize = new Size(520, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _title.Text = "Sign in to " + orgLabel;
        _steps.Text = string.IsNullOrEmpty(session)
            ? "1.  A browser window opens.\r\n2.  Sign in with your company account.\r\n3.  Come back here. SSMConnect finishes by itself."
            : "1.  A small window opens in your browser, already signed in to your company if you were before.\r\n2.  Confirm or sign in there.\r\n3.  Come back here. SSMConnect finishes by itself and all your servers become ready.";

        // the browser list: found browsers first; the PC's default is pre-selected unless a choice was saved
        var all = BrowserLauncher.Installed();
        var def = BrowserLauncher.Pick(preferredBrowser);
        foreach (var b in all) { _items.Add(b); _browser.Items.Add(b.Name + (def != null && b.Id == def.Id ? "  (detected)" : "")); }
        if (all.Count == 0) { _items.Add(null); _browser.Items.Add("Windows default browser"); }
        var idx = def == null ? 0 : Math.Max(0, all.FindIndex(b => b.Id == def.Id));
        _browser.SelectedIndex = Math.Min(idx, _browser.Items.Count - 1);
        _browser.SelectedIndexChanged += (_, _) => { if (_url != null && !_done) OpenInBrowser(); };

        _status.Text = "Starting the sign-in...";
        foreach (Control c in new Control[] { _title, _browserLabel, _browser, _steps, _code, _status, _again, _cancel }) Controls.Add(c);
        _again.Click += (_, _) => { if (_url != null) OpenInBrowser(); else _ = RunAsync(); };
        _cancel.Click += (_, _) => { _cts?.Cancel(); DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = _cancel;
        Layout += (_, _) => DoLayout();
        Shown += async (_, _) => await RunAsync();
        FormClosing += (_, _) => { if (!_done) { try { _cts?.Cancel(); } catch { } } };
        AppTheme.Style(this);
        DoLayout();
    }

    void DoLayout()
    {
        var f = DeviceDpi / 96f;
        int m = (int)(24 * f), w = ClientSize.Width - 2 * m;
        _title.SetBounds(m, (int)(20 * f), w, (int)(30 * f));
        _browserLabel.SetBounds(m, (int)(66 * f), w, (int)(20 * f));
        _browser.SetBounds(m, (int)(88 * f), Math.Min(w, (int)(300 * f)), (int)(28 * f));
        _steps.SetBounds(m, (int)(130 * f), w, (int)(96 * f));
        _code.SetBounds(m, (int)(228 * f), w, (int)(26 * f));
        _status.SetBounds(m, (int)(262 * f), w, (int)(44 * f));
        _cancel.SetBounds(ClientSize.Width - m - (int)(100 * f), ClientSize.Height - (int)(52 * f), (int)(100 * f), (int)(34 * f));
        _again.SetBounds(_cancel.Left - (int)(190 * f) - (int)(10 * f), _cancel.Top, (int)(190 * f), (int)(34 * f));
    }

    BrowserInfo? Chosen => _browser.SelectedIndex >= 0 && _browser.SelectedIndex < _items.Count ? _items[_browser.SelectedIndex] : null;

    void OpenInBrowser()
    {
        if (_url == null) return;
        ChosenBrowserId = Chosen?.Id ?? "";
        BrowserLauncher.Open(Chosen, _url);
        _status.Text = "Waiting for you to finish signing in in the " + (Chosen?.Name ?? "browser") + " window...";
    }

    async Task RunAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _again.Enabled = false;
        _url = null;
        _status.ForeColor = Color.FromArgb(60, 72, 78);
        _status.Text = "Starting the sign-in...";
        CliResult r;
        try
        {
            if (string.IsNullOrEmpty(_session))
            {
                _status.Text = "A browser window is opening. Sign in there...";
                r = await AwsCli.SsoLoginAsync(new AppConfig { Profile = _profile }, cts.Token);
            }
            else
            {
                r = await AwsSso.LoginInAppAsync(_session,
                    url => { try { BeginInvoke(() => { _url = url; _again.Enabled = true; OpenInBrowser(); }); } catch { } },
                    code => { try { BeginInvoke(() => { _code.Text = "If the page asks for a code:  " + code; _code.Visible = true; }); } catch { } },
                    cts.Token);
            }
        }
        catch (Exception ex) { r = new CliResult(-1, "", ex.Message); }
        if (cts.IsCancellationRequested || IsDisposed) return;

        if (r.Ok)
        {
            _done = true;
            _status.Text = "Signed in.";
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        _status.ForeColor = Color.FromArgb(145, 0, 20);
        _status.Text = AwsCli.FriendlyError(r.Error) + "  Click \"Open the window again\" to try once more.";
        _url = null;
        _again.Text = "Try again";
        _again.Enabled = true;
    }
}
