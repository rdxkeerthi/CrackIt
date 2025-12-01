// ipcHandlers.ts

import { ipcMain, app } from "electron"
import { AppState } from "./main"
import * as fs from "fs"
import * as path from "path"

export function initializeIpcHandlers(appState: AppState): void {
  ipcMain.handle(
    "update-content-dimensions",
    async (event, { width, height }: { width: number; height: number }) => {
      if (width && height) {
        appState.setWindowDimensions(width, height)
      }
    }
  )

  ipcMain.handle("delete-screenshot", async (event, path: string) => {
    return appState.deleteScreenshot(path)
  })

  ipcMain.handle("take-screenshot", async () => {
    try {
      const screenshotPath = await appState.takeScreenshot()
      const preview = await appState.getImagePreview(screenshotPath)
      return { path: screenshotPath, preview }
    } catch (error) {
      console.error("Error taking screenshot:", error)
      throw error
    }
  })

  ipcMain.handle("get-screenshots", async () => {
    console.log({ view: appState.getView() })
    try {
      let previews = []
      if (appState.getView() === "queue") {
        previews = await Promise.all(
          appState.getScreenshotQueue().map(async (path) => ({
            path,
            preview: await appState.getImagePreview(path)
          }))
        )
      } else {
        previews = await Promise.all(
          appState.getExtraScreenshotQueue().map(async (path) => ({
            path,
            preview: await appState.getImagePreview(path)
          }))
        )
      }
      previews.forEach((preview: any) => console.log(preview.path))
      return previews
    } catch (error) {
      console.error("Error getting screenshots:", error)
      throw error
    }
  })

  ipcMain.handle("toggle-window", async () => {
    appState.toggleMainWindow()
  })

  ipcMain.handle("reset-queues", async () => {
    try {
      appState.clearQueues()
      console.log("Screenshot queues have been cleared.")
      return { success: true }
    } catch (error: any) {
      console.error("Error resetting queues:", error)
      return { success: false, error: error.message }
    }
  })

  // IPC handler for analyzing audio from base64 data
  ipcMain.handle("analyze-audio-base64", async (event, data: string, mimeType: string) => {
    try {
      const result = await appState.processingHelper.processAudioBase64(data, mimeType)
      return result
    } catch (error: any) {
      console.error("Error in analyze-audio-base64 handler:", error)
      throw error
    }
  })

  // IPC handler for analyzing audio from file path
  ipcMain.handle("analyze-audio-file", async (event, path: string) => {
    try {
      const result = await appState.processingHelper.processAudioFile(path)
      return result
    } catch (error: any) {
      console.error("Error in analyze-audio-file handler:", error)
      throw error
    }
  })

  // IPC handler for analyzing image from file path
  ipcMain.handle("analyze-image-file", async (event, path: string) => {
    try {
      const result = await appState.processingHelper.getLLMHelper().analyzeImageFile(path)
      return result
    } catch (error: any) {
      console.error("Error in analyze-image-file handler:", error)
      throw error
    }
  })

  ipcMain.handle("gemini-chat", async (event, message: string) => {
    try {
      const result = await appState.processingHelper.getLLMHelper().chatWithGemini(message);
      return result;
    } catch (error: any) {
      console.error("Error in gemini-chat handler:", error);
      throw error;
    }
  });

  ipcMain.handle("quit-app", () => {
    app.quit()
  })

  // Window movement handlers
  ipcMain.handle("move-window-left", async () => {
    appState.moveWindowLeft()
  })

  ipcMain.handle("move-window-right", async () => {
    appState.moveWindowRight()
  })

  ipcMain.handle("move-window-up", async () => {
    appState.moveWindowUp()
  })

  ipcMain.handle("move-window-down", async () => {
    appState.moveWindowDown()
  })

  ipcMain.handle("center-and-show-window", async () => {
    appState.centerAndShowWindow()
  })

  // LLM Model Management Handlers
  ipcMain.handle("get-current-llm-config", async () => {
    try {
      const llmHelper = appState.processingHelper.getLLMHelper();
      return {
        provider: llmHelper.getCurrentProvider(),
        model: llmHelper.getCurrentModel(),
        isOllama: llmHelper.isUsingOllama()
      };
    } catch (error: any) {
      console.error("Error getting current LLM config:", error);
      throw error;
    }
  });

  ipcMain.handle("get-available-ollama-models", async () => {
    try {
      const llmHelper = appState.processingHelper.getLLMHelper();
      const models = await llmHelper.getOllamaModels();
      return models;
    } catch (error: any) {
      console.error("Error getting Ollama models:", error);
      throw error;
    }
  });

  ipcMain.handle("switch-to-ollama", async (_, model?: string, url?: string) => {
    try {
      const llmHelper = appState.processingHelper.getLLMHelper();
      await llmHelper.switchToOllama(model, url);
      return { success: true };
    } catch (error: any) {
      console.error("Error switching to Ollama:", error);
      return { success: false, error: error.message };
    }
  });

  ipcMain.handle("switch-to-gemini", async (_, apiKey?: string) => {
    try {
      const llmHelper = appState.processingHelper.getLLMHelper();
      await llmHelper.switchToGemini(apiKey);
      return { success: true };
    } catch (error: any) {
      console.error("Error switching to Gemini:", error);
      return { success: false, error: error.message };
    }
  });

  ipcMain.handle("test-llm-connection", async () => {
    try {
      const llmHelper = appState.processingHelper.getLLMHelper();
      const result = await llmHelper.testConnection();
      return result;
    } catch (error: any) {
      console.error("Error testing LLM connection:", error);
      return { success: false, error: error.message };
    }
  });

  // History Handlers
  ipcMain.handle("get-history", async () => {
    return appState.getHistory();
  });

  ipcMain.handle("load-history-item", async (event, id: string) => {
    const item = appState.getHistoryItem(id);
    if (item) {
      if (item.type === "chat") {
        appState.setView("queue");
        const mainWindow = appState.getMainWindow();
        if (mainWindow) {
          mainWindow.webContents.send("restore-chat", item.messages);
        }
      } else {
        appState.setProblemInfo(item.problemInfo);
        appState.setView("solutions");

        const mainWindow = appState.getMainWindow();
        if (mainWindow) {
          mainWindow.webContents.send(appState.PROCESSING_EVENTS.PROBLEM_EXTRACTED, item.problemInfo);
          mainWindow.webContents.send(appState.PROCESSING_EVENTS.SOLUTION_SUCCESS, { solution: item.solution });
        }
      }
      return { success: true };
    }
    return { success: false, error: "Item not found" };
  });

  ipcMain.handle("save-history-item", async (event, item: any) => {
    try {
      appState.addToHistory(item);
      return { success: true };
    } catch (error: any) {
      console.error("Error saving history item:", error);
      return { success: false, error: error.message };
    }
  });
  // ---- API Key storage & management ----
  const configPath = path.join(app.getPath("userData"), "config.json")

  function loadConfig(): any {
    try {
      if (fs.existsSync(configPath)) {
        const raw = fs.readFileSync(configPath, "utf-8")
        return JSON.parse(raw)
      }
    } catch (err) {
      console.error("Error loading config:", err)
    }
    return { apiKeys: [], currentKeyId: null }
  }

  function saveConfig(cfg: any) {
    try {
      fs.writeFileSync(configPath, JSON.stringify(cfg, null, 2), "utf-8")
      return true
    } catch (err) {
      console.error("Error saving config:", err)
      return false
    }
  }

  ipcMain.handle("get-api-keys", async () => {
    const cfg = loadConfig()
    return { apiKeys: cfg.apiKeys || [], currentKeyId: cfg.currentKeyId || null }
  })

  ipcMain.handle("save-api-key", async (event, keyEntry: { id?: string; provider: string; key: string; label?: string }) => {
    const cfg = loadConfig()
    cfg.apiKeys = cfg.apiKeys || []
    // If id provided, update
    if (keyEntry.id) {
      const idx = cfg.apiKeys.findIndex((k: any) => k.id === keyEntry.id)
      if (idx !== -1) {
        cfg.apiKeys[idx] = { ...cfg.apiKeys[idx], ...keyEntry }
      } else {
        cfg.apiKeys.push({ ...keyEntry })
      }
    } else {
      // create id
      const id = Date.now().toString(36) + Math.random().toString(36).slice(2, 8)
      cfg.apiKeys.push({ ...keyEntry, id })
      cfg.currentKeyId = id
    }
    const ok = saveConfig(cfg)
    return { success: ok, apiKeys: cfg.apiKeys, currentKeyId: cfg.currentKeyId }
  })

  ipcMain.handle("delete-api-key", async (event, id: string) => {
    const cfg = loadConfig()
    cfg.apiKeys = (cfg.apiKeys || []).filter((k: any) => k.id !== id)
    if (cfg.currentKeyId === id) cfg.currentKeyId = cfg.apiKeys.length ? cfg.apiKeys[0].id : null
    const ok = saveConfig(cfg)
    return { success: ok, apiKeys: cfg.apiKeys, currentKeyId: cfg.currentKeyId }
  })

  ipcMain.handle("get-current-api-key", async () => {
    const cfg = loadConfig()
    const current = (cfg.apiKeys || []).find((k: any) => k.id === cfg.currentKeyId) || null
    return { current }
  })

  ipcMain.handle("set-current-api-key", async (event, id: string) => {
    const cfg = loadConfig()
    cfg.currentKeyId = id
    const ok = saveConfig(cfg)
    return { success: ok, currentKeyId: cfg.currentKeyId }
  })
  
  // Answer MCQ: given problem text and options, ask LLM to pick best option and explain briefly
  ipcMain.handle("answer-mcq", async (event, { problem, options }: { problem: string; options: string[] }) => {
    try {
      const llm = appState.processingHelper.getLLMHelper()
      if (!llm) {
        return { success: false, error: 'No LLM configured' }
      }

      const prompt = `You are an expert test-taking assistant. Given the following multiple choice question, choose the single best answer and provide a one-line explanation.\n\nQuestion:\n${problem}\n\nOptions:\n${options.map((o, i) => `${String.fromCharCode(65 + i)}. ${o}`).join('\n')}\n\nReturn your answer in the format: LETTER - brief explanation. Do NOT provide code or attempt to submit the answer.`

      const response = await llm.chatWithGemini(prompt)
      return { success: true, text: response }
    } catch (err: any) {
      console.error('Error answering MCQ:', err)
      return { success: false, error: err.message || String(err) }
    }
  })

  // Hotkeys status
  ipcMain.handle("get-hotkeys-status", async () => {
    try {
      const shortcuts = (appState as any).shortcutsHelper
      if (shortcuts && typeof shortcuts.getRegistrationStatus === 'function') {
        return { success: true, registrations: shortcuts.getRegistrationStatus() }
      }
      return { success: false, error: 'Shortcuts helper not available' }
    } catch (err: any) {
      console.error('Error getting hotkeys status:', err)
      return { success: false, error: err.message }
    }
  })
  ipcMain.handle("summarize-meeting", async (event, audioBase64: string) => {
    try {
      return await appState.processingHelper.getLLMHelper().summarizeMeeting(audioBase64)
    } catch (error) {
      console.error("Error in summarize-meeting handler:", error)
      return "Error processing meeting audio."
    }
  })

  // API Key Startup Check
  ipcMain.handle("check-api-key-on-startup", async () => {
    try {
      // First check stored config
      const cfg = loadConfig()
      if (cfg && cfg.currentKeyId) {
        return { hasKey: true }
      }
      const hasApiKey = process.env.GEMINI_API_KEY || process.env.OPENAI_API_KEY
      return { hasKey: !!hasApiKey }
    } catch (error: any) {
      console.error("Error checking API key:", error)
      return { hasKey: false }
    }
  })

  ipcMain.handle("prompt-api-key-setup", async () => {
    const mainWindow = appState.getMainWindow()
    if (mainWindow) {
      mainWindow.webContents.send("show-api-key-setup-dialog")
    }
    return { status: "prompted" }
  })

  // Show main window on request (renderer can call this after onboarding)
  ipcMain.handle("show-main-window", async () => {
    try {
      appState.showMainWindow()
      return { success: true }
    } catch (err: any) {
      console.error('Error showing main window via IPC:', err)
      return { success: false, error: String(err) }
    }
  })
}

