import { ToastProvider } from "./components/ui/toast"
import Queue from "./_pages/Queue"
import { ToastViewport } from "@radix-ui/react-toast"
import { useEffect, useRef, useState } from "react"
import Solutions from "./_pages/Solutions"
import History from "./_pages/History"
import MeetingNotes from "./_pages/MeetingNotes"
import ApiSetup from "./_pages/ApiSetup"
import MainMenu from "./_pages/MainMenu"
import MCQAnalyzer from "./_pages/MCQAnalyzer"
import { QueryClient, QueryClientProvider } from "react-query"

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: Infinity,
      cacheTime: Infinity
    }
  }
})

const App: React.FC = () => {
  const [view, setView] = useState<"main-menu" | "queue" | "solutions" | "debug" | "history" | "meeting-notes" | "api-setup" | "crackit" | "mcq">("api-setup")
  const [showApiSetup, setShowApiSetup] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  // Check API key on startup
  useEffect(() => {
    const checkApiKeyOnStartup = async () => {
      try {
        const result = await (window.electronAPI as any).invoke("check-api-key-on-startup")
        if (!result.hasKey) {
          console.log("No API key found. Prompting user to set up...")
          setShowApiSetup(true)
          setView("api-setup")
        } else {
          // API key exists, show main menu
          setView("main-menu")
        }
      } catch (error) {
        console.error("Error checking API key:", error)
        // On error, show main menu anyway
        setView("main-menu")
      }
    }
    
    checkApiKeyOnStartup()

    const cleanup = window.electronAPI.onResetView(() => {
      console.log("Received 'reset-view' message from main process.")
      queryClient.invalidateQueries(["screenshots"])
      queryClient.invalidateQueries(["problem_statement"])
      queryClient.invalidateQueries(["solution"])
      queryClient.invalidateQueries(["new_solution"])
      setView("main-menu")
    })

    const cleanupShow = (window.electronAPI as any).onShowApiKeySetupDialog(() => {
      console.log("Main requested API key setup dialog")
      setShowApiSetup(true)
      setView("api-setup")
    })

    return () => {
      try { cleanup() } catch (e) {}
      try { cleanupShow() } catch (e) {}
    }
  }, [])

  useEffect(() => {
    if (!containerRef.current) return

    const updateHeight = () => {
      if (!containerRef.current) return
      const height = containerRef.current.scrollHeight
      const width = containerRef.current.scrollWidth
      window.electronAPI?.updateContentDimensions({ width, height })
    }

    const resizeObserver = new ResizeObserver(() => {
      updateHeight()
    })

    // Initial height update
    updateHeight()

    // Observe for changes
    resizeObserver.observe(containerRef.current)

    // Also update height when view changes
    const mutationObserver = new MutationObserver(() => {
      updateHeight()
    })

    mutationObserver.observe(containerRef.current, {
      childList: true,
      subtree: true,
      attributes: true,
      characterData: true
    })

    return () => {
      resizeObserver.disconnect()
      mutationObserver.disconnect()
    }
  }, [view]) // Re-run when view changes

  useEffect(() => {
    const cleanupFunctions = [
      window.electronAPI.onSolutionStart(() => {
        setView("solutions")
        console.log("starting processing")
      }),

      window.electronAPI.onUnauthorized(() => {
        queryClient.removeQueries(["screenshots"])
        queryClient.removeQueries(["solution"])
        queryClient.removeQueries(["problem_statement"])
        setView("queue")
        console.log("Unauthorized")
      }),
      // Update this reset handler
      window.electronAPI.onResetView(() => {
        console.log("Received 'reset-view' message from main process")

        queryClient.removeQueries(["screenshots"])
        queryClient.removeQueries(["solution"])
        queryClient.removeQueries(["problem_statement"])
        setView("main-menu")
        console.log("View reset to 'main-menu' via Command+R shortcut")
      }),
      window.electronAPI.onProblemExtracted((data: any) => {
        console.log("Problem extracted successfully")
        queryClient.invalidateQueries(["problem_statement"])
        queryClient.setQueryData(["problem_statement"], data)
      }),
      // Handle history restoration events
      window.electronAPI.onRestoreChat((messages) => {
        console.log("Restoring chat session...")
        queryClient.setQueryData(["restored_chat"], messages)
        setView("queue")
      }),
      window.electronAPI.onSolutionSuccess((data) => {
        // If we receive a solution success event, ensure we are in the solutions view
        // This handles both new solutions (redundant but safe) and history restoration
        console.log("Solution loaded/generated, switching to solutions view")

        if (data?.solution) {
          const solutionData = {
            code: data.solution.code,
            codes: data.solution.codes,
            thoughts: data.solution.thoughts,
            time_complexity: data.solution.time_complexity,
            space_complexity: data.solution.space_complexity
          }
          queryClient.setQueryData(["solution"], solutionData)
        }

        setView("solutions")
      })
    ]
    return () => cleanupFunctions.forEach((cleanup) => cleanup())
  }, [view]) // Added view dependency to ensure closure captures current view if needed, though mostly stateless here

  return (
    <div ref={containerRef} className="max-h-screen overflow-y-auto">
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          {view === "api-setup" ? (
            <ApiSetup onDone={() => setView("main-menu")} />
          ) : view === "main-menu" ? (
            <MainMenu setView={setView} />
          ) : view === "crackit" || view === "queue" ? (
            <Queue setView={setView} />
          ) : view === "solutions" ? (
            <Solutions setView={setView} />
          ) : view === "history" ? (
            <History setView={setView} />
          ) : view === "meeting-notes" ? (
            <MeetingNotes setView={setView} />
          ) : view === "mcq" ? (
            <MCQAnalyzer setView={setView} />
          ) : (
            <></>
          )}
          <ToastViewport />
        </ToastProvider>
      </QueryClientProvider>
    </div>
  )
}

export default App
