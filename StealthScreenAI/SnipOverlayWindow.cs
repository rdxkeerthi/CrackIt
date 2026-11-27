using System;
using System.Drawing;
using System.Windows.Forms;

namespace StealthScreenAI
{
    public class SnipOverlayWindow : Form
    {
        private Point _startPoint;
        private Rectangle _selectedRect;
        private bool _isSelecting = false;
        public event Action<Rectangle>? SnipCompleted;

        public SnipOverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
            Opacity = 0.35;
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

        public void StartSnip()
        {
            Rectangle bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            Bounds = bounds;
            _selectedRect = Rectangle.Empty;
            _isSelecting = false;
            Show();
            Activate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _startPoint = e.Location;
                _selectedRect = new Rectangle(e.Location, Size.Empty);
                _isSelecting = true;
                Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                Hide();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isSelecting)
            {
                int x = Math.Min(_startPoint.X, e.X);
                int y = Math.Min(_startPoint.Y, e.Y);
                int width = Math.Abs(_startPoint.X - e.X);
                int height = Math.Abs(_startPoint.Y - e.Y);
                _selectedRect = new Rectangle(x, y, width, height);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isSelecting && e.Button == MouseButtons.Left)
            {
                _isSelecting = false;
                Hide();
                if (_selectedRect.Width > 15 && _selectedRect.Height > 15)
                {
                    SnipCompleted?.Invoke(_selectedRect);
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_isSelecting && _selectedRect.Width > 0 && _selectedRect.Height > 0)
            {
                using var pen = new Pen(Color.FromArgb(88, 166, 255), 2);
                e.Graphics.DrawRectangle(pen, _selectedRect);
            }
        }
    }
}
