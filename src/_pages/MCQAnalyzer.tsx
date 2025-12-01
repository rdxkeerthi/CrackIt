import React, { useState, useEffect } from "react"

interface MCQAnalyzerProps {
  setView: (view: "main-menu" | "mcq") => void
}

const MCQAnalyzer: React.FC<MCQAnalyzerProps> = ({ setView }) => {
  const [mcqSuggestion, setMcqSuggestion] = useState<string | null>(null)
  const [fullExplanation, setFullExplanation] = useState<string | null>(null)
  const [isAutoMcq, setIsAutoMcq] = useState(false)
  const [loading, setLoading] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  // Listen for MCQ suggestions from Ctrl+J hotkey
  useEffect(() => {
    const handleMcqSuggestion = (data: any) => {
      console.log("MCQ Suggestion received:", data)
      if (data?.suggestion) {
        setMcqSuggestion(data.suggestion)
        setFullExplanation(data.fullExplanation || data.suggestion)
        setIsAutoMcq(true)
        setLoading(null)
        setError(null)
        // NO auto-dismiss - stay on page and wait for Ctrl+Y
      }
    }

    // Listen for MCQ notifications
    const handleMcqNotification = (data: any) => {
      console.log("MCQ Notification:", data)
      if (data?.status === 'copying') {
        setLoading(data.message)
        setMcqSuggestion(null)
        setFullExplanation(null)
        setError(null)
      } else if (data?.status === 'error') {
        setError(data.message)
        setLoading(null)
      }
    }

    const cleanupSuggestion = (window as any).electronAPI?.onMcqSuggestion?.(handleMcqSuggestion)
    const cleanupNotification = (window as any).electronAPI?.onMcqNotification?.(handleMcqNotification)
    
    return () => {
      cleanupSuggestion?.()
      cleanupNotification?.()
    }
  }, [setView])

  return (
    <div className="min-h-screen glass-container flex items-center justify-center p-4">
      <div className="w-full max-w-md">
        {/* Back Button */}
        <div className="mb-6 flex justify-between items-center">
          <h1 className="glass-heading text-2xl">MCQ Analyzer</h1>
          <button
            onClick={() => setView("main-menu")}
            className="glass-button text-sm"
          >
            ← Back
          </button>
        </div>

        {/* MCQ Suggestion Badge */}
        {loading && (
          <div className="glass-card text-center py-12">
            <p className="glass-text-secondary text-lg">{loading}</p>
          </div>
        )}

        {error && (
          <div className="glass-card text-center py-12 border border-red-400">
            <p className="glass-text-secondary text-lg">❌ {error}</p>
          </div>
        )}

        {mcqSuggestion && !loading && !error ? (
          <div className="glass-fade-in">
            <div className="glass-card text-center mb-6 p-8">
              <p className="glass-text-secondary text-sm mb-6">
                {isAutoMcq ? "AI Suggested Answer" : "AI Suggestion"}
              </p>
              
              {/* Answer Letter - Large Display */}
              <div className="text-7xl font-bold glass-text-primary mb-8">
                {mcqSuggestion.toUpperCase()}
              </div>

              {/* Full Explanation */}
              <div className="rounded-lg p-6 mb-6">
                <p className="glass-text-secondary text-left leading-relaxed whitespace-pre-wrap">
                  {fullExplanation}
                </p>
              </div>

              <p className="glass-text-tertiary text-sm">
                Press <strong>Ctrl+Y</strong> for next question
              </p>
            </div>

            <button
              onClick={() => {
                setMcqSuggestion(null)
                setFullExplanation(null)
                setIsAutoMcq(false)
                setView("main-menu")
              }}
              className="glass-button w-full text-center"
            >
              Back to Main Menu
            </button>
          </div>
        ) : !loading && !error ? (
          <div className="glass-card text-center py-16">
            <p className="glass-text-secondary text-lg mb-4">Press Ctrl+J</p>
            <p className="glass-text-tertiary text-sm mb-6">
              to capture screenshot and analyze MCQ
            </p>
          </div>
        ) : null}

        {/* Info Footer */}
        <div className="mt-8 glass-card text-center">
          <p className="glass-text-tertiary text-xs mb-2">⌨️ <strong>Hotkeys:</strong></p>
          <p className="glass-text-tertiary text-xs mb-1">
            <strong>Ctrl+J</strong> (Capture & Answer) • <strong>Ctrl+Y</strong> (Next Question)
          </p>
          <p className="glass-text-tertiary text-xs">
            <strong>Ctrl+M</strong> (Show App) • <strong>Ctrl+N</strong> (Hide/Overlay)
          </p>
        </div>
      </div>
    </div>
  )
}

export default MCQAnalyzer
