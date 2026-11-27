using System;
using System.Drawing;
using System.Windows.Forms;

namespace StealthScreenAI
{
    public class HotkeyWindow : NativeWindow
    {
        public const int HOTKEY_ID_HIDE = 1;
        public const int HOTKEY_ID_SOLVE = 2;
        public const int HOTKEY_ID_COMMAND = 3;
        public const int HOTKEY_ID_SNIP = 4;
        public const int HOTKEY_ID_GHOST = 5;

        public event Action? OnHide;
        public event Action? OnSolve;
        public event Action? OnCommand;
        public event Action? OnSnip;
        public event Action? OnGhost;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
            RegisterModifierFreeHotkeys();
        }

        private void RegisterModifierFreeHotkeys()
        {
            try
            {
                uint noModifiers = NativeWin32.MOD_NOREPEAT;
                NativeWin32.RegisterHotKey(Handle, HOTKEY_ID_HIDE, noModifiers, (uint)Keys.F8);
                NativeWin32.RegisterHotKey(Handle, HOTKEY_ID_SOLVE, noModifiers, (uint)Keys.F9);
                NativeWin32.RegisterHotKey(Handle, HOTKEY_ID_COMMAND, noModifiers, (uint)Keys.F10);
                NativeWin32.RegisterHotKey(Handle, HOTKEY_ID_SNIP, noModifiers, (uint)Keys.F7);
                NativeWin32.RegisterHotKey(Handle, HOTKEY_ID_GHOST, noModifiers, (uint)Keys.F6);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeWin32.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                switch (id)
                {
                    case HOTKEY_ID_HIDE:
                        OnHide?.Invoke();
                        break;
                    case HOTKEY_ID_SOLVE:
                        OnSolve?.Invoke();
                        break;
                    case HOTKEY_ID_COMMAND:
                        OnCommand?.Invoke();
                        break;
                    case HOTKEY_ID_SNIP:
                        OnSnip?.Invoke();
                        break;
                    case HOTKEY_ID_GHOST:
                        OnGhost?.Invoke();
                        break;
                }
            }
            base.WndProc(ref m);
        }

        public void UnregisterAll()
        {
            try
            {
                for (int i = 1; i <= 5; i++)
                {
                    NativeWin32.UnregisterHotKey(Handle, i);
                }
                DestroyHandle();
            }
            catch { }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("--verify", StringComparison.OrdinalIgnoreCase))
            {
                VerifyStealth.RunVerification();
                return;
            }

            ApplicationConfiguration.Initialize();

            // 1. First-Run API Key Onboarding
            if (string.IsNullOrWhiteSpace(ConfigManager.Current.GeminiApiKey))
            {
                using var settingsDlg = new SettingsDialog();
                settingsDlg.ShowDialog();
            }

            var overlay = new StealthOverlayWindow();
            var snipWindow = new SnipOverlayWindow();
            var commandBar = new CommandBarWindow();
            var hotkeyWin = new HotkeyWindow();
            var voiceEngine = new VoiceRecognitionService();
            var triggerEngine = new AdvancedTriggerService();

            // Overlay spans top bar as transparent glass HUD
            overlay.Show();

            // 2. Wire UI Actions
            overlay.RequestSnip += () => snipWindow.StartSnip();
            overlay.RequestChatInput += attachScreen =>
                commandBar.ShowCentered(overlay.Visible ? overlay.Bounds : null, attachScreen);
            snipWindow.SnipCompleted += rect => overlay.SolveRegion(rect);

            commandBar.ChatSubmitted += (cmd, attachScreen) => overlay.SubmitExternalQuery(cmd, attachScreen);

            // 3. Wire Deliberate Zero-Hook Visibility Toggles Only
            // (Top-edge hover, hot-corner, and double middle-click auto-solve are intentionally NOT wired
            // so responses stay on screen without unrequested refreshes.)
            triggerEngine.OnShakeTriggered += () =>
            {
                if (overlay.Visible) overlay.Hide();
                else overlay.Show();
            };

            triggerEngine.OnDoubleTapCtrlTriggered += () =>
            {
                if (overlay.Visible) overlay.Hide();
                else overlay.Show();
            };
            triggerEngine.Start();

            // 4. Wire Global Hotkeys
            hotkeyWin.OnHide += () =>
            {
                if (overlay.Visible) overlay.Hide();
                else overlay.Show();
            };
            hotkeyWin.OnSolve += () => overlay.SolveFullScreen(SolveMode.General);
            hotkeyWin.OnCommand += () => overlay.FocusChatBox();
            hotkeyWin.OnSnip += () => snipWindow.StartSnip();
            hotkeyWin.OnGhost += () => overlay.ToggleGhostMode();

            // 5. Initialize Voice Engine (Starts in Standby/Muted until user explicitly toggles it)
            voiceEngine.VoiceCommandRecognized += spoken =>
            {
                overlay.BeginInvoke(() => overlay.SubmitExternalQuery(spoken));
            };
            voiceEngine.VoiceStatusChanged += status =>
            {
                overlay.UpdateVoiceBadge(status);
            };
            voiceEngine.Initialize();

            // Run message loop with zero taskbar or system tray ("Show hidden icons") footprint
            Application.Run();

            hotkeyWin.UnregisterAll();
            triggerEngine.Dispose();
            voiceEngine.Dispose();
        }
    }
}
