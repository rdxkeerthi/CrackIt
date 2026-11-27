using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StealthScreenAI
{
    public static class NativeAutomation
    {
        private static readonly Dictionary<string, string> ProcessAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "edge", "msedge.exe" },
            { "microsoft edge", "msedge.exe" },
            { "chrome", "chrome.exe" },
            { "google chrome", "chrome.exe" },
            { "firefox", "firefox.exe" },
            { "code", "Code.exe" },
            { "vs code", "Code.exe" },
            { "visual studio code", "Code.exe" },
            { "notepad", "notepad.exe" },
            { "calculator", "calc.exe" },
            { "terminal", "wt.exe" },
            { "cmd", "cmd.exe" },
            { "spotify", "Spotify.exe" },
            { "discord", "Discord.exe" }
        };

        /// <summary>
        /// Terminates processes matching target name via native Win32 APIs.
        /// Zero PowerShell or cmd.exe overhead.
        /// </summary>
        public static string TerminateProcessByName(string targetName)
        {
            string clean = targetName.Trim();
            if (ProcessAliases.TryGetValue(clean, out string? alias))
            {
                clean = alias;
            }
            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                clean += ".exe";
            }

            int terminatedCount = 0;
            IntPtr snapshot = NativeWin32.CreateToolhelp32Snapshot(NativeWin32.TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero)
            {
                return $"Failed to create process snapshot (Error {Marshal.GetLastWin32Error()})";
            }

            try
            {
                var entry = new NativeWin32.PROCESSENTRY32
                {
                    dwSize = (uint)Marshal.SizeOf(typeof(NativeWin32.PROCESSENTRY32))
                };

                if (NativeWin32.Process32First(snapshot, ref entry))
                {
                    do
                    {
                        if (string.Equals(entry.szExeFile, clean, StringComparison.OrdinalIgnoreCase))
                        {
                            IntPtr hProc = NativeWin32.OpenProcess(NativeWin32.PROCESS_TERMINATE, false, entry.th32ProcessID);
                            if (hProc != IntPtr.Zero)
                            {
                                if (NativeWin32.TerminateProcess(hProc, 0))
                                {
                                    terminatedCount++;
                                }
                                NativeWin32.CloseHandle(hProc);
                            }
                        }
                    }
                    while (NativeWin32.Process32Next(snapshot, ref entry));
                }
            }
            finally
            {
                NativeWin32.CloseHandle(snapshot);
            }

            return terminatedCount > 0
                ? $"Successfully terminated {terminatedCount} instance(s) of {clean}."
                : $"Process '{clean}' was not found running.";
        }

        /// <summary>
        /// Disconnects active Wi-Fi interface using native wlanapi.dll.
        /// </summary>
        public static string DisconnectWiFi()
        {
            uint result = NativeWin32.WlanOpenHandle(2, IntPtr.Zero, out uint _, out IntPtr clientHandle);
            if (result != 0)
            {
                return $"WlanOpenHandle failed (Code {result})";
            }

            try
            {
                result = NativeWin32.WlanEnumInterfaces(clientHandle, IntPtr.Zero, out IntPtr pInterfaceList);
                if (result != 0 || pInterfaceList == IntPtr.Zero)
                {
                    return "No wireless interfaces found.";
                }

                try
                {
                    var list = Marshal.PtrToStructure<NativeWin32.WLAN_INTERFACE_INFO_LIST>(pInterfaceList);
                    if (list.dwNumberOfItems > 0)
                    {
                        // Disconnect primary wireless interface
                        Guid interfaceGuid = list.InterfaceInfo.InterfaceGuid;
                        uint disconnectRes = NativeWin32.WlanDisconnect(clientHandle, ref interfaceGuid, IntPtr.Zero);
                        return disconnectRes == 0
                            ? "Wi-Fi interface disconnected successfully."
                            : $"Wi-Fi disconnect returned code: {disconnectRes}";
                    }
                    return "No active wireless adapters detected.";
                }
                finally
                {
                    NativeWin32.WlanFreeMemory(pInterfaceList);
                }
            }
            finally
            {
                NativeWin32.WlanCloseHandle(clientHandle, IntPtr.Zero);
            }
        }

        /// <summary>
        /// Toggles system audio mute via virtual key event.
        /// </summary>
        public static string ToggleMute()
        {
            NativeWin32.keybd_event(NativeWin32.VK_VOLUME_MUTE, 0, 0, UIntPtr.Zero);
            NativeWin32.keybd_event(NativeWin32.VK_VOLUME_MUTE, 0, NativeWin32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            return "Toggled system mute.";
        }

        /// <summary>
        /// Adjusts volume up or down.
        /// </summary>
        public static string AdjustVolume(bool increase, int steps = 5)
        {
            byte vk = increase ? NativeWin32.VK_VOLUME_UP : NativeWin32.VK_VOLUME_DOWN;
            for (int i = 0; i < steps; i++)
            {
                NativeWin32.keybd_event(vk, 0, 0, UIntPtr.Zero);
                NativeWin32.keybd_event(vk, 0, NativeWin32.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(15);
            }
            return (increase ? "Increased" : "Decreased") + " system volume.";
        }

        /// <summary>
        /// Reliably copies text to clipboard with retries against external clipboard locks.
        /// </summary>
        public static bool SetClipboardSafe(string text, int retries = 5)
        {
            if (string.IsNullOrEmpty(text)) return false;

            for (int i = 0; i < retries; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true, 5, 50);
                    return true;
                }
                catch
                {
                    Thread.Sleep(50);
                }
            }

            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Copies code to clipboard and injects via Ctrl+V into foreground window.
        /// </summary>
        public static void PasteCodeToForeground(string code, IntPtr targetHwnd = default)
        {
            if (string.IsNullOrEmpty(code)) return;

            if (targetHwnd != IntPtr.Zero)
            {
                NativeWin32.ForceForeground(targetHwnd);
                Thread.Sleep(150);
            }

            SetClipboardSafe(code);
            Thread.Sleep(150);

            // Send Ctrl+V keystroke
            NativeWin32.keybd_event(NativeWin32.VK_CONTROL, 0, 0, UIntPtr.Zero);
            NativeWin32.keybd_event(NativeWin32.VK_V, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            NativeWin32.keybd_event(NativeWin32.VK_V, 0, NativeWin32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            NativeWin32.keybd_event(NativeWin32.VK_CONTROL, 0, NativeWin32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        /// <summary>
        /// Replaces existing compiler code on screen by sending Ctrl+A (Select All), Backspace, then Ctrl+V (Paste).
        /// </summary>
        public static void ReplaceCodeInCompiler(string code, IntPtr targetHwnd = default)
        {
            if (string.IsNullOrEmpty(code)) return;

            if (targetHwnd != IntPtr.Zero)
            {
                NativeWin32.ForceForeground(targetHwnd);
                Thread.Sleep(180);
            }

            SetClipboardSafe(code);
            Thread.Sleep(150);

            // 1. Send Ctrl+A (Select All existing code in compiler)
            NativeWin32.keybd_event(NativeWin32.VK_CONTROL, 0, 0, UIntPtr.Zero);
            NativeWin32.keybd_event(NativeWin32.VK_A, 0, 0, UIntPtr.Zero);
            Thread.Sleep(40);
            NativeWin32.keybd_event(NativeWin32.VK_A, 0, NativeWin32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            NativeWin32.keybd_event(NativeWin32.VK_CONTROL, 0, NativeWin32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(80);

            // 2. Send Backspace (Clear selected code)
            NativeWin32.keybd_event(NativeWin32.VK_BACK, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            NativeWin32.keybd_event(NativeWin32.VK_BACK, 0, NativeWin32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(80);

            // 3. Send Ctrl+V (Paste new solution code)
            NativeWin32.keybd_event(NativeWin32.VK_CONTROL, 0, 0, UIntPtr.Zero);
            NativeWin32.keybd_event(NativeWin32.VK_V, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            NativeWin32.keybd_event(NativeWin32.VK_V, 0, NativeWin32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            NativeWin32.keybd_event(NativeWin32.VK_CONTROL, 0, NativeWin32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        /// <summary>
        /// Executes a Windows system / cmd command silently and returns output.
        /// </summary>
        public static string ExecuteCmdCommand(string command)
        {
            try
            {
                string cmdToRun = command.Trim();
                if (cmdToRun.StartsWith("cmd /c ", StringComparison.OrdinalIgnoreCase))
                {
                    cmdToRun = cmdToRun.Substring(7);
                }
                else if (cmdToRun.StartsWith("cmd ", StringComparison.OrdinalIgnoreCase))
                {
                    cmdToRun = cmdToRun.Substring(4);
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {cmdToRun}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return "Failed to start command interpreter.";

                bool finished = proc.WaitForExit(4500);
                if (!finished)
                {
                    try { proc.Kill(); } catch { }
                    return "Command execution timed out after 4.5 seconds.";
                }

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    return stdout.TrimEnd();
                }
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    return $"[STDERR]\n{stderr.TrimEnd()}";
                }
                return $"Command completed with exit code {proc.ExitCode}.";
            }
            catch (Exception ex)
            {
                return $"Error executing command: {ex.Message}";
            }
        }
    }
}
