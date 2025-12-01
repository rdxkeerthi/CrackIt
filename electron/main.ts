import { app, BrowserWindow, Tray, Menu, nativeImage } from "electron"
import { initializeIpcHandlers } from "./ipcHandlers"
import { WindowHelper } from "./WindowHelper"
import { ScreenshotHelper } from "./ScreenshotHelper"
import { ShortcutsHelper } from "./shortcuts"
import { ProcessingHelper } from "./ProcessingHelper"

import * as fs from "fs"
import * as path from "path"

export interface HistoryItem {
  id: string
  timestamp: number
  type: "solution" | "chat"
  problemInfo?: any
  solution?: any
  screenshotPath?: string
  messages?: { role: "user" | "gemini"; text: string }[]
}

export class AppState {
  private static instance: AppState | null = null

  private windowHelper: WindowHelper
  private screenshotHelper: ScreenshotHelper
  public shortcutsHelper: ShortcutsHelper
  public processingHelper: ProcessingHelper
  private tray: Tray | null = null

  private history: HistoryItem[] = []
  private readonly HISTORY_FILE = "history.json"

  // View management
  private view: "queue" | "solutions" = "queue"

  private problemInfo: {
    problem_statement: string
    input_format: Record<string, any>
    output_format: Record<string, any>
    constraints: Array<Record<string, any>>
    test_cases: Array<Record<string, any>>
  } | null = null // Allow null

  private hasDebugged: boolean = false

  // Processing events
  public readonly PROCESSING_EVENTS = {
    //global states
    UNAUTHORIZED: "procesing-unauthorized",
    NO_SCREENSHOTS: "processing-no-screenshots",

    //states for generating the initial solution
    INITIAL_START: "initial-start",
    PROBLEM_EXTRACTED: "problem-extracted",
    SOLUTION_SUCCESS: "solution-success",
    INITIAL_SOLUTION_ERROR: "solution-error",

    //states for processing the debugging
    DEBUG_START: "debug-start",
    DEBUG_SUCCESS: "debug-success",
    DEBUG_ERROR: "debug-error"
  } as const

  constructor() {
    // Initialize WindowHelper with this
    this.windowHelper = new WindowHelper(this)

    // Initialize ScreenshotHelper
    this.screenshotHelper = new ScreenshotHelper(this.view)

    // Initialize ProcessingHelper
    this.processingHelper = new ProcessingHelper(this)

    // Initialize ShortcutsHelper
    this.shortcutsHelper = new ShortcutsHelper(this)

    this.loadHistory()
  }

  public static getInstance(): AppState {
    if (!AppState.instance) {
      AppState.instance = new AppState()
    }
    return AppState.instance
  }

  // Getters and Setters
  public getMainWindow(): BrowserWindow | null {
    return this.windowHelper.getMainWindow()
  }

  public getView(): "queue" | "solutions" {
    return this.view
  }

  public setView(view: "queue" | "solutions"): void {
    this.view = view
    this.screenshotHelper.setView(view)
  }

  public isVisible(): boolean {
    return this.windowHelper.isVisible()
  }

  public getScreenshotHelper(): ScreenshotHelper {
    return this.screenshotHelper
  }

  public getProblemInfo(): any {
    return this.problemInfo
  }

  public setProblemInfo(problemInfo: any): void {
    this.problemInfo = problemInfo
  }

  public getScreenshotQueue(): string[] {
    return this.screenshotHelper.getScreenshotQueue()
  }

  public getExtraScreenshotQueue(): string[] {
    return this.screenshotHelper.getExtraScreenshotQueue()
  }

  // Window management methods
  public createWindow(): void {
    this.windowHelper.createWindow()
  }

    public hideMainWindow(userInitiated: boolean = true): void {
    this.windowHelper.hideMainWindow(userInitiated)
  }

  public showMainWindow(): void {
    this.windowHelper.showMainWindow()
  }

  public toggleMainWindow(): void {
    console.log(
      "Screenshots: ",
      this.screenshotHelper.getScreenshotQueue().length,
      "Extra screenshots: ",
      this.screenshotHelper.getExtraScreenshotQueue().length
    )
    this.windowHelper.toggleMainWindow()
  }

  public setWindowDimensions(width: number, height: number): void {
    this.windowHelper.setWindowDimensions(width, height)
  }

  public clearQueues(): void {
    this.screenshotHelper.clearQueues()

    // Clear problem info
    this.problemInfo = null

    // Reset view to initial state
    this.setView("queue")
  }

  // Screenshot management methods
  public async takeScreenshot(): Promise<string> {
    if (!this.getMainWindow()) throw new Error("No main window available")

    // When taking a screenshot programmatically, don't trigger the overlay
    // which should only appear for user-initiated hides.
    const screenshotPath = await this.screenshotHelper.takeScreenshot(
      () => this.hideMainWindow(false),
      () => this.showMainWindow()
    )

    return screenshotPath
  }

  public async getImagePreview(filepath: string): Promise<string> {
    return this.screenshotHelper.getImagePreview(filepath)
  }

  public async deleteScreenshot(
    path: string
  ): Promise<{ success: boolean; error?: string }> {
    return this.screenshotHelper.deleteScreenshot(path)
  }

  // New methods to move the window
  public moveWindowLeft(): void {
    this.windowHelper.moveWindowLeft()
  }

  public moveWindowRight(): void {
    this.windowHelper.moveWindowRight()
  }
  public moveWindowDown(): void {
    this.windowHelper.moveWindowDown()
  }
  public moveWindowUp(): void {
    this.windowHelper.moveWindowUp()
  }

  public centerAndShowWindow(): void {
    this.windowHelper.centerAndShowWindow()
  }

  public createTray(): void {
    // Create a simple tray icon
    const image = nativeImage.createEmpty()

    // Try to use a system template image for better integration
    let trayImage = image
    try {
      // Create a minimal icon - just use an empty image and set the title
      trayImage = nativeImage.createFromBuffer(Buffer.alloc(0))
    } catch (error) {
      console.log("Using empty tray image")
      trayImage = nativeImage.createEmpty()
    }

    this.tray = new Tray(trayImage)

    const contextMenu = Menu.buildFromTemplate([
      {
        label: 'Show Interview Coder',
        click: () => {
          this.centerAndShowWindow()
        }
      },
      {
        label: 'Settings',
        click: () => {
          const mainWindow = this.getMainWindow()
          if (mainWindow) mainWindow.webContents.send('show-api-key-setup-dialog')
        }
      },
      {
        label: 'Toggle Window',
        click: () => {
          this.toggleMainWindow()
        }
      },
      {
        type: 'separator'
      },
      {
        label: 'Take Screenshot (Cmd+H)',
        click: async () => {
          try {
            const screenshotPath = await this.takeScreenshot()
            const preview = await this.getImagePreview(screenshotPath)
            const mainWindow = this.getMainWindow()
            if (mainWindow) {
              mainWindow.webContents.send("screenshot-taken", {
                path: screenshotPath,
                preview
              })
            }
          } catch (error) {
            console.error("Error taking screenshot from tray:", error)
          }
        }
      },
      {
        type: 'separator'
      },
      {
        label: 'Quit',
        accelerator: 'Command+Q',
        click: () => {
          app.quit()
        }
      }
    ])

    this.tray.setToolTip('Interview Coder - Press Cmd+Shift+Space to show')
    this.tray.setContextMenu(contextMenu)

    // Set a title for macOS (will appear in menu bar)
    if (process.platform === 'darwin') {
      this.tray.setTitle('IC')
    }

    // Double-click to show window
    this.tray.on('double-click', () => {
      this.centerAndShowWindow()
    })
  }

  public setHasDebugged(value: boolean): void {
    this.hasDebugged = value
  }

  public getHasDebugged(): boolean {
    return this.hasDebugged
  }

  // History Management
  private getHistoryPath(): string {
    const p = path.join(app.getPath("userData"), this.HISTORY_FILE)
    console.log("[AppState] History path:", p)
    return p
  }

  private loadHistory(): void {
    try {
      const historyPath = this.getHistoryPath()
      console.log("[AppState] Loading history from:", historyPath)
      if (fs.existsSync(historyPath)) {
        const data = fs.readFileSync(historyPath, "utf-8")
        this.history = JSON.parse(data)
        console.log(`[AppState] Loaded ${this.history.length} history items`)
      } else {
        console.log("[AppState] No history file found")
      }
    } catch (error) {
      console.error("[AppState] Error loading history:", error)
      this.history = []
    }
  }

  private saveHistory(): void {
    try {
      const historyPath = this.getHistoryPath()
      console.log("[AppState] Saving history to:", historyPath)
      console.log(`[AppState] Writing ${this.history.length} items to history file`)
      fs.writeFileSync(historyPath, JSON.stringify(this.history, null, 2))
      console.log("[AppState] History saved successfully")
    } catch (error) {
      console.error("[AppState] Error saving history:", error)
    }
  }

  public addToHistory(item: HistoryItem): void {
    console.log("[AppState] Adding/Updating item in history:", item.id)

    const existingIndex = this.history.findIndex(h => h.id === item.id)
    if (existingIndex !== -1) {
      // Update existing item
      this.history[existingIndex] = item
    } else {
      // Add new item
      this.history.unshift(item)
    }

    // Keep only last 50 items to prevent file from growing too large
    if (this.history.length > 50) {
      this.history = this.history.slice(0, 50)
    }
    this.saveHistory()
  }

  public getHistory(): HistoryItem[] {
    return this.history
  }

  public getHistoryItem(id: string): HistoryItem | undefined {
    return this.history.find(item => item.id === id)
  }

  // Stealth helpers (safe/no-op if modules absent)
  public enableStealthMode(): void {
    try {
      const { processObfuscator } = require('./stealth/process-obfuscator')
      const { stealthWindowManager } = (() => {
        try { return require('./stealth/window-manager') } catch { return {} }
      })()

      if (processObfuscator && typeof processObfuscator.setRandomInnocuousName === 'function') {
        processObfuscator.setRandomInnocuousName()
      }

      const mainWindow = this.getMainWindow()
      if (mainWindow && stealthWindowManager && typeof stealthWindowManager.makeWindowStealth === 'function') {
        stealthWindowManager.makeWindowStealth(mainWindow)
      }

      const floatingWindow = (typeof (global as any).floatingWindowManager !== 'undefined') ? (global as any).floatingWindowManager.getWindow?.() : null
      if (floatingWindow && stealthWindowManager && typeof stealthWindowManager.makeWindowStealth === 'function') {
        stealthWindowManager.makeWindowStealth(floatingWindow)
      }

      console.log('Stealth mode enabled (best-effort)')
    } catch (err) {
      console.warn('enableStealthMode failed (continuing):', err)
    }
  }

  public disableStealthMode(): void {
    try {
      const { processObfuscator } = require('./stealth/process-obfuscator')
      const { stealthWindowManager } = (() => {
        try { return require('./stealth/window-manager') } catch { return {} }
      })()

      if (processObfuscator && typeof processObfuscator.restore === 'function') {
        processObfuscator.restore()
      }

      const mainWindow = this.getMainWindow()
      if (mainWindow && stealthWindowManager && typeof stealthWindowManager.restoreWindow === 'function') {
        stealthWindowManager.restoreWindow(mainWindow, 'CrackIt')
      }

      console.log('Stealth mode disabled (best-effort)')
    } catch (err) {
      console.warn('disableStealthMode failed (continuing):', err)
    }
  }
}

// Application initialization
async function initializeApp() {
  const appState = AppState.getInstance()

  // Initialize IPC handlers before window creation
  initializeIpcHandlers(appState)
}

app.whenReady().then(() => {
  console.log("App is ready")
  const appState = AppState.getInstance()
  
  // Enable stealth mode at startup (obfuscate process and stealth windows)
  try {
    appState.enableStealthMode()
  } catch (err) {
    console.warn("Failed to enable stealth mode at startup:", err)
  }

  appState.createWindow()
  appState.createTray()
  // Register global shortcuts using ShortcutsHelper
  appState.shortcutsHelper.registerGlobalShortcuts()
})

app.on("activate", () => {
  console.log("App activated")
  const appState = AppState.getInstance()
  if (appState.getMainWindow() === null) {
    appState.createWindow()
  }
})

// Quit when all windows are closed, except on macOS
app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    app.quit()
  }
})

app.dock?.hide() // Hide dock icon (optional)
app.commandLine.appendSwitch("disable-background-timer-throttling")

// Start the application
initializeApp().catch(console.error)
