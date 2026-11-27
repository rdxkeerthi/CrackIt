using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StealthScreenAI
{
    /// <summary>
    /// Capture-excluded floating chat input bar.
    /// Keeps StealthOverlayWindow 100% focus-immune (WS_EX_NOACTIVATE) while giving the user
    /// a dedicated multi-line chatbox for queries, test errors, and follow-up messages.
    /// </summary>
    public class CommandBarWindow : Form
    {
        private readonly TextBox _inputBox;
        private readonly CheckBox _attachScreenCheck;
        private readonly Button _runBtn;
        private readonly Button _closeBtn;
        private IntPtr _previousForegroundHwnd = IntPtr.Zero;

        public event Action<string>? CommandSubmitted;
        public event Action<string, bool>? ChatSubmitted;

        public CommandBarWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Width = 760;
            Height = 76;
            BackColor = Color.FromArgb(14, 16, 22);

            var iconLabel = new Label
            {
                Text = "💬",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Location = new Point(12, 14),
                AutoSize = true
            };
            Controls.Add(iconLabel);

            _inputBox = new TextBox
            {
                Location = new Point(42, 12),
                Size = new Size(495, 52),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(10, 12, 16),
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Ask a follow-up, paste test error, or run command (Enter=Send, Shift+Enter=Newline, Esc=Close)..."
            };
            _inputBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    Submit();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    CloseAndRestoreFocus();
                }
            };
            Controls.Add(_inputBox);

            _attachScreenCheck = new CheckBox
            {
                Text = "📷 Screen",
                Location = new Point(546, 26),
                Size = new Size(86, 24),
                ForeColor = Color.FromArgb(190, 195, 205),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Checked = false
            };
            Controls.Add(_attachScreenCheck);

            _runBtn = new Button
            {
                Text = "➤ Send",
                Location = new Point(636, 20),
                Size = new Size(82, 34),
                BackColor = Color.FromArgb(30, 255, 255, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _runBtn.FlatAppearance.BorderSize = 1;
            _runBtn.FlatAppearance.BorderColor = Color.FromArgb(60, 255, 255, 255);
            _runBtn.Click += (s, e) => Submit();
            Controls.Add(_runBtn);

            _closeBtn = new Button
            {
                Text = "✕",
                Location = new Point(724, 21),
                Size = new Size(28, 32),
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(160, 165, 175),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.Click += (s, e) => CloseAndRestoreFocus();
            Controls.Add(_closeBtn);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1.25f);
            using var path = CreateRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 8);
            e.Graphics.DrawPath(pen, path);
        }

        public void ShowCentered(Rectangle? anchorOverlayBounds = null, bool defaultAttachScreen = false)
        {
            IntPtr fg = NativeWin32.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != Handle)
            {
                _previousForegroundHwnd = fg;
            }

            _attachScreenCheck.Checked = defaultAttachScreen;

            if (anchorOverlayBounds.HasValue)
            {
                Rectangle b = anchorOverlayBounds.Value;
                int x = b.Left + Math.Max(0, (b.Width - Width) / 2);
                int y = Math.Max(8, b.Bottom - Height - 32);
                Location = new Point(x, y);
            }
            else
            {
                Rectangle screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
                Location = new Point((screen.Width - Width) / 2, 80);
            }

            Show();
            if (ConfigManager.Current.ExcludeFromCapture)
            {
                NativeWin32.ApplyCaptureExclusion(Handle);
            }
            NativeWin32.ForceForeground(Handle);
            Activate();
            _inputBox.Focus();
            _inputBox.SelectAll();
        }

        private void CloseAndRestoreFocus()
        {
            Hide();
            if (_previousForegroundHwnd != IntPtr.Zero && _previousForegroundHwnd != Handle)
            {
                NativeWin32.ForceForeground(_previousForegroundHwnd);
            }
        }

        private void Submit()
        {
            string text = _inputBox.Text.Trim();
            bool includeScreen = _attachScreenCheck.Checked;
            CloseAndRestoreFocus();
            if (!string.IsNullOrEmpty(text))
            {
                _inputBox.Clear();
                ChatSubmitted?.Invoke(text, includeScreen);
                CommandSubmitted?.Invoke(text);
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
