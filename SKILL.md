---
name: stealth-screen-ai
description: >-
  Comprehensive engineering, architecture, stealth-by-default invariants, and
  operational skill for StealthScreenAI (ctfmon.exe / StealthScreenAI.exe).
  Activate whenever an AI model reads, modifies, builds, packages, verifies,
  debugs, or operates this .NET 9 Win32/SEB-safe overlay copilot across any
  execution mode, desktop, trigger, or AI solver pipeline.
---

# StealthScreenAI (`ctfmon.exe`) — Master AI Engineering & Operations Skill

> [!IMPORTANT]
> **STEALTH MODE IS THE DEFAULT MANDATORY POSTURE.**
> Any AI model modifying, extending, building, or operating this codebase MUST preserve all stealth, focus-immunity, Task Manager/tray invisibility, pointer-cursor, hookless-input, and capture-exclusion guarantees by default. Never introduce focus-stealing UI calls on the HUD, global `WH_KEYBOARD_LL` / `WH_MOUSE_LL` hooks, system tray icons, uncloaked windows, or noisy external shell spawns where native Win32 APIs exist. Whenever you update the codebase, update this `SKILL.md` too.

---

## 1. Core Stealth-by-Default Invariants (Non-Negotiable)

When writing or reviewing code in `StealthScreenAI`, enforce these invariants at all times:

1. **Zero Focus Loss (Focus Immunity)**
   - `StealthOverlayWindow` MUST set `ShowWithoutActivation => true` and include `WS_EX_NOACTIVATE` (`0x08000000`), `WS_EX_TOOLWINDOW` (`0x00000080`), and `WS_EX_TOPMOST` (`0x00000008`), while clearing `WS_EX_APPWINDOW` (`0x00040000`), in `CreateParams.ExStyle`.
   - Intercept `WM_MOUSEACTIVATE` (`0x0021`) in `StealthOverlayWindow.WndProc` and always return `MA_NOACTIVATE` (`3`).
   - Use `WM_NCHITTEST` (`0x0084`) returning `HTCAPTION` (`2`) on non-control top bar areas for OS-level dragging without emitting `WM_ACTIVATE` or `WM_SETFOCUS`.
   - Re-assert Z-order exclusively with `SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)`. Never call `Activate()`, `Focus()`, or `SetForegroundWindow()` on `StealthOverlayWindow` itself.
   - Delegate interactive text typing to the dedicated, capture-excluded `CommandBarWindow` (`RequestChatInput` / `F10`), which saves and restores `_previousForegroundHwnd` when closed or submitted.

2. **Screen Capture & Compositor Exclusion (`WDA_EXCLUDEFROMCAPTURE`)**
   - Always apply `NativeWin32.ApplyCaptureExclusion(Handle)` on `OnHandleCreated` for every form/dialog (`StealthOverlayWindow`, `CommandBarWindow`, `SnipOverlayWindow`, `SettingsDialog`, and the `Link Repository` dialog), and refresh every 5 seconds on `StealthOverlayWindow`'s heartbeat timer.
   - `NativeWin32.ApplyCaptureExclusion` enforces `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (`0x00000011`) on the top-level window and recursively on every child control via `EnumChildWindows`.

3. **Task Manager & System Tray Invisibility + Pointer Cursor**
   - Every form MUST set `ShowInTaskbar = false`, include `WS_EX_TOOLWINDOW` (`0x00000080`), clear `WS_EX_APPWINDOW` (`0x00040000`), and call `NativeWin32.HideFromTaskbarAndAppList(Handle)` (which invokes COM `ITaskbarList.DeleteTab`) on `OnHandleCreated` so the process never appears in the Taskbar, Alt+Tab, or Task Manager's **Apps** section (only under Background processes when searched).
   - Never register a `NotifyIcon` / system tray icon so nothing appears in the Windows taskbar "Show hidden icons" menu.
   - Always maintain `Cursors.Arrow` across `StealthOverlayWindow` by intercepting `WM_SETCURSOR` (`0x0020`) in both `StealthOverlayWindow.WndProc` and `PointerRichTextBox.WndProc` (suppressing RichEdit's `IDC_IBEAM` text cursor) and setting `Cursor = Cursors.Arrow` on HUD controls.

4. **Zero Global Low-Level Hooks (`WH_KEYBOARD_LL` = 0) & Deliberate Triggers**
   - Never call `SetWindowsHookEx` for keyboard or mouse hooks.
   - `AdvancedTriggerService` polls passively every 25 ms via `GetAsyncKeyState` and `GetCursorPos` for deliberate **Mouse Shake** (4 horizontal reversals within 500 ms) and **Double-Tap `Ctrl`** visibility toggles only (never clearing/overwriting responses). Accidental top-edge hover, hot-corner, and double middle-click auto-solve triggers are disabled.
   - `VoiceRecognitionService` starts in standby (`IsListening = false`) and uses explicit command grammar only (confidence `> 0.72f`, no background `DictationGrammar`) so ambient audio never generates unrequested responses.

5. **Process Masquerading & Zero-Bloat Native Execution**
   - `<AssemblyName>ctfmon</AssemblyName>` in `StealthScreenAI.csproj` disguises the binary as the Windows Text Services host (`ctfmon.exe`), synced to both `ctfmon.exe` and `StealthScreenAI.exe` at root.
   - Prefer native Win32 P/Invoke (`NativeWin32.cs`, `NativeAutomation.cs`) over spawning `powershell.exe` or `cmd.exe` (Toolhelp32 for process termination, `wlanapi.dll` for Wi-Fi disconnect, `BitBlt` in-RAM screen capture at 92% JPEG quality, NAudio WASAPI loopback in-RAM audio capture).

---

## 2. Project Architecture & File Map

All source files live in `StealthScreenAI/` (`net9.0-windows`, WinForms, `AllowUnsafeBlocks=true`, `PerMonitorV2` DPI):

| File | Responsibility | Key Symbols |
| :--- | :--- | :--- |
| `Program.cs` | Entry router (`--verify`), `HotkeyWindow`, wiring overlay/snip/chat/triggers/voice with zero tray icon | `Program.Main`, `HotkeyWindow` |
| `StealthOverlayWindow.cs` | Monochrome glass HUD (`1000x46` resting / `560` expanded), persistent multi-turn transcript (`PointerRichTextBox`), `WM_MOUSEACTIVATE => MA_NOACTIVATE`, `WM_SETCURSOR => Arrow`, `WM_NCHITTEST`, repo debug controls | `StealthOverlayWindow`, `PointerRichTextBox`, `SolveFullScreen`, `RunRepoTestsAndDiagnoseAsync`, `SelectRepoFolder` |
| `CommandBarWindow.cs` | Capture-excluded multi-line chat input bar (`760x76`, `Enter`=Send, `Shift+Enter`=Newline, `Esc`=Close), restores previous foreground window | `CommandBarWindow`, `ShowCentered`, `ChatSubmitted` |
| `SnipOverlayWindow.cs` | Capture-excluded rectangular screen snip overlay (`WS_EX_TOOLWINDOW`, `HideFromTaskbarAndAppList`) | `SnipOverlayWindow`, `StartSnip` |
| `SettingsDialog.cs` | Capture-excluded API key, model selection, repo URL/folder, GitHub token, and test command settings dialog | `SettingsDialog` |
| `AdvancedTriggerService.cs` | 25 ms passive poller for deliberate Mouse Shake (4 reversals) and Double-Tap `Ctrl` visibility toggles | `AdvancedTriggerService` |
| `MouseGestureService.cs` | Compatibility wrapper inheriting `AdvancedTriggerService` | `MouseGestureService` |
| `NativeWin32.cs` | Consolidated P/Invoke declarations, `ApplyCaptureExclusion` (`EnumChildWindows`), `HideFromTaskbarAndAppList` (`ITaskbarList.DeleteTab`), `ForceForeground`, Toolhelp32 & `wlanapi.dll` | `NativeWin32`, `ApplyCaptureExclusion`, `HideFromTaskbarAndAppList`, `ForceForeground` |
| `ScreenCaptureService.cs` | In-memory GDI `BitBlt` capture to 92%-quality Base64 JPEG | `ScreenCaptureService.CaptureFullScreenBase64`, `CaptureRegionBase64` |
| `AudioCaptureService.cs` | NAudio WASAPI system audio loopback (with `WaveIn` fallback) to in-RAM Base64 WAV | `AudioCaptureService.CaptureSystemAudioWavBase64Async` |
| `VoiceRecognitionService.cs` | Standby-by-default explicit command speech recognition (no `DictationGrammar`) | `VoiceRecognitionService` |
| `GeminiStreamClient.cs` | Multi-turn SSE streaming client (`system_instruction`, rolling history up to 20 turns / 40k chars, `temperature = 0.2`, `maxOutputTokens = 8192`, 20s connect + resettable 35s per-chunk inactivity timeout, failover only when 0 tokens received) | `GeminiStreamClient`, `SolveMode`, `StreamSolveScreenAsync`, `StreamSolveAudioAsync`, `StreamChatAsync` |
| `RepoContextService.cs` | Local repo indexer, test runner (`dotnet test`, `pytest`, `npm test`, `cargo test`, `go test`), stack-trace/file-line parser, and GitHub / `*.github.dev` Codespace remote file fetcher | `RepoContextService`, `RunTestsAsync`, `BuildRepoDebugContextAsync`, `TryParseGitHubRepo`, `IsBrowserWorkspaceUrl` |
| `NativeAutomation.cs` | Native process kill, `wlanapi.dll` Wi-Fi disconnect, volume/mute, safe clipboard, `Ctrl+V` paste, `Ctrl+A`+`Back`+`Ctrl+V` compiler replace, silent `cmd /c` | `NativeAutomation` |
| `Config.cs` | Persists `config.json` next to executable (`GEMINI_API_KEY` env fallback, `RepoPath`, `TestCommand`, `GitHubToken`) | `AppConfigData`, `ConfigManager` |
| `VerifyStealth.cs` | 8-point diagnostic suite (`--verify`) writing `verification_result.txt` | `VerifyStealth.RunVerification` |

---

## 3. Operating Modes & CLI Flags

`ctfmon.exe` (and `StealthScreenAI.exe`) supports:

| Mode / Flag | Behavior |
| :--- | :--- |
| *(no args)* | Runs the focus-immune monochrome glass HUD on the desktop with zero taskbar/tray presence. |
| `--verify` | Runs the 8-point `VerifyStealth.RunVerification()` self-test and logs to `verification_result.txt`. |
| `Launch_StealthScreen.bat` | Launches `StealthScreenAI.exe` or `ctfmon.exe` (with Release/Debug fallback). |

---

## 4. Controls, Hotkeys, Triggers & Solver Modes

### Global Hotkeys (`HotkeyWindow`)
- `F6` — Toggle Ghost Mode (`WS_EX_TRANSPARENT` + `0.50` opacity + `👻 GHOST` label)
- `F7` — Region Snip & Solve (`SnipOverlayWindow`)
- `F8` — Hide / Show Overlay
- `F9` — Solve Full Screen (`SolveMode.General`)
- `F10` — Open Chat Input Bar (`CommandBarWindow`)

### Passive Hookless Triggers (`AdvancedTriggerService`)
- **Mouse Velocity Shake** (≥ 4 horizontal reversals within 500 ms) → Toggle Hide / Show
- **Double-Tap `Ctrl`** → Toggle Hide / Show

### AI Solve Modes, Chat & Repo Bug Fixing
- **HUD Modes**: `[📸 Solve]` (`General`), `[💻 Code]` (`Coding` — auto-detects language), `[🔘 MCQ]` (`MCQ`), `[🎙️ Viva]` (10s WASAPI loopback), `[🐞 Fix]` (`RepoFix` — analyzes visible browser/Codespaces/local errors + linked repo context).
- **Persistent Chat & Memory**: Responses remain on screen until you start a new request or click `[🧹 New Chat]` (which clears both the display and `_history`). Clicking `[💬 Chat]`, the bottom prompt bar, or `F10` opens `CommandBarWindow` (with optional `📷 Screen` attach toggle).
- **Repo / Codespaces Linking**: Click `[📁 Repo]` (or type `repo <url|owner/repo|local-path>` / `repo clear` in chat) to link a local directory, GitHub repo (`https://github.com/owner/repo`), or Codespace (`*.github.dev`), or use **Browser Mode** directly from screen captures. Clicking `[🧪 Test]` runs tests in local repos and automatically diagnoses failures with exact file paths, line numbers, and code fixes.
- **Code Injection**: `[📋 Copy]` extracts the latest fenced code block; `[⚡ Paste]` restores `_lastActiveAppHwnd` and sends `Ctrl+V`; `[🔄 Replace]` sends `Ctrl+A` → `Backspace` → `Ctrl+V`.
- **Chat Shortcuts**: `code`, `mcq`, `solve`, `fix`, `viva`, `snip`, `copy`, `replace`, `paste`, `clear`/`new chat`, `hide`, `show`, `ghost`, `key`, `test`/`run tests`, `repo <target>`, natural-language system actions (`turn off wifi`, `close <app>`, `mute`, `volume up/down`), shell commands (`cmd`, `exec`, `run`, `powershell`, `ipconfig`, `tasklist`, etc.), or any free-form Gemini prompt.

---

## 5. Configuration & Gemini Model Pipeline

`ConfigManager` reads/writes `config.json` in `AppDomain.CurrentDomain.BaseDirectory`:

```json
{
  "GeminiApiKey": "",
  "GeminiModel": "gemini-flash-lite-latest",
  "Opacity": 0.88,
  "ExcludeFromCapture": true,
  "AlwaysOnTop": true,
  "ClickThrough": false,
  "RepoPath": "",
  "TestCommand": "",
  "GitHubToken": ""
}
```

- **API Key Fallback**: If `GeminiApiKey` is empty, `ConfigManager.Load()` reads `GEMINI_API_KEY` from environment variables.
- **Model Discovery & Streaming (`GeminiStreamClient`)**:
  - Queries `GET /v1beta/models?key=...` (cached 20 min) filtering for `generateContent` and excluding `-tts`, `embedding`, `aqa`, `lyria`, `robotics`, `transcribe`, `clip`.
  - Uses `Timeout.InfiniteTimeSpan` on `HttpClient`, a 20s connect/header timeout, and a resettable 35s per-chunk inactivity timeout so long streams are never cut off mid-response.
  - Fails over on HTTP `429`/`404`/`503`/timeout/0-token streams **only when 0 tokens have been streamed**; partial responses are preserved on screen.

---

## 6. Build, Publish & Verification Workflows

Run commands from `c:\Users\gugha\Downloads\solver-dist\StealthScreenAI` (using `$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe` if the global `dotnet` host has no SDK registered, with `--no-restore` when packages are already restored):

### Debug / Incremental Build
```powershell
dotnet build --configuration Debug --no-restore
```

### Standalone Self-Contained Single-File EXE (`win-x64`)
```powershell
dotnet publish -c Release -r win-x64 --self-contained true --no-restore `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:PublishTrimmed=false
```
Sync the published `ctfmon.exe` to `publish_single\ctfmon.exe`, `ctfmon.exe`, and `StealthScreenAI.exe` at the repository root.

### Automated Verification
```powershell
Start-Process -FilePath ".\publish_single\ctfmon.exe" -ArgumentList "--verify" -Wait -PassThru
```
Confirm exit code `0` and inspect `verification_result.txt` for all 8 subsystem checks (screen capture, Toolhelp32, `wlanapi.dll`, memory working set, WASAPI loopback, trigger engine, `WS_EX_NOACTIVATE` overlay creation, and `RepoContextService` + chat memory).

---

## 7. AI Model Checklist Before Completing Any Task

1. Did any new UI form/control accidentally omit `ShowInTaskbar = false`, `WS_EX_TOOLWINDOW`, `& ~WS_EX_APPWINDOW`, `HideFromTaskbarAndAppList`, or `ApplyCaptureExclusion`?
2. Does `StealthOverlayWindow` strictly preserve `WS_EX_NOACTIVATE`, `ShowWithoutActivation => true`, `WM_MOUSEACTIVATE => MA_NOACTIVATE`, and `WM_SETCURSOR => Cursors.Arrow` (with zero calls to `Activate()`/`Focus()`/`ForceForeground(Handle)` on itself)?
3. Is there zero `NotifyIcon` / system tray registration and zero `SetWindowsHookEx` hook?
4. Do all changes compile cleanly (`0 Warning(s)`, `0 Error(s)`), pass `--verify` (exit code `0`), and update `publish_single\ctfmon.exe`, `ctfmon.exe`, `StealthScreenAI.exe`, and `SKILL.md`?
