using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StealthScreenAI
{
    /// <summary>
    /// Monochrome Transparent Glass HUD with:
    /// - Persistent multi-turn chat display & inline chat input box
    /// - Dedicated modes: [📸 Solve], [💻 Code], [🔘 MCQ], [🎙️ Viva], [🐞 Fix], [✂️ Snip], [💬 Chat], [🔑 Key]
    /// - Repository test runner & automated bug-fix location diagnostics
    /// - Focus Immunity (WS_EX_NOACTIVATE) on all action controls; temporary typing focus only inside chatbox
    /// - Screen Capture Exclusion (WDA_EXCLUDEFROMCAPTURE)
    /// </summary>
    public class StealthOverlayWindow : Form
    {
        private const int BarWidth = 1000;
        private const int BarRestingHeight = 46;
        private const int BarExpandedHeight = 560;

        // Pure monochrome transparent glass design system (Zero color saturation)
        private static readonly Color SmokedGlassBg = Color.FromArgb(12, 14, 18);
        private static readonly Color GlassHeaderBg = Color.FromArgb(16, 18, 24);
        private static readonly Color GlassDropdownBg = Color.FromArgb(10, 12, 16);
        private static readonly Color GlassBorderColor = Color.FromArgb(60, 65, 75);
        private static readonly Color TextPrimary = Color.FromArgb(245, 245, 250);
        private static readonly Color TextSecondary = Color.FromArgb(160, 165, 175);
        private static readonly Color TextUserQuery = Color.FromArgb(190, 220, 255);
        private static readonly Color ButtonGlassBg = Color.FromArgb(24, 27, 34);
        private static readonly Color ButtonGlassHover = Color.FromArgb(38, 42, 52);

        private bool _isGhostMode = false;
        private bool _isExpanded = false;
        private bool _isDragging = false;
        private bool _hasConversationOutput = false;
        private Point _dragStart;

        private readonly GeminiStreamClient _aiClient = new();
        private CancellationTokenSource? _solveCts;
        private CancellationTokenSource? _audioCts;

        // Top Bar Controls
        private readonly Panel _topBarPanel;
        private readonly Label _brandLabel;
        private readonly Label _sebBadge;

        private readonly Button _solveScreenBtn;
        private readonly Button _codeSolverBtn;
        private readonly Button _mcqSolverBtn;
        private readonly Button _vivaAudioBtn;
        private readonly Button _fixBugBtn;
        private readonly Button _snipBtn;
        private readonly Button _chatFocusBtn;
        private readonly Button _keyBtn;
        private readonly Button _quickCopyBtn;
        private readonly Button _quickReplaceBtn;
        private readonly Button _ghostBtn;
        private readonly Button _expandBtn;
        private readonly Button _closeBtn;

        // Dropdown Panel Controls
        private readonly Panel _dropdownPanel;
        private readonly Panel _dropdownToolbar;
        private readonly Label _dropdownStatusLabel;
        private readonly Button _repoBtn;
        private readonly Button _runTestsBtn;
        private readonly Button _replaceBtn;
        private readonly Button _pasteBtn;
        private readonly Button _copyBtn;
        private readonly Button _clearBtn;
        private readonly TrackBar _opacitySlider;
        private readonly Label _repoFooterLabel;
        private readonly PointerRichTextBox _contentBox;

        // Chat Launch Bar (delegates typing to capture-excluded CommandBarWindow so HUD remains strictly WS_EX_NOACTIVATE)
        private readonly Panel _chatInputPanel;
        private readonly Button _chatPromptLaunchBtn;
        private readonly CheckBox _attachScreenCheck;
        private int _cloakRefreshTick = 0;

        private IntPtr _lastActiveAppHwnd = IntPtr.Zero;
        private readonly System.Windows.Forms.Timer _heartbeatTimer;

        public event Action? RequestSnip;
        public event Action<bool>? RequestChatInput;
        public bool AttachScreenChecked => _attachScreenCheck.Checked;

        protected override bool ShowWithoutActivation => true;

        public StealthOverlayWindow()
        {
            Text = "";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Cursor = Cursors.Arrow;

            Width = BarWidth;
            Height = BarRestingHeight;
            BackColor = SmokedGlassBg;
            Opacity = ConfigManager.Current.Opacity;

            Rectangle screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Location = new Point(Math.Max(0, (screen.Width - BarWidth) / 2), 8);

            // 1. Top Bar Panel
            _topBarPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = BarRestingHeight,
                BackColor = GlassHeaderBg
            };
            _topBarPanel.MouseDown += OnBarMouseDown;
            _topBarPanel.MouseMove += OnBarMouseMove;
            _topBarPanel.MouseUp += OnBarMouseUp;
            _topBarPanel.Paint += (s, e) => DrawGlassBorder(e.Graphics, _topBarPanel.ClientRectangle, 6);

            _brandLabel = new Label
            {
                Text = "⚡ STEALTH",
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(10, 13),
                AutoSize = true
            };
            _brandLabel.MouseDown += OnBarMouseDown;
            _brandLabel.MouseMove += OnBarMouseMove;
            _brandLabel.MouseUp += OnBarMouseUp;
            _topBarPanel.Controls.Add(_brandLabel);

            _sebBadge = new Label
            {
                Text = "SEB SAFE",
                ForeColor = TextSecondary,
                BackColor = Color.FromArgb(24, 28, 36),
                Font = new Font("Segoe UI", 7f, FontStyle.Bold),
                Location = new Point(82, 14),
                Padding = new Padding(3, 1, 3, 1),
                AutoSize = true
            };
            _topBarPanel.Controls.Add(_sebBadge);

            // Dedicated Action Keys
            int leftX = 144;
            _solveScreenBtn = CreateMonochromeGlassBtn("📸 Solve", leftX, 7, 68, (s, e) => SolveFullScreen(SolveMode.General));
            leftX += 72;

            _codeSolverBtn = CreateMonochromeGlassBtn("💻 Code", leftX, 7, 68, (s, e) => SolveFullScreen(SolveMode.Coding));
            leftX += 72;

            _mcqSolverBtn = CreateMonochromeGlassBtn("🔘 MCQ", leftX, 7, 64, (s, e) => SolveFullScreen(SolveMode.MCQ));
            leftX += 68;

            _vivaAudioBtn = CreateMonochromeGlassBtn("🎙️ Viva", leftX, 7, 66, (s, e) => ToggleVivaAudioCapture());
            leftX += 70;

            _fixBugBtn = CreateMonochromeGlassBtn("🐞 Fix", leftX, 7, 60, (s, e) => FixRepoBugFromScreen());
            leftX += 64;

            _snipBtn = CreateMonochromeGlassBtn("✂️ Snip", leftX, 7, 60, (s, e) => RequestSnip?.Invoke());
            leftX += 64;

            _chatFocusBtn = CreateMonochromeGlassBtn("💬 Chat", leftX, 7, 66, (s, e) => FocusChatBox());
            leftX += 70;

            _keyBtn = CreateMonochromeGlassBtn("🔑 Key", leftX, 7, 58, (s, e) => OpenSettings());
            leftX += 62;

            _quickCopyBtn = CreateMonochromeGlassBtn("📋 Copy", leftX, 7, 64, (s, e) => CopyCode());
            leftX += 68;

            _quickReplaceBtn = CreateMonochromeGlassBtn("🔄 Replace", leftX, 7, 78, (s, e) => ReplaceCodeInCompiler());

            _topBarPanel.Controls.Add(_solveScreenBtn);
            _topBarPanel.Controls.Add(_codeSolverBtn);
            _topBarPanel.Controls.Add(_mcqSolverBtn);
            _topBarPanel.Controls.Add(_vivaAudioBtn);
            _topBarPanel.Controls.Add(_fixBugBtn);
            _topBarPanel.Controls.Add(_snipBtn);
            _topBarPanel.Controls.Add(_chatFocusBtn);
            _topBarPanel.Controls.Add(_keyBtn);
            _topBarPanel.Controls.Add(_quickCopyBtn);
            _topBarPanel.Controls.Add(_quickReplaceBtn);

            // Utility buttons on far right
            int rightX = BarWidth - 32;
            _closeBtn = CreateIconButton("✕", rightX, 9, (s, e) => Hide());
            rightX -= 28;

            _expandBtn = CreateIconButton("▼", rightX, 9, (s, e) => ToggleExpand());
            rightX -= 28;

            _ghostBtn = CreateIconButton("👻", rightX, 9, (s, e) => ToggleGhostMode());

            _topBarPanel.Controls.Add(_closeBtn);
            _topBarPanel.Controls.Add(_expandBtn);
            _topBarPanel.Controls.Add(_ghostBtn);

            // 2. Expandable Dropdown Panel
            _dropdownPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = GlassDropdownBg,
                Visible = false
            };

            _dropdownToolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(14, 16, 22)
            };

            _dropdownStatusLabel = new Label
            {
                Text = "● Ready (Memory Active)",
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                Location = new Point(10, 9),
                Size = new Size(420, 18),
                AutoEllipsis = true
            };
            _dropdownToolbar.Controls.Add(_dropdownStatusLabel);

            int toolRight = BarWidth - 10;
            _clearBtn = CreateMonochromeGlassBtn("🧹 New Chat", toolRight - 96, 3, 96, (s, e) => ClearContent());
            toolRight -= 100;

            _copyBtn = CreateMonochromeGlassBtn("📋 Copy", toolRight - 78, 3, 78, (s, e) => CopyCode());
            toolRight -= 82;

            _pasteBtn = CreateMonochromeGlassBtn("⚡ Paste", toolRight - 74, 3, 74, (s, e) => AutoInjectCode());
            toolRight -= 78;

            _replaceBtn = CreateMonochromeGlassBtn("🔄 Replace", toolRight - 96, 3, 96, (s, e) => ReplaceCodeInCompiler());
            toolRight -= 100;

            _runTestsBtn = CreateMonochromeGlassBtn("🧪 Test", toolRight - 76, 3, 76, async (s, e) => await RunRepoTestsAndDiagnoseAsync());
            toolRight -= 80;

            _repoBtn = CreateMonochromeGlassBtn("📁 Repo", toolRight - 80, 3, 80, (s, e) => SelectRepoFolder());

            _dropdownToolbar.Controls.Add(_clearBtn);
            _dropdownToolbar.Controls.Add(_copyBtn);
            _dropdownToolbar.Controls.Add(_pasteBtn);
            _dropdownToolbar.Controls.Add(_replaceBtn);
            _dropdownToolbar.Controls.Add(_runTestsBtn);
            _dropdownToolbar.Controls.Add(_repoBtn);

            // Chat / Response Transcript (PointerRichTextBox forces arrow cursor over text)
            _contentBox = new PointerRichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(12, 14, 18),
                ForeColor = Color.FromArgb(235, 240, 245),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10f),
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Padding = new Padding(12),
                Cursor = Cursors.Arrow
            };
            ShowWelcomeBanner();

            // Capture-Safe Chat Launch Bar (keeps StealthOverlayWindow 100% WS_EX_NOACTIVATE)
            _chatInputPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                BackColor = Color.FromArgb(16, 19, 25),
                Padding = new Padding(10, 6, 10, 6)
            };
            _chatInputPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(GlassBorderColor, 1f);
                e.Graphics.DrawLine(pen, 0, 0, _chatInputPanel.Width, 0);
            };

            _chatPromptLaunchBtn = CreateMonochromeGlassBtn(
                "💬 Click here or press F10 to ask a follow-up, paste a test error, or run a command...",
                10, 7, BarWidth - 120,
                (s, e) => FocusChatBox());
            _chatPromptLaunchBtn.Height = 32;
            _chatPromptLaunchBtn.TextAlign = ContentAlignment.MiddleLeft;
            _chatPromptLaunchBtn.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            _chatPromptLaunchBtn.ForeColor = TextSecondary;
            _chatInputPanel.Controls.Add(_chatPromptLaunchBtn);

            _attachScreenCheck = new CheckBox
            {
                Text = "📷 Screen",
                Location = new Point(BarWidth - 102, 11),
                Size = new Size(88, 24),
                ForeColor = TextSecondary,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Cursor = Cursors.Arrow,
                Checked = false
            };
            _chatInputPanel.Controls.Add(_attachScreenCheck);

            // Dropdown Footer
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = Color.FromArgb(14, 16, 22)
            };
            var opLbl = new Label
            {
                Text = "Opacity:",
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 7.5f),
                Location = new Point(10, 6),
                AutoSize = true
            };
            footer.Controls.Add(opLbl);

            _opacitySlider = new TrackBar
            {
                Minimum = 15,
                Maximum = 100,
                Value = Math.Clamp((int)(ConfigManager.Current.Opacity * 100), 15, 100),
                TickStyle = TickStyle.None,
                Location = new Point(58, 3),
                Width = 180,
                Height = 20
            };
            _opacitySlider.ValueChanged += (s, e) =>
            {
                double op = _opacitySlider.Value / 100.0;
                Opacity = op;
                ConfigManager.Current.Opacity = op;
            };
            _opacitySlider.MouseUp += (s, e) => ConfigManager.Save();
            footer.Controls.Add(_opacitySlider);

            _repoFooterLabel = new Label
            {
                ForeColor = Color.FromArgb(145, 155, 170),
                Font = new Font("Segoe UI", 7.5f),
                Location = new Point(252, 6),
                Size = new Size(460, 16),
                AutoEllipsis = true
            };
            UpdateRepoFooterLabel();
            footer.Controls.Add(_repoFooterLabel);

            var hintLbl = new Label
            {
                Text = "Shake mouse or Ctrl×2 to Hide/Show",
                ForeColor = Color.FromArgb(120, 125, 135),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Italic),
                Location = new Point(BarWidth - 220, 6),
                AutoSize = true
            };
            footer.Controls.Add(hintLbl);

            // Add controls in layout-safe order: Fill first (with BringToFront), Top/Bottom panels around it
            _dropdownPanel.Controls.Add(_contentBox);
            _dropdownPanel.Controls.Add(_dropdownToolbar);
            _dropdownPanel.Controls.Add(_chatInputPanel);
            _dropdownPanel.Controls.Add(footer);
            _contentBox.BringToFront();

            Controls.Add(_dropdownPanel);
            Controls.Add(_topBarPanel);

            // Watchdog heartbeat: Keep topmost without taking focus (SWP_NOACTIVATE) and refresh capture exclusion every 5s
            _heartbeatTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _heartbeatTimer.Tick += (s, e) =>
            {
                if (Visible && !_isGhostMode)
                {
                    NativeWin32.SetWindowPos(Handle, NativeWin32.HWND_TOPMOST, 0, 0, 0, 0,
                        NativeWin32.SWP_NOMOVE | NativeWin32.SWP_NOSIZE | NativeWin32.SWP_NOACTIVATE);
                }

                _cloakRefreshTick++;
                if (_cloakRefreshTick >= 5)
                {
                    _cloakRefreshTick = 0;
                    if (ConfigManager.Current.ExcludeFromCapture && IsHandleCreated)
                    {
                        NativeWin32.ApplyCaptureExclusion(Handle);
                    }
                }
            };
            _heartbeatTimer.Start();
        }

        private void ShowWelcomeBanner()
        {
            _hasConversationOutput = false;
            _contentBox.Clear();
            _contentBox.Text = "⚡ StealthScreen AI Monochrome Glass HUD (Persistent Memory + Repo Debugger)\n" +
                "──────────────────────────────────────────────────────────────────────────\n\n" +
                "🎯 DEDICATED MODES & CHAT:\n" +
                "  • [📸 Solve] / [💻 Code] / [🔘 MCQ] : Screen capture & streaming solution\n" +
                "  • [🎙️ Viva]                         : System audio oral defense copilot\n" +
                "  • [💬 Chat] or F10 / bar below      : Multi-turn follow-up chat with memory\n" +
                "  • [📁 Repo] + [🧪 Test] / [🐞 Fix]  : Local or browser/Codespaces bug diagnosis\n\n" +
                "🛡️ STEALTH & PERSISTENCE (SKILL.md Compliant):\n" +
                "  • Strict WS_EX_NOACTIVATE + MA_NOACTIVATE: HUD never steals focus from SEB/IDE\n" +
                "  • WDA_EXCLUDEFROMCAPTURE applied to HUD, child controls, and popup dialogs\n" +
                "  • Responses stay on screen until you start a new request or click [🧹 New Chat]";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeWin32.WS_EX_NOACTIVATE | NativeWin32.WS_EX_TOOLWINDOW | NativeWin32.WS_EX_TOPMOST;
                cp.ExStyle &= ~NativeWin32.WS_EX_APPWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeWin32.HideFromTaskbarAndAppList(Handle);
            ApplyGlassEffect();
            if (ConfigManager.Current.ExcludeFromCapture)
            {
                NativeWin32.ApplyCaptureExclusion(Handle);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeWin32.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)NativeWin32.MA_NOACTIVATE;
                return;
            }
            if (m.Msg == NativeWin32.WM_SETCURSOR)
            {
                Cursor.Current = Cursors.Arrow;
                m.Result = (IntPtr)1;
                return;
            }
            if (m.Msg == NativeWin32.WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (m.Result == (IntPtr)NativeWin32.HTCLIENT)
                {
                    Point local = PointToClient(Cursor.Position);
                    if (local.Y >= 0 && local.Y <= BarRestingHeight &&
                        _topBarPanel.GetChildAtPoint(local) == null)
                    {
                        m.Result = (IntPtr)NativeWin32.HTCAPTION;
                    }
                }
                return;
            }
            base.WndProc(ref m);
        }

        public void FocusChatBox()
        {
            Show();
            Expand();
            RequestChatInput?.Invoke(_attachScreenCheck.Checked);
        }

        public void SubmitExternalQuery(string query, bool captureScreen = false)
        {
            _ = HandleUserQueryAsync(query, captureScreen: captureScreen);
        }

        private async Task HandleUserQueryAsync(string query, bool captureScreen)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            string lower = query.Trim().ToLowerInvariant();

            // Quick shortcuts
            if (lower == "code" || lower == "solve code" || lower == "coding" || lower == "coding answer")
            {
                await Task.Delay(60);
                SolveFullScreen(SolveMode.Coding);
                return;
            }
            if (lower == "mcq" || lower == "solve mcq" || lower == "mcq answer")
            {
                await Task.Delay(60);
                SolveFullScreen(SolveMode.MCQ);
                return;
            }
            if (lower == "solve" || lower == "solve screen" || lower == "scan screen")
            {
                await Task.Delay(60);
                SolveFullScreen(SolveMode.General);
                return;
            }
            if (lower == "fix" || lower == "fix bug" || lower == "debug")
            {
                await Task.Delay(60);
                FixRepoBugFromScreen();
                return;
            }
            if (lower == "viva" || lower == "listen viva" || lower == "audio" || lower == "listen" || lower == "audio solve")
            {
                ToggleVivaAudioCapture();
                return;
            }
            if (lower == "snip" || lower == "snippet" || lower == "crop")
            {
                RequestSnip?.Invoke();
                return;
            }
            if (lower == "copy" || lower == "copy code")
            {
                CopyCode();
                return;
            }
            if (lower == "replace" || lower == "replace code" || lower == "replace compiler")
            {
                ReplaceCodeInCompiler();
                return;
            }
            if (lower == "paste" || lower == "paste code")
            {
                AutoInjectCode();
                return;
            }
            if (lower == "clear" || lower == "new chat")
            {
                ClearContent();
                return;
            }
            if (lower == "hide" || lower == "hide window")
            {
                Hide();
                return;
            }
            if (lower == "show" || lower == "show window")
            {
                Show();
                return;
            }
            if (lower == "ghost mode" || lower == "ghost")
            {
                ToggleGhostMode();
                return;
            }
            if (lower == "key" || lower == "settings" || lower == "api key")
            {
                OpenSettings();
                return;
            }

            // Repository link shortcut in chat: "repo https://github.com/owner/repo" or "repo owner/repo" or "repo clear"
            if (lower == "repo clear" || lower == "repo none")
            {
                ConfigManager.Current.RepoPath = "";
                ConfigManager.Save();
                UpdateRepoFooterLabel();
                DisplayCommandResult(query, "Cleared linked repository. Using active browser/screen context.");
                return;
            }
            if (lower.StartsWith("repo "))
            {
                string targetRepo = query.Substring(5).Trim();
                if (Directory.Exists(targetRepo) ||
                    RepoContextService.TryParseGitHubRepo(targetRepo, out _) ||
                    RepoContextService.IsBrowserWorkspaceUrl(targetRepo))
                {
                    ConfigManager.Current.RepoPath = targetRepo;
                    ConfigManager.Save();
                    UpdateRepoFooterLabel();
                    DisplayCommandResult(query, $"Linked repository/workspace: {targetRepo}\n(Use [🐞 Fix] or [📷 Screen] on your Codespace/browser IDE to diagnose errors and get exact file & line fixes.)");
                }
                else
                {
                    DisplayCommandResult(query, $"Could not parse '{targetRepo}'. Paste a GitHub URL (`https://github.com/owner/repo`), Codespace URL (`*.github.dev`), or local folder path.");
                }
                return;
            }

            // Explicit test runner command from chat
            if (lower == "test" || lower == "tests" || lower == "run test" || lower == "run tests")
            {
                await RunRepoTestsAndDiagnoseAsync();
                return;
            }
            if (lower.StartsWith("dotnet test") || lower.StartsWith("pytest") ||
                lower.StartsWith("npm test") || lower.StartsWith("cargo test") || lower.StartsWith("go test"))
            {
                await RunRepoTestsAndDiagnoseAsync(query);
                return;
            }

            // Explicit shell execution
            if (IsWindowsShellCommand(query))
            {
                string shellOutput = NativeAutomation.ExecuteCmdCommand(query);
                DisplayCommandResult(query, shellOutput);
                return;
            }

            // Capture optional screenshot before expanding
            string? imageBase64 = null;
            if (captureScreen)
            {
                double prevOp = Opacity;
                Opacity = 0.0;
                Application.DoEvents();
                await Task.Delay(40);
                imageBase64 = ScreenCaptureService.CaptureFullScreenBase64();
                Opacity = prevOp;
            }

            Show();
            Expand();
            BeginConversationTurn($"🧑 You: {query}");

            bool isBug = RepoContextService.LooksLikeErrorOrBugQuery(query);
            string repoPath = RepoContextService.ResolveActiveRepoPath();
            string repoCtx = (isBug && !string.IsNullOrEmpty(repoPath))
                ? await RepoContextService.BuildRepoDebugContextAsync(repoPath, query)
                : "";

            if (!string.IsNullOrEmpty(repoCtx))
            {
                string repoName = RepoContextService.TryParseGitHubRepo(repoPath, out var gh) && gh != null
                    ? $"{gh.Owner}/{gh.Repo}"
                    : Path.GetFileName(repoPath);
                AppendTextFormatted($"📁 [Grounded in repo: {repoName}]\n", TextSecondary);
            }

            _dropdownStatusLabel.Text = $"Thinking ({ConfigManager.Current.GeminiModel})...";
            StartChatStream(query, imageBase64, repoCtx, isBug);
        }

        private static bool IsWindowsShellCommand(string input)
        {
            string s = input.Trim();
            if (s.StartsWith("cmd ", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("exec ", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("run ", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("powershell ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string firstWord = s.Split(' ')[0].ToLowerInvariant();
            string[] shellCommands = {
                "ipconfig", "ping", "dir", "tasklist", "taskkill", "netstat", "whoami",
                "systeminfo", "hostname", "ver", "echo", "type", "mkdir", "rmdir", "del",
                "copy", "move", "start", "calc", "notepad", "curl", "nslookup", "tracert",
                "route", "attrib", "cls", "help", "tree", "assoc", "fc", "find", "findstr"
            };
            return Array.IndexOf(shellCommands, firstWord) >= 0;
        }

        private void ApplyGlassEffect()
        {
            try
            {
                var margins = new NativeWin32.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                NativeWin32.DwmExtendFrameIntoClientArea(Handle, ref margins);

                int accentColor = unchecked((int)0xAA080A0E);
                var accent = new NativeWin32.AccentPolicy
                {
                    AccentState = NativeWin32.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2,
                    GradientColor = accentColor,
                    AnimationId = 0
                };

                int accentSize = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new NativeWin32.WindowCompositionAttributeData
                {
                    Attribute = NativeWin32.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentSize
                };

                NativeWin32.SetWindowCompositionAttribute(Handle, ref data);
                Marshal.FreeHGlobal(accentPtr);
            }
            catch { }
        }

        public void ToggleExpand()
        {
            _isExpanded = !_isExpanded;
            if (_isExpanded)
            {
                Height = BarExpandedHeight;
                _dropdownPanel.Visible = true;
                _expandBtn.Text = "▲";
            }
            else
            {
                _dropdownPanel.Visible = false;
                Height = BarRestingHeight;
                _expandBtn.Text = "▼";
            }
        }

        public void Expand()
        {
            if (!_isExpanded) ToggleExpand();
        }

        public void Collapse()
        {
            if (_isExpanded) ToggleExpand();
        }

        private void BeginConversationTurn(string headerText)
        {
            if (!_hasConversationOutput)
            {
                _contentBox.Clear();
                _hasConversationOutput = true;
            }
            else
            {
                AppendTextFormatted("\n\n────────────────────────────────────────────────────────\n", GlassBorderColor);
            }

            AppendTextFormatted($"{headerText}\n\n", TextUserQuery);
        }

        /// <summary>
        /// Solves full screen with specialized mode: General, Coding (auto language detect), MCQ, or RepoFix.
        /// Preserves previous conversation turns on screen and in Gemini memory.
        /// </summary>
        public async void SolveFullScreen(SolveMode mode)
        {
            IntPtr fg = NativeWin32.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != Handle)
            {
                _lastActiveAppHwnd = fg;
            }

            string modeLabel = mode switch
            {
                SolveMode.Coding => "💻 [CODE SOLVER] Detecting programming language and solving challenge...",
                SolveMode.MCQ => "🔘 [MCQ SOLVER] Scanning question and multiple choice options...",
                SolveMode.RepoFix => "🐞 [REPO BUG FIXER] Scanning screen error and repository context...",
                _ => "📸 [SCREEN SOLVE] Capturing screen and streaming solution..."
            };

            double prevOp = Opacity;
            Opacity = 0.0;
            Application.DoEvents();
            await Task.Delay(40);

            string base64 = ScreenCaptureService.CaptureFullScreenBase64();
            Opacity = prevOp;

            Show();
            Expand();
            BeginConversationTurn(modeLabel);

            string? repoCtx = null;
            if (mode == SolveMode.RepoFix)
            {
                string repoPath = RepoContextService.ResolveActiveRepoPath();
                if (!string.IsNullOrEmpty(repoPath))
                {
                    _dropdownStatusLabel.Text = "Loading repository context...";
                    repoCtx = await RepoContextService.BuildRepoDebugContextAsync(repoPath, "");
                    string repoName = RepoContextService.TryParseGitHubRepo(repoPath, out var gh) && gh != null
                        ? $"{gh.Owner}/{gh.Repo}"
                        : Path.GetFileName(repoPath);
                    AppendTextFormatted($"📁 [Repo: {repoName}]\n\n", TextSecondary);
                }
                else
                {
                    AppendTextFormatted("🌐 [Browser/IDE Screen Mode] Inspecting visible terminal errors, tabs & editor...\n\n", TextSecondary);
                }
            }

            _dropdownStatusLabel.Text = $"Connecting to {ConfigManager.Current.GeminiModel}...";
            StartAiStream(base64, mode, repoCtx);
        }

        public void FixRepoBugFromScreen()
        {
            SolveFullScreen(SolveMode.RepoFix);
        }

        public void SolveRegion(Rectangle rect)
        {
            IntPtr fg = NativeWin32.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != Handle)
            {
                _lastActiveAppHwnd = fg;
            }

            Show();
            Expand();
            BeginConversationTurn("✂️ [REGION SNIP] Capturing region and streaming solution...");

            string base64 = ScreenCaptureService.CaptureRegionBase64(rect.X, rect.Y, rect.Width, rect.Height);
            StartAiStream(base64, SolveMode.General, null);
        }

        private void StartAiStream(string imageBase64, SolveMode mode, string? repoContext)
        {
            _solveCts?.Cancel();
            _solveCts = new CancellationTokenSource();
            SetButtonsEnabled(false);

            Task.Run(async () =>
            {
                try
                {
                    await _aiClient.StreamSolveScreenAsync(
                        imageBase64,
                        mode,
                        chunk =>
                        {
                            if (IsHandleCreated)
                            {
                                BeginInvoke(() => AppendTextFormatted(chunk, TextPrimary));
                            }
                        },
                        status =>
                        {
                            if (IsHandleCreated)
                            {
                                BeginInvoke(() =>
                                {
                                    _dropdownStatusLabel.Text = status;
                                    AppendTextFormatted($"\nℹ️ [{status}]\n\n", TextSecondary);
                                });
                            }
                        },
                        repoContext,
                        _solveCts.Token
                    );
                    if (IsHandleCreated)
                    {
                        BeginInvoke(() => _dropdownStatusLabel.Text = $"✓ Ready ({ConfigManager.Current.GeminiModel} | Memory: {_aiClient.HistoryTurnCount / 2} turns)");
                    }
                }
                finally
                {
                    if (IsHandleCreated)
                    {
                        BeginInvoke(() => SetButtonsEnabled(true));
                    }
                }
            });
        }

        private void StartChatStream(string userMessage, string? imageBase64, string? repoContext, bool isBugFixMode)
        {
            _solveCts?.Cancel();
            _solveCts = new CancellationTokenSource();
            SetButtonsEnabled(false);

            Task.Run(async () =>
            {
                try
                {
                    await _aiClient.StreamChatAsync(
                        userMessage,
                        imageBase64,
                        repoContext,
                        isBugFixMode,
                        chunk =>
                        {
                            if (IsHandleCreated)
                            {
                                BeginInvoke(() => AppendTextFormatted(chunk, TextPrimary));
                            }
                        },
                        status =>
                        {
                            if (IsHandleCreated)
                            {
                                BeginInvoke(() =>
                                {
                                    _dropdownStatusLabel.Text = status;
                                    AppendTextFormatted($"\nℹ️ [{status}]\n\n", TextSecondary);
                                });
                            }
                        },
                        _solveCts.Token
                    );
                    if (IsHandleCreated)
                    {
                        BeginInvoke(() => _dropdownStatusLabel.Text = $"✓ Ready ({ConfigManager.Current.GeminiModel} | Memory: {_aiClient.HistoryTurnCount / 2} turns)");
                    }
                }
                finally
                {
                    if (IsHandleCreated)
                    {
                        BeginInvoke(() => SetButtonsEnabled(true));
                    }
                }
            });
        }

        public void SelectRepoFolder()
        {
            using var dlg = new Form
            {
                Text = "🔗 Link Repository (GitHub / Codespaces / Local)",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true,
                ShowInTaskbar = false,
                Width = 500,
                Height = 250,
                BackColor = SmokedGlassBg,
                ForeColor = TextPrimary
            };
            dlg.HandleCreated += (s, e) =>
            {
                NativeWin32.HideFromTaskbarAndAppList(dlg.Handle);
                if (ConfigManager.Current.ExcludeFromCapture)
                {
                    NativeWin32.ApplyCaptureExclusion(dlg.Handle);
                }
            };

                var urlLbl = new Label
                {
                    Text = "GitHub URL / owner/repo (or local folder path):",
                    Location = new Point(18, 16),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    ForeColor = TextPrimary
                };
                dlg.Controls.Add(urlLbl);

                var repoInput = new TextBox
                {
                    Location = new Point(18, 38),
                    Width = 446,
                    Font = new Font("Segoe UI", 9.5f),
                    BackColor = GlassDropdownBg,
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    Text = ConfigManager.Current.RepoPath,
                    PlaceholderText = "https://github.com/owner/repo or *.github.dev (blank = Browser Mode)"
                };
                dlg.Controls.Add(repoInput);

                var tokenLbl = new Label
                {
                    Text = "GitHub Token (optional — private repos only):",
                    Location = new Point(18, 74),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    ForeColor = TextSecondary
                };
                dlg.Controls.Add(tokenLbl);

                var tokenInput = new TextBox
                {
                    Location = new Point(18, 96),
                    Width = 446,
                    Font = new Font("Segoe UI", 9f),
                    BackColor = GlassDropdownBg,
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    UseSystemPasswordChar = true,
                    Text = ConfigManager.Current.GitHubToken,
                    PlaceholderText = "ghp_... or github_pat_..."
                };
                dlg.Controls.Add(tokenInput);

                var saveBtn = CreateMonochromeGlassBtn("💾 Save Link", 18, 142, 130, (s, e) =>
                {
                    string val = repoInput.Text.Trim();
                    if (!string.IsNullOrEmpty(val) &&
                        !Directory.Exists(val) &&
                        !RepoContextService.TryParseGitHubRepo(val, out _) &&
                        !RepoContextService.IsBrowserWorkspaceUrl(val))
                    {
                        _dropdownStatusLabel.Text = "⚠️ Invalid URL or folder path.";
                        return;
                    }

                    ConfigManager.Current.RepoPath = val;
                    ConfigManager.Current.GitHubToken = tokenInput.Text.Trim();
                    ConfigManager.Save();
                    UpdateRepoFooterLabel();
                    _dropdownStatusLabel.Text = string.IsNullOrEmpty(val)
                        ? "🌐 Browser / Screen-only mode active"
                        : (RepoContextService.TryParseGitHubRepo(val, out var gh) && gh != null
                            ? $"🐙 Linked GitHub: {gh.Owner}/{gh.Repo}"
                            : (RepoContextService.IsBrowserWorkspaceUrl(val)
                                ? "🌐 Linked Browser/Codespace Workspace"
                                : $"📁 Repo set: {Path.GetFileName(val)}"));
                    dlg.Close();
                });
                saveBtn.Height = 34;
                dlg.Controls.Add(saveBtn);

                var browserModeBtn = CreateMonochromeGlassBtn("🌐 Browser Mode", 156, 142, 145, (s, e) =>
                {
                    ConfigManager.Current.RepoPath = "";
                    ConfigManager.Save();
                    UpdateRepoFooterLabel();
                    _dropdownStatusLabel.Text = "🌐 Browser / Screen-only mode active";
                    dlg.Close();
                });
                browserModeBtn.Height = 34;
                dlg.Controls.Add(browserModeBtn);

                var browseLocalBtn = CreateMonochromeGlassBtn("📁 Local Folder...", 309, 142, 155, (s, e) =>
                {
                    using var fbd = new FolderBrowserDialog
                    {
                        Description = "Select Local Repository Root Folder",
                        SelectedPath = Directory.Exists(repoInput.Text) ? repoInput.Text : Environment.CurrentDirectory
                    };
                    if (fbd.ShowDialog(dlg) == DialogResult.OK)
                    {
                        repoInput.Text = fbd.SelectedPath;
                    }
                });
                browseLocalBtn.Height = 34;
                dlg.Controls.Add(browseLocalBtn);

                dlg.AcceptButton = saveBtn;
                dlg.ShowDialog(this);
        }

        private void UpdateRepoFooterLabel()
        {
            string repoPath = RepoContextService.ResolveActiveRepoPath();
            if (string.IsNullOrEmpty(repoPath))
            {
                _repoFooterLabel.Text = "🌐 Repo: Browser/Screen mode (click [📁 Repo] to link GitHub/Codespace)";
            }
            else if (RepoContextService.TryParseGitHubRepo(repoPath, out var gh) && gh != null)
            {
                _repoFooterLabel.Text = $"🐙 GitHub: {gh.Owner}/{gh.Repo}";
            }
            else if (RepoContextService.IsBrowserWorkspaceUrl(repoPath))
            {
                _repoFooterLabel.Text = $"🌐 Codespace/Browser: {repoPath}";
            }
            else
            {
                _repoFooterLabel.Text = $"📁 Repo: {repoPath}";
            }
        }

        public async Task RunRepoTestsAndDiagnoseAsync(string? customCommand = null)
        {
            string repoPath = RepoContextService.ResolveActiveRepoPath();
            if (string.IsNullOrEmpty(repoPath))
            {
                Show();
                Expand();
                BeginConversationTurn("🧪 [TEST RUNNER]");
                AppendTextFormatted(
                    "🌐 Browser / Codespaces mode active.\n" +
                    "• Run your tests inside the Codespace/browser terminal, then click **[🐞 Fix]** (or paste the error here) to get the exact file, line, and fix.\n" +
                    "• Optional: click **[📁 Repo]** to link `https://github.com/owner/repo` or your `*.github.dev` URL.",
                    TextSecondary);
                return;
            }

            if (RepoContextService.TryParseGitHubRepo(repoPath, out var gh) && gh != null)
            {
                Show();
                Expand();
                BeginConversationTurn($"🧪 [TEST RUNNER — {gh.Owner}/{gh.Repo}]");
                AppendTextFormatted(
                    "🌐 Linked repository is a remote GitHub repo (Codespaces/browser).\n" +
                    "Run tests inside your Codespaces terminal, then click **[🐞 Fix]** or paste the failing test output into the chatbox — StealthScreenAI will pull the exact failing files from GitHub and tell you what & where to fix.",
                    TextSecondary);
                return;
            }

            if (RepoContextService.IsBrowserWorkspaceUrl(repoPath))
            {
                Show();
                Expand();
                BeginConversationTurn("🧪 [CODESPACE / BROWSER WORKSPACE]");
                AppendTextFormatted(
                    $"🌐 Linked workspace: `{repoPath}`\n" +
                    "Run tests inside your Codespaces terminal, then click **[🐞 Fix]** (or paste the failing error in chat) to diagnose the exact file, line number, and code fix.",
                    TextSecondary);
                return;
            }

            string testCmd = string.IsNullOrWhiteSpace(customCommand)
                ? RepoContextService.DetectTestCommand(repoPath)
                : customCommand.Trim();

            Show();
            Expand();
            BeginConversationTurn($"🧪 [TEST RUNNER] Running `{testCmd}` in {repoPath}...");
            _dropdownStatusLabel.Text = $"⏳ Running `{testCmd}`...";
            SetButtonsEnabled(false);

            try
            {
                _solveCts?.Cancel();
                _solveCts = new CancellationTokenSource();

                TestRunResult result = await RepoContextService.RunTestsAsync(repoPath, testCmd, _solveCts.Token);

                if (result.Success)
                {
                    AppendTextFormatted($"✅ Tests PASSED (Exit Code {result.ExitCode})\n\n{result.Output}\n", Color.FromArgb(160, 235, 160));
                    _dropdownStatusLabel.Text = $"✅ Tests Passed ({testCmd})";
                    return;
                }

                // Tests failed: show output and automatically ask Gemini what and where to fix
                AppendTextFormatted($"❌ Tests FAILED (Exit Code {result.ExitCode})\n\n{result.Output}\n\n", Color.FromArgb(240, 175, 175));
                AppendTextFormatted("🔍 Analyzing failing files & line numbers in repository...\n\n", TextSecondary);
                _dropdownStatusLabel.Text = "🔍 Diagnosing test failure with Gemini...";

                string repoCtx = RepoContextService.BuildRepoDebugContext(repoPath, result.Output);
                string bugPrompt =
                    $"The test command `{result.Command}` failed in repository `{repoPath}` with exit code {result.ExitCode}.\n" +
                    $"Test output:\n```\n{result.Output}\n```\n\n" +
                    "Tell me exactly WHAT failed, WHERE to fix it (file path, line number, method), and provide the exact code fix.";

                await _aiClient.StreamChatAsync(
                    bugPrompt,
                    imageBase64: null,
                    repoContext: repoCtx,
                    isBugFixMode: true,
                    chunk =>
                    {
                        if (IsHandleCreated)
                        {
                            BeginInvoke(() => AppendTextFormatted(chunk, TextPrimary));
                        }
                    },
                    status =>
                    {
                        if (IsHandleCreated)
                        {
                            BeginInvoke(() =>
                            {
                                _dropdownStatusLabel.Text = status;
                                AppendTextFormatted($"\nℹ️ [{status}]\n\n", TextSecondary);
                            });
                        }
                    },
                    _solveCts.Token
                );

                _dropdownStatusLabel.Text = $"✓ Bug Fix Ready ({ConfigManager.Current.GeminiModel} | Memory: {_aiClient.HistoryTurnCount / 2} turns)";
            }
            catch (Exception ex)
            {
                AppendTextFormatted($"\n❌ Test Runner Error: {ex.Message}\n", Color.FromArgb(240, 175, 175));
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        /// <summary>
        /// Captures System Audio loopback (interviewer speech) and streams oral viva defense.
        /// </summary>
        public async void ToggleVivaAudioCapture()
        {
            if (AudioCaptureService.IsRecording)
            {
                AudioCaptureService.StopRecordingEarly();
                _vivaAudioBtn.Text = "⏳ ...";
                return;
            }

            Show();
            Expand();
            BeginConversationTurn("🎙️ [VIVA ORAL COPILOT] Listening to system audio (interviewer question)...");

            _vivaAudioBtn.Text = "⏹️ Stop (10s)";
            _dropdownStatusLabel.Text = "🔴 RECORDING SYSTEM AUDIO...";

            _audioCts?.Cancel();
            _audioCts = new CancellationTokenSource();

            try
            {
                string base64Wav = await AudioCaptureService.CaptureSystemAudioWavBase64Async(
                    durationSeconds: 10,
                    onCountdownTick: remaining =>
                    {
                        if (IsHandleCreated)
                        {
                            BeginInvoke(() =>
                            {
                                _vivaAudioBtn.Text = $"⏹️ ({remaining}s)";
                                _dropdownStatusLabel.Text = $"🔴 LISTENING TO AUDIO ({remaining}s remaining)...";
                            });
                        }
                    },
                    externalCt: _audioCts.Token
                );

                _vivaAudioBtn.Text = "🎙️ Viva";
                _dropdownStatusLabel.Text = "Generating viva response...";

                if (string.IsNullOrEmpty(base64Wav))
                {
                    AppendTextFormatted("⚠️ No audio detected. Ensure audio is playing through speakers/headphones.\n", Color.FromArgb(220, 180, 180));
                    return;
                }

                AppendTextFormatted("⏳ Streaming viva answer from Gemini...\n\n", TextSecondary);

                _solveCts?.Cancel();
                _solveCts = new CancellationTokenSource();

                await _aiClient.StreamSolveAudioAsync(
                    base64Wav,
                    chunk =>
                    {
                        if (IsHandleCreated)
                        {
                            BeginInvoke(() => AppendTextFormatted(chunk, TextPrimary));
                        }
                    },
                    status =>
                    {
                        if (IsHandleCreated)
                        {
                            BeginInvoke(() =>
                            {
                                _dropdownStatusLabel.Text = status;
                                AppendTextFormatted($"\nℹ️ [{status}]\n\n", TextSecondary);
                            });
                        }
                    },
                    _solveCts.Token
                );

                _dropdownStatusLabel.Text = $"✓ Viva Answer Ready ({ConfigManager.Current.GeminiModel} | Memory: {_aiClient.HistoryTurnCount / 2} turns)";
            }
            catch (Exception ex)
            {
                AppendTextFormatted($"\n❌ Viva Audio Error: {ex.Message}\n", Color.FromArgb(220, 180, 180));
            }
            finally
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(() => _vivaAudioBtn.Text = "🎙️ Viva");
                }
            }
        }

        public void DisplayCommandResult(string command, string result)
        {
            Show();
            Expand();
            BeginConversationTurn($"⚡ Command: {command}");
            AppendTextFormatted(result, TextPrimary);
        }

        public void ToggleGhostMode()
        {
            _isGhostMode = !_isGhostMode;
            IntPtr curStyle = NativeWin32.GetWindowLongPtr(Handle, NativeWin32.GWL_EXSTYLE);
            long style = curStyle.ToInt64();

            if (_isGhostMode)
            {
                style |= NativeWin32.WS_EX_TRANSPARENT;
                _ghostBtn.ForeColor = Color.White;
                _brandLabel.Text = "👻 GHOST";
                Opacity = 0.50;
            }
            else
            {
                style &= ~NativeWin32.WS_EX_TRANSPARENT;
                _ghostBtn.ForeColor = TextSecondary;
                _brandLabel.Text = "⚡ STEALTH";
                Opacity = ConfigManager.Current.Opacity;
            }

            NativeWin32.SetWindowLongPtr(Handle, NativeWin32.GWL_EXSTYLE, new IntPtr(style));
        }

        public void UpdateVoiceBadge(string status)
        {
            if (IsHandleCreated)
            {
                BeginInvoke(() => _dropdownStatusLabel.Text = status);
            }
        }

        public void OpenSettings()
        {
            using var dlg = new SettingsDialog();
            dlg.ShowDialog(this);
            UpdateRepoFooterLabel();
        }

        public void ClearContent()
        {
            _solveCts?.Cancel();
            _aiClient.ClearHistory();
            ShowWelcomeBanner();
            _dropdownStatusLabel.Text = "● New Chat Started (Memory Cleared)";
        }

        public void CopyCode()
        {
            string code = ExtractLatestCodeSnippet(_contentBox.Text);
            if (string.IsNullOrEmpty(code))
            {
                _dropdownStatusLabel.Text = "⚠️ No code snippet found in the current conversation.";
                return;
            }

            NativeAutomation.SetClipboardSafe(code);
            _copyBtn.Text = "✓ Copied!";
            _quickCopyBtn.Text = "✓ Copied";
            var t = new System.Windows.Forms.Timer { Interval = 1600 };
            t.Tick += (s, e) =>
            {
                _copyBtn.Text = "📋 Copy";
                _quickCopyBtn.Text = "📋 Copy";
                t.Stop();
                t.Dispose();
            };
            t.Start();
        }

        public void AutoInjectCode()
        {
            string code = ExtractLatestCodeSnippet(_contentBox.Text);
            if (string.IsNullOrEmpty(code))
            {
                _dropdownStatusLabel.Text = "⚠️ No code snippet found to paste.";
                return;
            }

            IntPtr targetHwnd = _lastActiveAppHwnd != IntPtr.Zero ? _lastActiveAppHwnd : NativeWin32.GetForegroundWindow();
            Hide();
            Task.Delay(200).ContinueWith(_ =>
            {
                NativeAutomation.PasteCodeToForeground(code, targetHwnd);
                Task.Delay(800).ContinueWith(__ =>
                {
                    if (IsHandleCreated) BeginInvoke(Show);
                });
            });
        }

        public void ReplaceCodeInCompiler()
        {
            string code = ExtractLatestCodeSnippet(_contentBox.Text);
            if (string.IsNullOrEmpty(code))
            {
                _dropdownStatusLabel.Text = "⚠️ No code snippet found to replace in compiler.";
                return;
            }

            _replaceBtn.Text = "🔄 ...";
            _quickReplaceBtn.Text = "🔄 ...";
            IntPtr targetHwnd = _lastActiveAppHwnd != IntPtr.Zero ? _lastActiveAppHwnd : NativeWin32.GetForegroundWindow();
            Hide();
            Task.Delay(200).ContinueWith(_ =>
            {
                NativeAutomation.ReplaceCodeInCompiler(code, targetHwnd);
                Task.Delay(900).ContinueWith(__ =>
                {
                    if (IsHandleCreated)
                    {
                        BeginInvoke(() =>
                        {
                            Show();
                            _replaceBtn.Text = "✓ Done!";
                            _quickReplaceBtn.Text = "✓ Done";
                            var t = new System.Windows.Forms.Timer { Interval = 1800 };
                            t.Tick += (s, e) =>
                            {
                                _replaceBtn.Text = "🔄 Replace";
                                _quickReplaceBtn.Text = "🔄 Replace";
                                t.Stop();
                                t.Dispose();
                            };
                            t.Start();
                        });
                    }
                });
            });
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _solveScreenBtn.Enabled = enabled;
            _codeSolverBtn.Enabled = enabled;
            _mcqSolverBtn.Enabled = enabled;
            _fixBugBtn.Enabled = enabled;
            _runTestsBtn.Enabled = enabled;
            _chatPromptLaunchBtn.Enabled = enabled;
            _quickCopyBtn.Enabled = enabled;
            _quickReplaceBtn.Enabled = enabled;
        }

        private static string ExtractLatestCodeSnippet(string fullText)
        {
            var matches = Regex.Matches(fullText, @"(?s)```[a-zA-Z0-9_#+-]*\r?\n?(.*?)\r?\n?```");
            if (matches.Count > 0)
            {
                return matches[matches.Count - 1].Groups[1].Value.Trim();
            }
            return "";
        }

        private void AppendTextFormatted(string text, Color color)
        {
            _contentBox.SelectionStart = _contentBox.TextLength;
            _contentBox.SelectionLength = 0;
            _contentBox.SelectionColor = color;
            _contentBox.AppendText(text);
            _contentBox.ScrollToCaret();
        }

        private static void DrawGlassBorder(Graphics g, Rectangle rect, int radius)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(GlassBorderColor, 1.0f);
            using var path = CreateRoundedRect(new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), radius);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Button CreateMonochromeGlassBtn(string text, int x, int y, int width, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 30),
                BackColor = ButtonGlassBg,
                ForeColor = TextPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Cursor = Cursors.Arrow
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = GlassBorderColor;
            btn.FlatAppearance.MouseOverBackColor = ButtonGlassHover;
            btn.Click += onClick;
            return btn;
        }

        private static Button CreateIconButton(string text, int x, int y, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(24, 26),
                BackColor = Color.Transparent,
                ForeColor = TextSecondary,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Arrow
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ButtonGlassHover;
            btn.Click += onClick;
            return btn;
        }

        private void OnBarMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragStart = e.Location;
            }
        }

        private void OnBarMouseMove(object? sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point diff = new Point(e.Location.X - _dragStart.X, e.Location.Y - _dragStart.Y);
                Location = new Point(Location.X + diff.X, Location.Y + diff.Y);
            }
        }

        private void OnBarMouseUp(object? sender, MouseEventArgs e)
        {
            _isDragging = false;
        }

        private sealed class PointerRichTextBox : RichTextBox
        {
            public PointerRichTextBox()
            {
                Cursor = Cursors.Arrow;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeWin32.WM_SETCURSOR)
                {
                    Cursor.Current = Cursors.Arrow;
                    m.Result = (IntPtr)1;
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
