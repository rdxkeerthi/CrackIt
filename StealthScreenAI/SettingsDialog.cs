using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StealthScreenAI
{
    public class SettingsDialog : Form
    {
        private const string AutoModelChoice = "⚡ Auto-Select (Fastest Working Model & Fallback)";
        private readonly TextBox _keyBox;
        private readonly ComboBox _modelCombo;
        private readonly TextBox _repoBox;
        private readonly TextBox _githubTokenBox;
        private readonly TextBox _testCmdBox;
        private readonly Label _statusLabel;
        private readonly Button _testBtn;
        private readonly Button _refreshBtn;
        private readonly Button _saveBtn;

        public SettingsDialog()
        {
            Text = "⚡ StealthScreen API Key & Repo Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            Width = 530;
            Height = 530;
            BackColor = Color.FromArgb(14, 16, 22);
            ForeColor = Color.FromArgb(240, 245, 250);

            var titleLabel = new Label
            {
                Text = "⚡ Gemini AI Configuration",
                Location = new Point(24, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.White
            };
            Controls.Add(titleLabel);

            var subtitleLabel = new Label
            {
                Text = "Enter your Google Gemini API key. Models available for your key are dynamically detected with automatic quota fallback.",
                Location = new Point(24, 40),
                Size = new Size(460, 32),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(160, 165, 175)
            };
            Controls.Add(subtitleLabel);

            var keyLabel = new Label
            {
                Text = "Gemini API Key:",
                Location = new Point(24, 78),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 225, 230)
            };
            Controls.Add(keyLabel);

            _keyBox = new TextBox
            {
                Location = new Point(24, 98),
                Width = 460,
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(10, 12, 16),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Text = ConfigManager.Current.GeminiApiKey,
                PlaceholderText = "AIzaSy..."
            };
            _keyBox.Leave += async (s, e) => await LoadAvailableModelsAsync(forceRefresh: true);
            Controls.Add(_keyBox);

            var modelLabel = new Label
            {
                Text = "Active Model (Auto-fallbacks if quota runs out):",
                Location = new Point(24, 132),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 225, 230)
            };
            Controls.Add(modelLabel);

            _modelCombo = new ComboBox
            {
                Location = new Point(24, 152),
                Width = 360,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(10, 12, 16),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_modelCombo);

            _refreshBtn = new Button
            {
                Text = "🔄 Fetch",
                Location = new Point(390, 151),
                Size = new Size(94, 28),
                BackColor = Color.FromArgb(25, 255, 255, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _refreshBtn.FlatAppearance.BorderSize = 1;
            _refreshBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 255, 255, 255);
            _refreshBtn.Click += async (s, e) => await LoadAvailableModelsAsync(forceRefresh: true);
            Controls.Add(_refreshBtn);

            var repoLabel = new Label
            {
                Text = "Repository (Local Folder OR GitHub URL / owner/repo):",
                Location = new Point(24, 190),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 225, 230)
            };
            Controls.Add(repoLabel);

            _repoBox = new TextBox
            {
                Location = new Point(24, 210),
                Width = 360,
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(10, 12, 16),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Text = ConfigManager.Current.RepoPath,
                PlaceholderText = "C:\\path\\to\\repo OR https://github.com/owner/repo"
            };
            Controls.Add(_repoBox);

            var browseRepoBtn = new Button
            {
                Text = "📁 Browse",
                Location = new Point(390, 209),
                Size = new Size(94, 26),
                BackColor = Color.FromArgb(25, 255, 255, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            browseRepoBtn.FlatAppearance.BorderSize = 1;
            browseRepoBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 255, 255, 255);
            browseRepoBtn.Click += (s, e) =>
            {
                using var fbd = new FolderBrowserDialog
                {
                    Description = "Select Local Repository Root Folder",
                    SelectedPath = System.IO.Directory.Exists(_repoBox.Text) ? _repoBox.Text : Environment.CurrentDirectory
                };
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    _repoBox.Text = fbd.SelectedPath;
                }
            };
            Controls.Add(browseRepoBtn);

            var ghTokenLabel = new Label
            {
                Text = "GitHub Token (optional — only needed for private GitHub/Codespace repos):",
                Location = new Point(24, 244),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 225, 230)
            };
            Controls.Add(ghTokenLabel);

            _githubTokenBox = new TextBox
            {
                Location = new Point(24, 264),
                Width = 460,
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(10, 12, 16),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = true,
                Text = ConfigManager.Current.GitHubToken,
                PlaceholderText = "ghp_... or github_pat_... (leave blank for public repos / screen-only)"
            };
            Controls.Add(_githubTokenBox);

            var testCmdLabel = new Label
            {
                Text = "Custom Test Command (local repos only; blank = auto-detect):",
                Location = new Point(24, 298),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 225, 230)
            };
            Controls.Add(testCmdLabel);

            _testCmdBox = new TextBox
            {
                Location = new Point(24, 318),
                Width = 460,
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(10, 12, 16),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Text = ConfigManager.Current.TestCommand,
                PlaceholderText = "Auto-detect (e.g. dotnet test, pytest, npm test)"
            };
            Controls.Add(_testCmdBox);

            _statusLabel = new Label
            {
                Text = "● Loading available models...",
                Location = new Point(24, 352),
                Size = new Size(460, 36),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(160, 165, 175)
            };
            Controls.Add(_statusLabel);

            _testBtn = new Button
            {
                Text = "⚡ Test Connection",
                Location = new Point(24, 398),
                Size = new Size(180, 36),
                BackColor = Color.FromArgb(25, 255, 255, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _testBtn.FlatAppearance.BorderSize = 1;
            _testBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 255, 255, 255);
            _testBtn.Click += async (s, e) => await TestApiKeyAsync();
            Controls.Add(_testBtn);

            _saveBtn = new Button
            {
                Text = "Save & Apply",
                Location = new Point(214, 398),
                Size = new Size(270, 36),
                BackColor = Color.FromArgb(40, 255, 255, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _saveBtn.FlatAppearance.BorderSize = 1;
            _saveBtn.FlatAppearance.BorderColor = Color.FromArgb(70, 255, 255, 255);
            _saveBtn.Click += (s, e) =>
            {
                ConfigManager.Current.GeminiApiKey = _keyBox.Text.Trim();
                ConfigManager.Current.RepoPath = _repoBox.Text.Trim();
                ConfigManager.Current.GitHubToken = _githubTokenBox.Text.Trim();
                ConfigManager.Current.TestCommand = _testCmdBox.Text.Trim();
                string selected = _modelCombo.SelectedItem?.ToString() ?? "";
                if (selected == AutoModelChoice || string.IsNullOrWhiteSpace(selected))
                {
                    ConfigManager.Current.GeminiModel = "gemini-flash-lite-latest";
                }
                else
                {
                    ConfigManager.Current.GeminiModel = selected;
                }
                ConfigManager.Save();
                Close();
            };
            Controls.Add(_saveBtn);

            Shown += async (s, e) => await LoadAvailableModelsAsync(forceRefresh: false);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeWin32.WS_EX_TOOLWINDOW | NativeWin32.WS_EX_TOPMOST;
                cp.ExStyle &= ~NativeWin32.WS_EX_APPWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeWin32.HideFromTaskbarAndAppList(Handle);
            if (ConfigManager.Current.ExcludeFromCapture)
            {
                NativeWin32.ApplyCaptureExclusion(Handle);
            }
        }

        private async Task LoadAvailableModelsAsync(bool forceRefresh)
        {
            string key = _keyBox.Text.Trim();
            _statusLabel.Text = "Detecting available Gemini models for your API key...";
            _statusLabel.ForeColor = Color.FromArgb(200, 205, 215);
            _refreshBtn.Enabled = false;

            try
            {
                List<string> models = await GeminiStreamClient.FetchAvailableModelsAsync(key, forceRefresh);
                _modelCombo.Items.Clear();
                _modelCombo.Items.Add(AutoModelChoice);

                foreach (var m in models)
                {
                    _modelCombo.Items.Add(m);
                }

                string current = ConfigManager.Current.GeminiModel;
                int idx = _modelCombo.FindStringExact(current);
                if (idx >= 0)
                {
                    _modelCombo.SelectedIndex = idx;
                }
                else
                {
                    _modelCombo.SelectedIndex = _modelCombo.Items.Count > 1 ? 1 : 0;
                }

                _statusLabel.Text = $"✓ Detected {models.Count} models for this key. Auto-fallback active.";
                _statusLabel.ForeColor = Color.FromArgb(160, 220, 160);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"⚠️ Error loading models: {ex.Message}";
                _statusLabel.ForeColor = Color.FromArgb(240, 180, 180);
            }
            finally
            {
                _refreshBtn.Enabled = true;
            }
        }

        private async Task TestApiKeyAsync()
        {
            string key = _keyBox.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                _statusLabel.Text = "⚠️ Please paste an API key first.";
                _statusLabel.ForeColor = Color.FromArgb(240, 180, 180);
                return;
            }

            _statusLabel.Text = "Testing model connection & quota status...";
            _statusLabel.ForeColor = Color.FromArgb(200, 205, 215);
            _testBtn.Enabled = false;

            try
            {
                List<string> models = await GeminiStreamClient.FetchAvailableModelsAsync(key, forceRefresh: false);
                string selected = _modelCombo.SelectedItem?.ToString() ?? "";

                var testQueue = new List<string>();
                if (selected != AutoModelChoice && !string.IsNullOrWhiteSpace(selected))
                {
                    testQueue.Add(selected);
                }
                foreach (var m in models)
                {
                    if (!testQueue.Contains(m, StringComparer.OrdinalIgnoreCase))
                    {
                        testQueue.Add(m);
                    }
                }

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string body = "{\"contents\":[{\"parts\":[{\"text\":\"hi\"}]}]}";
                string? workingModel = null;
                string? quotaExceededModel = null;

                foreach (var model in testQueue)
                {
                    string cleanModel = model.StartsWith("models/") ? model.Substring(7) : model;
                    string url = $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent?key={key}";
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");

                    try
                    {
                        var resp = await client.PostAsync(url, content);
                        if (resp.IsSuccessStatusCode)
                        {
                            workingModel = cleanModel;
                            break;
                        }
                        else if ((int)resp.StatusCode == 429)
                        {
                            if (quotaExceededModel == null) quotaExceededModel = cleanModel;
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (workingModel != null)
                {
                    int matchIdx = _modelCombo.FindStringExact(workingModel);
                    if (matchIdx >= 0) _modelCombo.SelectedIndex = matchIdx;

                    if (quotaExceededModel != null && quotaExceededModel != workingModel)
                    {
                        _statusLabel.Text = $"⚠️ {quotaExceededModel} reached quota (429). Found working model: {workingModel}! Auto-switched.";
                        _statusLabel.ForeColor = Color.FromArgb(255, 215, 120);
                    }
                    else
                    {
                        _statusLabel.Text = $"✓ Connection Successful! ({workingModel} is active & responsive)";
                        _statusLabel.ForeColor = Color.FromArgb(160, 235, 160);
                    }
                }
                else
                {
                    _statusLabel.Text = "❌ All candidate models failed or exceeded quota. Check your API key.";
                    _statusLabel.ForeColor = Color.FromArgb(240, 180, 180);
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"❌ Network error: {ex.Message}";
                _statusLabel.ForeColor = Color.FromArgb(240, 180, 180);
            }
            finally
            {
                _testBtn.Enabled = true;
            }
        }
    }
}
