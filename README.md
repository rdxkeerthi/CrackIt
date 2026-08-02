# CrackIt (CrackCoder) — Codex Edition 🚀

> **Invisible, Stealthy AI-Powered Interview Assistant & Aptitude Solver**

CrackIt (Codex Edition) is a desktop AI assistant designed for live technical interviews, coding assessments, and aptitude tests. Running as a transparent floating window, it captures screen regions and system voice audio, utilizing Google's Gemini models to provide real-time solutions, live verbal interview scripts, and direct Q&A responses.

---

## 🌟 Key Features

### 🔒 1. Advanced Stealth Mode & Security
- **Screen-Capture Evasion**: Built-in hardware content protection (`setContentProtection(true)`) prevents proctoring software and screen recorders from capturing the app overlay.
- **Topmost Priority Overlay**: Runs with `screen-saver` level window priority to remain visible over full-screen applications.
- **Taskbar-Free Presentation**: Operates with `skipTaskbar: true` to avoid cluttering the OS taskbar.
- **No Hover Tooltips**: Clean button interface without hover tooltips or cursor popups.

### 🤖 2. Dynamic Gemini Multi-Model Failover
- **Resilient AI Pipeline**: Automatically iterates across active Gemini models (`gemini-2.5-flash`, `gemini-2.0-flash`, `gemini-3-flash-preview`, `gemini-2.0-flash-lite`, `gemini-1.5-flash-8b`, `gemini-2.5-pro`, `gemini-3-pro-preview`).
- **Extended Request Timeout**: 25-second execution timeout per model with automatic failover if rate limits or network delays occur.

### 🎙️ 3. Live Voice Input & Spoken Interview Scripting
- **Audio Capture**: Record live audio questions via the `MediaRecorder` Web API.
- **Spoken Answer Scripts**: Returns instant verbal response scripts (`🗣️ SPEAK THIS OUT LOUD TO INTERVIEWER`) along with key talking points to speak out loud during technical interviews.

### ⚙️ 4. 5 Specialized Operational Modes
Switch modes easily via settings, hotkey (`Ctrl + M`), or mode button:
1. **`AUTO` (Auto Detect)**: Automatically classifies inputs into Coding, MCQ, Interview Script, or Direct Q&A.
2. **`CODING` (Coding Only)**: Provides complete solution code, step-by-step approach, and Big-O Time/Space complexity analysis.
3. **`MCQ` (MCQ Only)**: Analyzes choices and highlights the correct option with detailed reasoning.
4. **`INTERVIEW` (Interview Script)**: Generates direct spoken answer scripts for verbal interview questions.
5. **`QA` (Q&A Direct Answer)**: Formats direct Question & Answer cards with a **📋 Copy Answer** button.

### 🌐 5. Preferred Language Tailoring
All code solutions, syntax references, explanations, and Q&A answers strictly adhere to your selected programming language (**Python**, **JavaScript**, **TypeScript**, **Java**, **C++**, **C**, **Go**, **Rust**).

### 📸 6. Multi-Screenshot Context Fusion
Capture up to 4 screenshots for a single problem (e.g. top description, code template, test cases). The AI automatically merges context across all images into a unified response.

---

## ⌨️ Shortcuts & Controls

### Interactive Buttons & Keyboard Shortcuts

| Shortcut | Button | Description |
| :--- | :--- | :--- |
| **`Ctrl/Cmd + H`** | 📸 **Shot** | Capture screen region into screenshot queue (max 4) |
| **`Ctrl/Cmd + Enter`** | 💡 **Solve** | Process queued screenshots with Gemini AI |
| **`—`** | 🎙️ **Voice** | Click to record live audio question |
| **`Ctrl/Cmd + R`** | 🔄 **Reset** | Clear screenshot and audio queue |
| **`Ctrl/Cmd + P`** | ⚙️ **Settings** | Open API Key and Language configuration modal |
| **`Ctrl/Cmd + B`** | — | **Stealth Toggle**: Instantly Hide or Show floating window |
| **`Ctrl/Cmd + M`** | — | **Cycle Mode**: Toggle between Auto / Coding / MCQ / Interview / QA |
| **`Ctrl/Cmd + L`** | — | **Cycle Language**: Toggle preferred programming language |
| **`Ctrl/Cmd + Arrows`** | — | Move overlay window around the screen |
| **`Ctrl/Cmd + Q`** | — | Quit application |

---

## 🛠️ Installation & Setup

### Prerequisites
- [Node.js](https://nodejs.org/) (v18 or higher)
- Free Google Gemini API Key from [Google AI Studio](https://aistudio.google.com/app/apikey)

### 1. Clone & Install
```bash
git clone https://github.com/rdxkeerthi/CrackIt.git
cd CrackIt
git checkout Codex
npm install
```

### 2. Configure API Key
1. Start the application:
   ```bash
   npm start
   ```
2. Press **`Ctrl/Cmd + P`** to open the Settings screen.
3. Enter your **Google Gemini API Key** and select your preferred language.
4. Click **Save Configuration**.

### 3. Build & Package

- **Development Watch Mode**:
  ```bash
  npm run dev
  ```
- **Compile Production Bundle**:
  ```bash
  npm run build
  ```
- **Package Windows Installer & Portable Executable**:
  ```bash
  npm run dist:win
  ```
  *Output executables will be generated in `release/CrackCoder Setup 1.0.0.exe` and `release/CrackCoder 1.0.0.exe`.*

---

## 📄 License

Distributed under the MIT License. See `LICENSE` for details.
