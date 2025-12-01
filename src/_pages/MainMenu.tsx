import React, { useState } from "react"

interface MainMenuProps {
  setView: (view: "main-menu" | "crackit" | "mcq" | "api-setup") => void
}

const MainMenu: React.FC<MainMenuProps> = ({ setView }) => {
  const [isLoading, setIsLoading] = useState(false)

  const handleCrackItMode = async () => {
    setIsLoading(true)
    try {
      ;(window as any).electronAPI?.send?.("mode-selected", "crackit")
      setView("crackit")
    } catch (error) {
      console.error("Error starting CrackIt mode:", error)
    } finally {
      setIsLoading(false)
    }
  }

  const handleMCQMode = async () => {
    setIsLoading(true)
    try {
      ;(window as any).electronAPI?.send?.("mode-selected", "mcq")
      setView("mcq")
    } catch (error) {
      console.error("Error starting MCQ mode:", error)
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="min-h-screen glass-container flex items-center justify-center p-4">
      <div className="w-full max-w-2xl">
        {/* Header */}
        <div className="mb-12 flex items-center justify-between">
          <div>
            <h1 className="glass-heading text-5xl mb-1">CrackIt</h1>
            <p className="glass-text-secondary text-lg">Interview Problem Solver & MCQ Analyzer</p>
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={() => setView("api-setup")}
              className="glass-button text-sm"
              title="Add or update API key"
            >
              🔑 API Key
            </button>
          </div>
        </div>

        {/* Mode Selection Cards */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          {/* CrackIt Mode Card */}
          <button
            onClick={handleCrackItMode}
            disabled={isLoading}
            className="glass-card p-8 rounded-lg transition-all duration-300 hover:shadow-lg disabled:opacity-50 text-left h-full"
          >
            <div className="flex flex-col items-center text-center">
              <div className="mb-4 text-5xl">💻</div>
              <h2 className="glass-heading text-2xl mb-2">CrackIt Mode</h2>
              <p className="glass-text-secondary mb-4 text-sm">
                Solve interview coding problems with AI-powered solutions
              </p>
              <ul className="text-xs glass-text-tertiary space-y-2 text-left w-full mb-6">
                <li>✓ Screenshot problem</li>
                <li>✓ Auto-generate solutions</li>
                <li>✓ Real-time debugging</li>
                <li>✓ Complexity analysis</li>
              </ul>
              <div className="glass-button w-full text-center font-semibold">
                {isLoading ? "Loading..." : "Start CrackIt Mode"}
              </div>
            </div>
          </button>

          {/* MCQ Mode Card */}
          <button
            onClick={handleMCQMode}
            disabled={isLoading}
            className="glass-card p-8 rounded-lg transition-all duration-300 hover:shadow-lg disabled:opacity-50 text-left h-full"
          >
            <div className="flex flex-col items-center text-center">
              <div className="mb-4 text-5xl">📝</div>
              <h2 className="glass-heading text-2xl mb-2">MCQ Analyzer</h2>
              <p className="glass-text-secondary mb-4 text-sm">
                Screenshot MCQ questions and get instant AI suggestions
              </p>
              <ul className="text-xs glass-text-tertiary space-y-2 text-left w-full mb-6">
                <li>✓ Auto-detect questions</li>
                <li>✓ Extract options</li>
                <li>✓ Single-letter answers</li>
                <li>✓ Instant suggestions</li>
              </ul>
              <div className="glass-button w-full text-center font-semibold">
                {isLoading ? "Loading..." : "Start MCQ Analyzer"}
              </div>
            </div>
          </button>
        </div>

        {/* Info Footer */}
        <div className="mt-12 glass-card text-center">
          <p className="glass-text-tertiary text-xs mb-2">⌨️ <strong>Hotkeys:</strong></p>
          <p className="glass-text-tertiary text-xs">
            Ctrl+M (Show) • Ctrl+N (Hide) • Ctrl+H (Screenshot) • Ctrl+J (MCQ)
          </p>
        </div>
      </div>
    </div>
  )
}

export default MainMenu