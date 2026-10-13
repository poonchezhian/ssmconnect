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
/// "Add an AWS account" dialog. Creates a named profile in ~/.aws, either through the company sign-in (SSO, with the
/// accounts and roles looked up for the person) or with access keys. Can be used any number of times, one per AWS account.
/// </summary>
public sealed class ProfileDialog : Form
{
    static readonly Color Good = Color.FromArgb(0, 110, 40);
    static readonly Color Bad = Color.FromArgb(175, 0, 20);
    static readonly Color Muted = Color.FromArgb(70, 70, 70);
    const int W = 600;

    readonly RadioButton _rbSso = new() { Text = "Company sign-in (SSO) - most companies", AutoSize = true, Checked = true };
    readonly RadioButton _rbKeys = new() { Text = "Access keys (from the AWS console)", AutoSize = true };
    readonly TextBox _name = new() { Width = 260 };
    readonly TextBox _startUrl = new() { Width = 460 };
    readonly ComboBox _ssoRegion = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Button _signIn = new() { Text = "Sign in and find my accounts", AutoSize = true, MinimumSize = new Size(280, 38), Padding = new Padding(10, 2, 10, 2) };
    readonly Label _ssoStatus = new() { AutoSize = true, MaximumSize = new Size(W, 0) };
    readonly ComboBox _account = new() { Width = 460, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _role = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _manualAccount = new() { Width = 200 };
    readonly TextBox _manualRole = new() { Width = 300 };
    readonly TextBox _akid = new() { Width = 300 };
    readonly TextBox _secret = new() { Width = 460, UseSystemPasswordChar = true };
    readonly Button _save = new() { Text = "Save account", AutoSize = true, MinimumSize = new Size(150, 38), Padding = new Padding(10, 2, 10, 2) };
    readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, MinimumSize = new Size(110, 38), Padding = new Padding(10, 2, 10, 2), DialogResult = DialogResult.Cancel };
    readonly Label _message = new() { AutoSize = true, MaximumSize = new Size(W, 0) };
    readonly FlowLayoutPanel _ssoPanel = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    readonly FlowLayoutPanel _keysPanel = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
    readonly FlowLayoutPanel _manualRow;
    string _token = "";
    readonly ComboBox _found = new() { Width = 560, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label _foundStatus = new() { AutoSize = true, MaximumSize = new Size(W, 0), ForeColor = Muted, Margin = new Padding(0, 0, 0, 8) };
    List<SsoCandidate> _cands = new();

    /// <summary>Name of the profile that was saved, when the dialog returns OK.</summary>
    public string? CreatedProfile { get; private set; }

    static Label Hint(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(W, 0), ForeColor = Muted, Margin = new Padding(0, 0, 0, 10) };
    static Label Cap(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 0, 2) };

    public ProfileDialog()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Add an AWS account";
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(680, 700);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;

        var host = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 16, 24, 8) };
        Controls.Add(host);

        host.Controls.Add(new Label { Text = "Add an AWS account", Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
        host.Controls.Add(Hint("Each AWS account you work with gets its own saved login (a \"profile\"). Add as many as you need; you pick which one to use when choosing servers."));

        host.Controls.Add(Cap("How do you sign in to AWS?"));
        host.Controls.Add(_rbSso);
        host.Controls.Add(_rbKeys);

        host.Controls.Add(Cap("Nickname for this account"));
        host.Controls.Add(_name);
        host.Controls.Add(Hint("For example: work-prod or client-a. Letters, numbers and - _ . @ only."));

        // --- company sign-in ---
        _ssoPanel.Controls.Add(Cap("Detected sign-in (from this PC or your company)"));
        _ssoPanel.Controls.Add(_found);
        _ssoPanel.Controls.Add(_foundStatus);
        _found.SelectedIndexChanged += (_, _) => UseCandidate();
        _ssoPanel.Controls.Add(Cap("Sign-in URL (the AWS access portal address)"));
        _ssoPanel.Controls.Add(_startUrl);
        _ssoPanel.Controls.Add(Hint("Looks like https://yourcompany.awsapps.com/start. Your AWS admin or IT team can tell you."));
        _ssoPanel.Controls.Add(Cap("Region of the sign-in page"));
        _ssoRegion.Items.AddRange(AwsRegions.Common.Cast<object>().ToArray());
        _ssoPanel.Controls.Add(_ssoRegion);
        _ssoPanel.Controls.Add(Hint("Not sure? Ask your AWS admin. It is where the sign-in page lives, often us-east-1 or ap-south-1."));
        _signIn.Margin = new Padding(0, 4, 0, 6);
        _ssoPanel.Controls.Add(_signIn);
        _ssoPanel.Controls.Add(_ssoStatus);
        _ssoPanel.Controls.Add(Cap("AWS account"));
        _ssoPanel.Controls.Add(_account);
        _ssoPanel.Controls.Add(Cap("Role"));
        _ssoPanel.Controls.Add(_role);
        _manualRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        _manualRow.Controls.Add(Hint("The list could not be loaded. Type the account ID (12 digits) and role name your AWS admin gave you."));
        _manualRow.Controls.Add(Cap("Account ID"));
        _manualRow.Controls.Add(_manualAccount);
        _manualRow.Controls.Add(Cap("Role name"));
        _manualRow.Controls.Add(_manualRole);
        _ssoPanel.Controls.Add(_manualRow);
        host.Controls.Add(_ssoPanel);

        // --- access keys ---
        _keysPanel.Controls.Add(Cap("Access key ID"));
        _keysPanel.Controls.Add(_akid);
        _keysPanel.Controls.Add(Cap("Secret access key"));
        _keysPanel.Controls.Add(_secret);
        _keysPanel.Controls.Add(Hint("Saved in your own .aws\\credentials file, exactly like the AWS CLI does. SSMConnect never sends it anywhere except to AWS."));
        host.Controls.Add(_keysPanel);

        _message.Margin = new Padding(0, 6, 0, 6);
        host.Controls.Add(_message);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 8, 0, 12) };
        _save.Margin = new Padding(0, 0, 12, 0);
        buttons.Controls.Add(_save);
        buttons.Controls.Add(_cancel);
        host.Controls.Add(buttons);
        CancelButton = _cancel;

        _rbSso.CheckedChanged += (_, _) => { _ssoPanel.Visible = _rbSso.Checked; _keysPanel.Visible = !_rbSso.Checked; };
        _signIn.Click += async (_, _) => await SignInAsync();
        _account.SelectedIndexChanged += async (_, _) => await LoadRolesAsync();
        _save.Click += (_, _) => Save();
        Shown += async (_, _) => await DetectAsync();
    }

    /// <summary>Fills the sign-in URL and region on its own: settings from IT, existing AWS files, earlier sign-ins, then the company domain.</summary>
    async Task DetectAsync()
    {
        try
        {
            _cands = SsoDiscovery.Find();
            if (_cands.Count == 0)
            {
                _foundStatus.Text = "Looking for your company's sign-in address...";
                try { _cands = await SsoDiscovery.GuessFromDomainAsync(); } catch { }
            }
            _found.Items.Clear();
            foreach (var c in _cands) _found.Items.Add(c.StartUrl + (c.Region.Length > 0 ? "   (" + c.Region + ")" : "") + "   - " + c.Source);
            if (_cands.Count == 0)
            {
                _foundStatus.Text = "Nothing was detected on this PC. Type the sign-in URL below (your AWS admin or IT team can tell you). IT teams can pre-fill it for everyone: see the README, \"For IT teams\".";
                return;
            }
            _found.SelectedIndex = 0;
            _foundStatus.Text = _cands[0].Guess
                ? "This address was guessed from your company domain. Check it looks right, then sign in."
                : $"Found {_cands.Count} sign-in address(es). The first one is filled in; pick another from the list if needed.";
        }
        catch { /* detection is a convenience only */ }
    }

    void UseCandidate()
    {
        if (_found.SelectedIndex < 0 || _found.SelectedIndex >= _cands.Count) return;
        var c = _cands[_found.SelectedIndex];
        _startUrl.Text = c.StartUrl;
        var region = c.Region.Length > 0 ? c.Region : "us-east-1";
        _ssoRegion.SelectedItem = AwsRegions.Common.Contains(region) ? region : null;
        if (_ssoRegion.SelectedItem == null && AwsRegions.Common.Count() > 0) { _ssoRegion.Items.Add(region); _ssoRegion.SelectedItem = region; }
        if (_name.Text.Trim().Length == 0)
        {
            try
            {
                var host = new Uri(c.StartUrl).Host.Split('.')[0];
                if (AwsProfiles.IsValidName(host)) _name.Text = host;
            }
            catch { }
        }
    }

    void Say(string text, bool? ok = null, Label? label = null)
    {
        var l = label ?? _message;
        l.ForeColor = ok == null ? SystemColors.ControlText : ok.Value ? Good : Bad;
        l.Text = text;
    }

    bool ValidName()
    {
        if (!AwsProfiles.IsValidName(_name.Text.Trim())) { Say("Enter a nickname using only letters, numbers and - _ . @ (up to 64 characters).", false); return false; }
        return true;
    }

    async Task SignInAsync()
    {
        if (!ValidName()) return;
        var url = CleanUrl(_startUrl.Text);
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out _) || url.Any(char.IsWhiteSpace))
        { Say("Enter the sign-in URL, starting with https://", false, _ssoStatus); return; }
        _startUrl.Text = url;
        if (_ssoRegion.SelectedItem is not string ssoRegion) { Say("Choose the region of the sign-in page.", false, _ssoStatus); return; }
        if (AwsCli.FindAws() == null) { Say("The AWS CLI is not installed yet. Go back to step 1 and install the missing components.", false, _ssoStatus); return; }

        _signIn.Enabled = false;
        _manualRow.Visible = false;
        try
        {
            var name = _name.Text.Trim();
            // AWS rejects the request ("RegisterClient") when the region is not the one the company's sign-in lives in.
            // That is easy to get wrong, so try the chosen region first and then the others until AWS accepts one.
            var candidates = new[] { ssoRegion }.Concat(AwsRegions.Common.Where(r => r != ssoRegion)).ToList();
            CliResult login = new(-1, "", "");
            var found = false;
            // Second pass drops the optional "scopes" setting, in case this AWS setup does not accept it.
            foreach (var withScopes in new[] { true, false })
            {
                foreach (var region in candidates)
                {
                    AwsProfiles.SaveSsoSession(name, url, region, withScopes);
                    Say(region == ssoRegion && withScopes
                        ? "A browser window will open. Sign in there and approve the request (up to 3 minutes)..."
                        : $"Not accepted yet. Trying {region}{(withScopes ? "" : " (simpler settings)")}...", null, _ssoStatus);
                    login = await AwsSso.LoginAsync(name);
                    if (login.Ok || !login.Error.Contains("RegisterClient", StringComparison.OrdinalIgnoreCase)) { found = true; ssoRegion = region; break; }
                }
                if (found) break;
            }
            if (!found)
            {
                Say("AWS did not accept this sign-in URL in any region. Check the URL with your AWS admin (it should look like https://yourcompany.awsapps.com/start) and that IAM Identity Center is enabled for it.", false, _ssoStatus);
                return;
            }
            if (!login.Ok) { Say(AwsCli.FriendlyError(login.Error), false, _ssoStatus); return; }
            _ssoRegion.SelectedItem = ssoRegion;

            _token = AwsSso.ReadAccessToken(name) ?? "";
            if (_token.Length == 0) throw new InvalidOperationException("Signed in, but the sign-in token could not be found.");
            Say("Signed in. Loading your accounts...", null, _ssoStatus);
            var accounts = await AwsSso.ListAccountsAsync(_token, ssoRegion);
            _account.Items.Clear();
            _account.Items.AddRange(accounts.Cast<object>().ToArray());
            if (accounts.Count == 0) throw new InvalidOperationException("You are signed in, but no AWS accounts are assigned to you. Ask your AWS admin.");
            Say($"Signed in. Found {accounts.Count} account(s). Pick one below.", true, _ssoStatus);
            _account.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Say(ex.Message + "\nYou can also type the account ID and role below.", false, _ssoStatus);
            _manualRow.Visible = true;
        }
        finally { _signIn.Enabled = true; }
    }

    /// <summary>Trims spaces, a trailing slash and any #fragment, which AWS does not expect in the start URL.</summary>
    static string CleanUrl(string raw)
    {
        var u = raw.Trim();
        var hash = u.IndexOf('#');
        if (hash >= 0) u = u.Substring(0, hash);
        return u.TrimEnd('/');
    }

    async Task LoadRolesAsync()
    {
        if (_account.SelectedItem is not SsoAccount acct || _ssoRegion.SelectedItem is not string region || _token.Length == 0) return;
        _role.Items.Clear();
        try
        {
            var roles = await AwsSso.ListRolesAsync(_token, region, acct.Id);
            _role.Items.AddRange(roles.Cast<object>().ToArray());
            if (roles.Count > 0) _role.SelectedIndex = 0;
        }
        catch (Exception ex) { Say(ex.Message, false); _manualRow.Visible = true; }
    }

    void Save()
    {
        if (!ValidName()) return;
        var name = _name.Text.Trim();
        if (AwsProfiles.Find(name) != null &&
            MessageBox.Show(this, $"An AWS profile called \"{name}\" already exists on this PC. Replace its settings?", "Account exists", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            if (_rbSso.Checked)
            {
                var url = CleanUrl(_startUrl.Text);
                if (_ssoRegion.SelectedItem is not string ssoRegion || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                { Say("Fill in the sign-in URL and region first, then click \"Sign in and find my accounts\".", false); return; }
                string account, role;
                if (_manualRow.Visible) { account = _manualAccount.Text.Trim(); role = _manualRole.Text.Trim(); }
                else { account = (_account.SelectedItem as SsoAccount)?.Id ?? ""; role = _role.SelectedItem as string ?? ""; }
                if (!System.Text.RegularExpressions.Regex.IsMatch(account, @"^\d{12}$")) { Say("Pick an account (or type the 12-digit account ID).", false); return; }
                if (!System.Text.RegularExpressions.Regex.IsMatch(role, @"^[\w+=,.@\-]{1,64}$")) { Say("Pick a role (or type the role name).", false); return; }
                AwsProfiles.SaveSsoSession(name, url, ssoRegion);
                AwsProfiles.SaveSsoProfile(name, name, account, role);
            }
            else
            {
                AwsProfiles.SaveAccessKeys(name, _akid.Text.Trim(), _secret.Text.Trim());
            }
            CreatedProfile = name;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { Say(ex.Message, false); }
    }
}
