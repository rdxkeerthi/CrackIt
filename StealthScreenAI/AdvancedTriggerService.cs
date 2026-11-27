using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StealthScreenAI
{
    /// <summary>
    /// Advanced stealth trigger engine using zero-hook passive polling.
    /// Completely immune to WH_KEYBOARD_LL / WH_MOUSE_LL hook scanners.
    /// Provides 3 robust trigger methods that replace fragile 2-pixel cursor edges:
    /// 1. Mouse Velocity Shake: Rapid horizontal wiggling anywhere on screen.
    /// 2. Passive Double-Tap Key: Quick double-press of Ctrl (polled via GetAsyncKeyState).
    /// 3. Passive Double Middle-Click: Quick double-click of the scroll wheel.
    /// 4. DPI-Aware Top Edge Proximity (generous 20px threshold).
    /// </summary>
    public class AdvancedTriggerService : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private readonly System.Windows.Forms.Timer _timer;

        // --- Mouse Shake Detection State ---
        private int _lastX = -1;
        private int _lastDirection = 0; // -1 = left, 1 = right
        private int _shakeReversals = 0;
        private DateTime _shakeStartTime = DateTime.MinValue;
        private DateTime _lastShakeTriggered = DateTime.MinValue;

        // --- Passive Double-Tap Ctrl Detection State ---
        private bool _ctrlWasDown = false;
        private DateTime _lastCtrlPress = DateTime.MinValue;
        private DateTime _lastCtrlTriggered = DateTime.MinValue;

        // --- Public Events ---
        public event Action? OnShakeTriggered;            // Mouse shake -> Toggle HUD Visibility
        public event Action? OnDoubleTapCtrlTriggered;    // Double-tap Ctrl -> Toggle HUD Visibility
#pragma warning disable CS0067
        // Kept for API compatibility; automatic unrequested solve/corner triggers are disabled
        // so responses are never overwritten or hidden unexpectedly.
        public event Action? OnDoubleMiddleClickTriggered;
        public event Action? OnTopEdgeHoverTriggered;
        public event Action? OnHotCornerTriggered;
#pragma warning restore CS0067

        public AdvancedTriggerService()
        {
            // Passive 25ms high-responsiveness polling (Zero Windows Hooks)
            _timer = new System.Windows.Forms.Timer { Interval = 25 };
            _timer.Tick += OnTick;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void OnTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;

            // 1. Passive Double-Tap Ctrl Detection (GetAsyncKeyState - Zero Hook!)
            short ctrlState = NativeWin32.GetAsyncKeyState(NativeWin32.VK_CONTROL);
            bool ctrlIsDown = (ctrlState & 0x8000) != 0;

            if (ctrlIsDown && !_ctrlWasDown)
            {
                // Key down event
                if ((now - _lastCtrlPress).TotalMilliseconds <= 380 && (now - _lastCtrlTriggered).TotalMilliseconds > 600)
                {
                    _lastCtrlTriggered = now;
                    _lastCtrlPress = DateTime.MinValue;
                    OnDoubleTapCtrlTriggered?.Invoke();
                }
                else
                {
                    _lastCtrlPress = now;
                }
            }
            _ctrlWasDown = ctrlIsDown;

            // 2. Coordinate Analysis: Deliberate Mouse Shake Gesture Only
            // (Double middle-click, hot-corner, and top-edge hover auto-triggers removed to prevent
            // accidental refreshes, hidden bars, or overwritten responses while reading/clicking.)
            if (!GetCursorPos(out POINT pt)) return;

            if (_lastX != -1)
            {
                int dx = pt.X - _lastX;
                int currentDirection = dx > 15 ? 1 : (dx < -15 ? -1 : 0);

                if (currentDirection != 0)
                {
                    if (_lastDirection != 0 && currentDirection != _lastDirection)
                    {
                        // Direction reversed!
                        if ((now - _shakeStartTime).TotalMilliseconds > 500)
                        {
                            _shakeStartTime = now;
                            _shakeReversals = 1;
                        }
                        else
                        {
                            _shakeReversals++;
                            // 4 quick directional reversals within 500ms = deliberate rapid shake
                            if (_shakeReversals >= 4)
                            {
                                if ((now - _lastShakeTriggered).TotalMilliseconds > 1200)
                                {
                                    _lastShakeTriggered = now;
                                    OnShakeTriggered?.Invoke();
                                }
                                _shakeReversals = 0;
                                _shakeStartTime = DateTime.MinValue;
                            }
                        }
                    }
                    else if (_shakeReversals == 0)
                    {
                        _shakeStartTime = now;
                        _shakeReversals = 1;
                    }

                    _lastDirection = currentDirection;
                }
                else
                {
                    if ((now - _shakeStartTime).TotalMilliseconds > 500)
                    {
                        _shakeReversals = 0;
                        _lastDirection = 0;
                    }
                }
            }

            _lastX = pt.X;
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
