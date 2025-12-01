import { globalShortcut, app } from "electron"
import { AppState } from "./main" // Adjust the import path if necessary

export class ShortcutsHelper {
  private appState: AppState
  private registrations: Record<string, boolean> = {}

  constructor(appState: AppState) {
    this.appState = appState
  }

  public registerGlobalShortcuts(): void {
    const register = (accel: string, handler: (...args: any[]) => void) => {
      try {
        // globalShortcut.register may be typed as void in some Electron typings
        // so we simply call it and treat success as no exception thrown
        // and record the registration as true. If it throws, we record false.
        (globalShortcut.register as any)(accel, handler)
        this.registrations[accel] = true
        console.log(`[Shortcuts] Registered ${accel}`)
        return true
      } catch (err) {
        this.registrations[accel] = false
        console.error(`[Shortcuts] Failed to register ${accel}:`, err)
        return false
      }
    }

    // Add global shortcut to show/center window
    register("CommandOrControl+Shift+Space", () => {
      console.log("Show/Center window shortcut pressed...")
      this.appState.centerAndShowWindow()
    })

    register("CommandOrControl+H", async () => {
      const mainWindow = this.appState.getMainWindow()
      if (mainWindow) {
        console.log("Taking screenshot...")
        try {
          const screenshotPath = await this.appState.takeScreenshot()
          const preview = await this.appState.getImagePreview(screenshotPath)
          mainWindow.webContents.send("screenshot-taken", {
            path: screenshotPath,
            preview
          })
        } catch (error) {
          console.error("Error capturing screenshot:", error)
        }
      }
    })

    register("CommandOrControl+Enter", async () => {
      await this.appState.processingHelper.processScreenshots()
    })

    register("CommandOrControl+R", () => {
      console.log(
        "Command + R pressed. Canceling requests and resetting queues..."
      )

      // Cancel ongoing API requests
      this.appState.processingHelper.cancelOngoingRequests()

      // Clear both screenshot queues
      this.appState.clearQueues()

      console.log("Cleared queues.")

      // Update the view state to 'queue'
      this.appState.setView("queue")

      // Notify renderer process to switch view to 'queue'
      const mainWindow = this.appState.getMainWindow()
      if (mainWindow && !mainWindow.isDestroyed()) {
        mainWindow.webContents.send("reset-view")
      }
    })

    // New shortcuts for moving the window
    register("CommandOrControl+Left", () => {
      console.log("Command/Ctrl + Left pressed. Moving window left.")
      this.appState.moveWindowLeft()
    })

    register("CommandOrControl+Right", () => {
      console.log("Command/Ctrl + Right pressed. Moving window right.")
      this.appState.moveWindowRight()
    })
    register("CommandOrControl+Down", () => {
      console.log("Command/Ctrl + down pressed. Moving window down.")
      this.appState.moveWindowDown()
    })
    register("CommandOrControl+Up", () => {
      console.log("Command/Ctrl + Up pressed. Moving window Up.")
      this.appState.moveWindowUp()
    })

    register("CommandOrControl+B", () => {
      this.appState.toggleMainWindow()
      // If window exists and we're showing it, bring it to front
      const mainWindow = this.appState.getMainWindow()
      if (mainWindow && !this.appState.isVisible()) {
        // Force the window to the front on macOS
        if (process.platform === "darwin") {
          mainWindow.setAlwaysOnTop(true, "normal")
          // Reset alwaysOnTop after a brief delay
          setTimeout(() => {
            if (mainWindow && !mainWindow.isDestroyed()) {
              mainWindow.setAlwaysOnTop(true, "floating")
            }
          }, 100)
        }
      }
    })

    // SPECIAL CONTROL HOTKEYS
    // Ctrl+M: Show UI
    register("CommandOrControl+M", () => {
      console.log("Ctrl+M pressed. Showing UI...")
      try {
        this.appState.showMainWindow()
      } catch (err) {
        console.error("Failed to show main window via AppState:", err)
        const mainWindow = this.appState.getMainWindow()
        if (mainWindow) {
          try { mainWindow.show(); mainWindow.focus() } catch (_) {}
        }
      }
    })

    // Ctrl+N: Hide UI (user-initiated hide -> show overlay)
    register("CommandOrControl+N", () => {
      console.log("Ctrl+N pressed. Hiding UI (user-initiated)...")
      try {
        this.appState.hideMainWindow()
      } catch (err) {
        console.error("Failed to hide main window via AppState:", err)
        const mainWindow = this.appState.getMainWindow()
        if (mainWindow) {
          try { mainWindow.hide() } catch (_) {}
        }
      }
    })

    // Ctrl+J: Auto MCQ Analyzer (Start processing)
    register("CommandOrControl+J", async () => {
      console.log("Ctrl+J pressed. Starting auto MCQ analyzer...")
      const mainWindow = this.appState.getMainWindow()
      if (mainWindow) {
        try {
          // First, check if API key is configured
          const ipcMain = require('electron').ipcMain
          let hasApiKey = false
          try {
            const config = require('fs').existsSync(require('path').join(require('electron').app.getPath("userData"), "config.json"))
              ? JSON.parse(require('fs').readFileSync(require('path').join(require('electron').app.getPath("userData"), "config.json"), "utf-8"))
              : {}
            hasApiKey = !!(config.apiKeys && config.apiKeys.length > 0 && config.currentKeyId)
          } catch (e) {
            console.warn("Failed to check API key:", e)
          }

          if (!hasApiKey) {
            mainWindow.webContents.send("show-api-key-setup-dialog")
            return
          }

          // Show notification: questions copied, waiting for AI
          mainWindow.webContents.send('mcq-notification', { 
            status: 'copying',
            message: 'All questions are copied. Waiting for AI answer...'
          })

          // Automatically capture screen, extract MCQ, and request suggestion
          const screenshotPath = await this.appState.takeScreenshot()
          const llm = this.appState.processingHelper.getLLMHelper()
          if (!llm) {
            throw new Error('LLM not configured')
          }
          // Extract question + options from screenshot
          const mcq = await llm.extractMcqFromImages([screenshotPath])
          // Build a concise prompt to ask the model to pick a single letter and one-line reason
          const prompt = `Choose the single best answer (letter only) for the following question and provide a one-line explanation.\n\nQuestion:\n${mcq.question}\n\nOptions:\n${mcq.options.map((o: string, i: number) => `${String.fromCharCode(65 + i)}. ${o}`).join('\n')}\n\nReturn format: LETTER - one-line explanation.`
          const suggestion = await llm.chatWithGemini(prompt)
          // Extract just the letter from the suggestion (format: "A - explanation")
          const letterMatch = suggestion.match(/^([A-D])/i)
          const answerLetter = letterMatch ? letterMatch[1].toUpperCase() : suggestion.split(' ')[0]
          
          // Send suggestion back to renderer for UI display
          mainWindow.webContents.send('mcq-suggestion', { 
            screenshotPath, 
            question: mcq.question, 
            options: mcq.options, 
            suggestion: answerLetter,
            fullExplanation: suggestion
          })
        } catch (error) {
          console.error("MCQ analysis error:", error)
          mainWindow.webContents.send('mcq-notification', { 
            status: 'error',
            message: `Error: ${error.message}`
          })
        }
      }
    })

    // Ctrl+Y: Load next MCQ question
    register("CommandOrControl+Y", async () => {
      console.log("Ctrl+Y pressed. Loading next MCQ question...")
      const mainWindow = this.appState.getMainWindow()
      if (mainWindow) {
        try {
          // First, check if API key is configured
          let hasApiKey = false
          try {
            const config = require('fs').existsSync(require('path').join(require('electron').app.getPath("userData"), "config.json"))
              ? JSON.parse(require('fs').readFileSync(require('path').join(require('electron').app.getPath("userData"), "config.json"), "utf-8"))
              : {}
            hasApiKey = !!(config.apiKeys && config.apiKeys.length > 0 && config.currentKeyId)
          } catch (e) {
            console.warn("Failed to check API key:", e)
          }

          if (!hasApiKey) {
            mainWindow.webContents.send("show-api-key-setup-dialog")
            return
          }

          // Show notification: loading next question
          mainWindow.webContents.send('mcq-notification', { 
            status: 'copying',
            message: 'Loading next MCQ question...'
          })

          // Automatically capture screen and extract MCQ
          const screenshotPath = await this.appState.takeScreenshot()
          const llm = this.appState.processingHelper.getLLMHelper()
          if (!llm) {
            throw new Error('LLM not configured')
          }
          // Extract question + options from screenshot
          const mcq = await llm.extractMcqFromImages([screenshotPath])
          // Build a concise prompt to ask the model to pick a single letter and one-line reason
          const prompt = `Choose the single best answer (letter only) for the following question and provide a one-line explanation.\n\nQuestion:\n${mcq.question}\n\nOptions:\n${mcq.options.map((o: string, i: number) => `${String.fromCharCode(65 + i)}. ${o}`).join('\n')}\n\nReturn format: LETTER - one-line explanation.`
          const suggestion = await llm.chatWithGemini(prompt)
          // Extract just the letter from the suggestion (format: "A - explanation")
          const letterMatch = suggestion.match(/^([A-D])/i)
          const answerLetter = letterMatch ? letterMatch[1].toUpperCase() : suggestion.split(' ')[0]
          
          // Send suggestion back to renderer for UI display
          mainWindow.webContents.send('mcq-suggestion', { 
            screenshotPath, 
            question: mcq.question, 
            options: mcq.options, 
            suggestion: answerLetter,
            fullExplanation: suggestion
          })
        } catch (error) {
          console.error("MCQ loading error:", error)
          mainWindow.webContents.send('mcq-notification', { 
            status: 'error',
            message: `Error: ${error.message}`
          })
        }
      }
    })

    // Ctrl+Q: Quit App
    register("CommandOrControl+Q", () => {
      console.log("Ctrl+Q pressed. Quitting application...")
      app.quit()
    })

    // Unregister shortcuts when quitting
    app.on("will-quit", () => {
      globalShortcut.unregisterAll()
    })
  }

  public getRegistrationStatus(): Record<string, boolean> {
    return this.registrations
  }
}
