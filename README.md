# CrackIt (CrackCoder) - Invisible AI-Powered Interview Assistant

A powerful, stealthy AI tool for solving Coding questions and MCQs during technical interviews. The tool runs 100% undetectably in the background using the latest **Google Gemini 3 (2026)** models.

**Repository**: [https://github.com/rdxkeerthi/CrackIt](https://github.com/rdxkeerthi/CrackIt)

## 🚀 Key Features

- **🔒 Stealth Mode**: Completely invisible. Toggle visibility instantly with `Ctrl + B`.
- **🧠 Advanced AI**: Powered by **Gemini 3 Flash & Pro (2026)** for superior coding and logic.
- **📝 Multi-Mode**:
  - **Coding Mode**: Solves algorithmic problems with time/space complexity.
  - **MCQ Mode**: Solves multiple-choice questions with explanations.
- **⚡ Fast & Responsive**: Optimized for speed with keyboard shortcuts.
- **🛠️ Easy Config**: Configure your API key directly in the app (`Ctrl + P`). No restarting required.

## 📥 Installation

1. **Clone the repository**:
   ```bash
   git clone https://github.com/rdxkeerthi/CrackIt.git
   cd CrackIt
   ```

2. **Install dependencies**:
   ```bash
   npm install
   ```

3. **Build the project**:
   ```bash
   npm run build
   ```

4. **Start the application**:
   ```bash
   npm start
   ```

## ⚙️ Configuration

1. Launch the app (`npm start`).
2. Press **`Ctrl/Cmd + P`** to open the Settings screen.
3. Enter your **Google Gemini API Key**.
   - Get a key here: [Google AI Studio](https://aistudio.google.com/app/apikey) (It's free!)
4. Click **Save**. The app will validate your key and auto-close the settings.

> **Note**: The app automatically selects the best available model (Gemini 3 Flash/Pro) based on your key limits.

## ⌨️ Shortcuts

| Shortcut | Action |
|----------|--------|
| **`Ctrl/Cmd + H`** | **Capture Screenshot** (Analyze current screen) |
| **`Ctrl/Cmd + Enter`** | **View Solution** (Show AI output) |
| **`Ctrl/Cmd + B`** | **Stealth Mode** (Instantly Hide/Show App) |
| **`Ctrl/Cmd + P`** | **Settings** (API Key & Language) |
| **`Ctrl/Cmd + M`** | **Cycle Mode** (Coding / MCQ) |
| **`Ctrl/Cmd + L`** | **Cycle Language** (Python / Java / C++ / JS) |
| **`Ctrl/Cmd + R`** | **Reset** |
| **`Ctrl/Cmd + Q`** | **Quit** |
| **`Ctrl/Cmd + Arrows`** | **Move Window** |

## 🤖 Models Used

This project uses the latest 2026 Gemini models:
- **`gemini-3-flash-preview`**: Ultra-fast, optimized for coding and standardized tests.
- **`gemini-2.5-flash`**: Stable, production-ready fallback.
- **`gemini-3-pro-preview`**: High-reasoning model for complex architecture questions.

## 🤝 Contributing

Contributions are welcome!
1. Fork the repo
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

Distributed under the MIT License. See `LICENSE` for more information.
