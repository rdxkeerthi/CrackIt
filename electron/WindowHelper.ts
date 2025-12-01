
import { BrowserWindow, screen, app } from "electron"
import { AppState } from "main"
import path from "node:path"

const isDev = process.env.NODE_ENV === "development"

function getStartUrl(): string {
  if (isDev) {
    return "http://localhost:5180"
  }
  
  // In production, app.getAppPath() returns the resources/app folder
  // Files are packaged at the root: dist/ and dist-electron/
  const appPath = app.getAppPath()
  const indexPath = path.join(appPath, 'dist', 'index.html')
  console.log("Production mode - Loading from:", indexPath)
  return `file://${indexPath}`
}

export class WindowHelper {
  private mainWindow: BrowserWindow | null = null
  private overlayWindow: BrowserWindow | null = null
  private isWindowVisible: boolean = false
  private started: boolean = false
  private windowPosition: { x: number; y: number } | null = null
  private windowSize: { width: number; height: number } | null = null
  private appState: AppState

  // Initialize with explicit number type and 0 value
  private screenWidth: number = 0
  private screenHeight: number = 0
  private step: number = 0
  private currentX: number = 0
  private currentY: number = 0

  constructor(appState: AppState) {
    this.appState = appState
  }

  public setWindowDimensions(width: number, height: number): void {
    if (!this.mainWindow || this.mainWindow.isDestroyed()) return

    // Get current window position
    const [currentX, currentY] = this.mainWindow.getPosition()

    // Get screen dimensions
    const primaryDisplay = screen.getPrimaryDisplay()
    const workArea = primaryDisplay.workAreaSize

    // Use 75% width if debugging has occurred, otherwise use 60%
    const maxAllowedWidth = Math.floor(
      workArea.width * (this.appState.getHasDebugged() ? 0.75 : 0.5)
    )

    // Ensure width doesn't exceed max allowed width and height is reasonable
    const newWidth = Math.min(width + 32, maxAllowedWidth)
    const newHeight = Math.ceil(height)

    // Center the window horizontally if it would go off screen
    const maxX = workArea.width - newWidth
    const newX = Math.min(Math.max(currentX, 0), maxX)

    // Update window bounds
    this.mainWindow.setBounds({
      x: newX,
      y: currentY,
      width: newWidth,
      height: newHeight
    })

    // Update internal state
    this.windowPosition = { x: newX, y: currentY }
    this.windowSize = { width: newWidth, height: newHeight }
    this.currentX = newX
  }

  public createWindow(): void {
    if (this.mainWindow !== null) return

    const primaryDisplay = screen.getPrimaryDisplay()
    const workArea = primaryDisplay.workAreaSize
    this.screenWidth = workArea.width
    this.screenHeight = workArea.height


    const windowSettings: Electron.BrowserWindowConstructorOptions = {
      width: 400,
      height: 600,
      minWidth: 300,
      minHeight: 200,
      maxHeight: 800, // Add max height so content becomes scrollable
      webPreferences: {
        nodeIntegration: true,
        contextIsolation: true,
        preload: isDev
          ? path.join(__dirname, "preload.js")
          : path.join(app.getAppPath(), "dist-electron", "preload.js")
      },
      show: false,
      alwaysOnTop: true,
      frame: false,
      transparent: true,
      fullscreenable: false,
      hasShadow: false,
      backgroundColor: "#00000000",
      focusable: true,
      resizable: true,
      movable: true,
      x: 100, // Start at a visible position
      y: 100,
      // start fully opaque so UI is visible immediately
      type: "panel", // Critical for macOS overlay
      enableLargerThanScreen: true,
      paintWhenInitiallyHidden: true,
      skipTaskbar: false,
    }


    this.mainWindow = new BrowserWindow(windowSettings)

    // Reset zoom level to default (fixes Ctrl+- issue)
    this.mainWindow.webContents.setZoomFactor(1.0)

    // this.mainWindow.webContents.openDevTools()

    if (process.platform === "darwin") {
      this.mainWindow.setVisibleOnAllWorkspaces(true, {
        visibleOnFullScreen: true
      })
      this.mainWindow.setHiddenInMissionControl(true)
      this.mainWindow.setAlwaysOnTop(true, "screen-saver", 1)
    }
    if (process.platform === "linux") {
      // Linux-specific optimizations for better compatibility
      if (this.mainWindow.setHasShadow) {
        this.mainWindow.setHasShadow(false)
      }
      // Keep window focusable on Linux for proper interaction
      this.mainWindow.setFocusable(true)
    }

    // Windows-specific: Apply content protection for screen share exclusion
    // Apply globally for all platforms where supported, but definitely for Windows
    this.mainWindow.setContentProtection(true)
    if (process.platform === "win32") {
      console.log("👻 Windows: Content protection enabled (WDA_EXCLUDEFROMCAPTURE)")
    }

    // Prevent the window from being captured by screen recording
    this.mainWindow.webContents.setBackgroundThrottling(false)
    this.mainWindow.webContents.setFrameRate(60)

    this.mainWindow.setSkipTaskbar(false)
    this.mainWindow.setAlwaysOnTop(true)

    const startUrl = getStartUrl()
    this.mainWindow.loadURL(startUrl).catch((err) => {
      console.error("Failed to load URL:", err)
      console.error("Attempted URL:", startUrl)
    })

    // Show window after loading URL and center it
    this.mainWindow.once('ready-to-show', () => {
      if (this.mainWindow) {
        // Reset zoom factor to ensure proper display
        this.mainWindow.webContents.setZoomFactor(1.0)

        // Re-apply content protection after window is ready (fixes transparent window issue)
        this.mainWindow.setContentProtection(true)
        if (process.platform === "win32") {
          console.log("👻 Windows: Content protection re-applied after ready-to-show")
        }

        // Center the window and show it normally
        this.centerWindow()
        this.mainWindow.setIgnoreMouseEvents(false)
        try {
          this.mainWindow.show()
        } catch (e) {
          // fallback to showInactive if show fails in some environments
          try { this.mainWindow.showInactive() } catch (_) {}
        }
        this.mainWindow.setAlwaysOnTop(true, "screen-saver", 1)
        console.log("Window is now visible and centered (zoom reset)")
        // mark helper as fully started so overlay will only appear after user hides
        this.started = true
      }
    })

    const bounds = this.mainWindow.getBounds()
    this.windowPosition = { x: bounds.x, y: bounds.y }
    this.windowSize = { width: bounds.width, height: bounds.height }
    this.currentX = bounds.x
    this.currentY = bounds.y

    this.setupWindowListeners()
    this.isWindowVisible = true
  }

  private setupWindowListeners(): void {
    if (!this.mainWindow) return

    this.mainWindow.on("move", () => {
      if (this.mainWindow) {
        const bounds = this.mainWindow.getBounds()
        this.windowPosition = { x: bounds.x, y: bounds.y }
        this.currentX = bounds.x
        this.currentY = bounds.y
      }
    })

    this.mainWindow.on("resize", () => {
      if (this.mainWindow) {
        const bounds = this.mainWindow.getBounds()
        this.windowSize = { width: bounds.width, height: bounds.height }
      }
    })

    this.mainWindow.on("closed", () => {
      this.mainWindow = null
      this.isWindowVisible = false
      this.windowPosition = null
      this.windowSize = null
    })

    // Disable default zoom shortcuts
    this.mainWindow.webContents.on("before-input-event", (event, input) => {
      if (input.control || input.meta) {
        if (input.key === "+" || input.key === "-" || input.key === "=" || input.key === "0") {
          event.preventDefault()
        }
      }
    })
  }

  public getMainWindow(): BrowserWindow | null {
    return this.mainWindow
  }

  public isVisible(): boolean {
    return this.isWindowVisible
  }

  public hideMainWindow(userInitiated: boolean = true): void {
    if (!this.mainWindow || this.mainWindow.isDestroyed()) {
      console.warn("Main window does not exist or is destroyed.")
      return
    }

    const bounds = this.mainWindow.getBounds()
    this.windowPosition = { x: bounds.x, y: bounds.y }
    this.windowSize = { width: bounds.width, height: bounds.height }

    // Hide the main window and show a small overlay badge so user can restore it
    try {
      this.mainWindow.hide()
    } catch (e) {
      try { this.mainWindow.setOpacity(0); } catch (_) {}
    }
    this.isWindowVisible = false

    // create overlay badge if not already present and only after startup and user-initiated hides
    if (!this.overlayWindow && this.started && userInitiated) {
      const { BrowserWindow } = require('electron')
      const overlayHtml = `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:;">
        <style>
          html,body{margin:0;padding:0;background:transparent;overflow:hidden}
          /* Small draggable badge (27x20) */
          .badge{width:27px;height:20px;border-radius:6px;display:flex;align-items:center;justify-content:center;font-size:12px;color:rgba(20,20,20,0.92);backdrop-filter: blur(8px) saturate(1.0);background:rgba(255,255,255,0.06);border:1px solid rgba(0,0,0,0.08);box-shadow:0 6px 18px rgba(0,0,0,0.45);-webkit-app-region: drag}
          .click{width:100%;height:100%;display:flex;align-items:center;justify-content:center;-webkit-app-region: no-drag;cursor:pointer}
          svg.icon{width:18px;height:14px;display:block}
        </style></head><body><div class="badge" id="badge"><div class="click" id="click"><svg class="icon" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2l2.9 6.3L22 9.3l-5 4.6L18 22l-6-3.4L6 22l1-8.1L2 9.3l7.1-1L12 2z"/></svg></div></div><script>document.getElementById('click').addEventListener('click',()=>{window.close()});</script></body></html>`

      const ow = 27
      const oh = 20
      this.overlayWindow = new BrowserWindow({
        width: ow,
        height: oh,
        x: Math.round(this.screenWidth - ow - 24),
        y: Math.round(this.screenHeight - oh - 80),
        frame: false,
        transparent: true,
        resizable: false,
        movable: true,
        alwaysOnTop: true,
        focusable: true,
        skipTaskbar: true,
        webPreferences: { nodeIntegration: true, contextIsolation: false }
      })

      this.overlayWindow.setAlwaysOnTop(true, 'floating')
      this.overlayWindow.setIgnoreMouseEvents(false)
      // Load inline HTML
      this.overlayWindow.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(overlayHtml)}`)

      // When overlay closed (clicked), show main window
      this.overlayWindow.on('closed', () => {
        this.overlayWindow = null
        try {
          this.showMainWindow()
        } catch (e) {
          // ignore
        }
      })
    }
    console.log('Main window hidden; overlay badge shown')
  }

  public showMainWindow(): void {
    if (!this.mainWindow || this.mainWindow.isDestroyed()) {
      console.warn("Main window does not exist or is destroyed.")
      return
    }

    if (this.windowPosition && this.windowSize) {
      this.mainWindow.setBounds({
        x: this.windowPosition.x,
        y: this.windowPosition.y,
        width: this.windowSize.width,
        height: this.windowSize.height
      })
    }
    // Close overlay if present
    try {
      if (this.overlayWindow && !this.overlayWindow.isDestroyed()) {
        this.overlayWindow.close()
        this.overlayWindow = null
      }
    } catch (e) {}

    this.mainWindow.setIgnoreMouseEvents(false)
    this.mainWindow.setAlwaysOnTop(true, "screen-saver", 1)
    this.mainWindow.setVisibleOnAllWorkspaces(true, {
      visibleOnFullScreen: true
    })
    this.mainWindow.setContentProtection(true)

    try {
      this.mainWindow.show()
    } catch (e) {
      try { this.mainWindow.showInactive() } catch (_) {}
    }
    this.mainWindow.setOpacity(1)

    this.isWindowVisible = true
    console.log('Window shown')
  }

  public toggleMainWindow(): void {
    if (this.isWindowVisible) {
      this.hideMainWindow()
    } else {
      this.showMainWindow()
    }
  }

  private centerWindow(): void {
    if (!this.mainWindow || this.mainWindow.isDestroyed()) {
      return
    }

    const primaryDisplay = screen.getPrimaryDisplay()
    const workArea = primaryDisplay.workAreaSize

    // Get current window size or use defaults
    const windowBounds = this.mainWindow.getBounds()
    const windowWidth = windowBounds.width || 400
    const windowHeight = windowBounds.height || 600

    // Calculate center position
    const centerX = Math.floor((workArea.width - windowWidth) / 2)
    const centerY = Math.floor((workArea.height - windowHeight) / 2)

    // Set window position
    this.mainWindow.setBounds({
      x: centerX,
      y: centerY,
      width: windowWidth,
      height: windowHeight
    })

    // Update internal state
    this.windowPosition = { x: centerX, y: centerY }
    this.windowSize = { width: windowWidth, height: windowHeight }
    this.currentX = centerX
    this.currentY = centerY
  }

  public centerAndShowWindow(): void {
    if (!this.mainWindow || this.mainWindow.isDestroyed()) {
      console.warn("Main window does not exist or is destroyed.")
      return
    }

    this.centerWindow()

    this.mainWindow.setIgnoreMouseEvents(false)
    try { this.mainWindow.show() } catch (e) { try { this.mainWindow.showInactive() } catch (_) {} }
    this.mainWindow.setAlwaysOnTop(true, "screen-saver", 1)
    this.isWindowVisible = true

    console.log(`Window centered and shown`)
  }

  // New methods for window movement
  public moveWindowRight(): void {
    if (!this.mainWindow) return

    const windowWidth = this.windowSize?.width || 0
    const halfWidth = windowWidth / 2

    // Ensure currentX and currentY are numbers
    this.currentX = Number(this.currentX) || 0
    this.currentY = Number(this.currentY) || 0

    this.currentX = Math.min(
      this.screenWidth - halfWidth,
      this.currentX + this.step
    )
    this.mainWindow.setPosition(
      Math.round(this.currentX),
      Math.round(this.currentY)
    )
  }

  public moveWindowLeft(): void {
    if (!this.mainWindow) return

    const windowWidth = this.windowSize?.width || 0
    const halfWidth = windowWidth / 2

    // Ensure currentX and currentY are numbers
    this.currentX = Number(this.currentX) || 0
    this.currentY = Number(this.currentY) || 0

    this.currentX = Math.max(-halfWidth, this.currentX - this.step)
    this.mainWindow.setPosition(
      Math.round(this.currentX),
      Math.round(this.currentY)
    )
  }

  public moveWindowDown(): void {
    if (!this.mainWindow) return

    const windowHeight = this.windowSize?.height || 0
    const halfHeight = windowHeight / 2

    // Ensure currentX and currentY are numbers
    this.currentX = Number(this.currentX) || 0
    this.currentY = Number(this.currentY) || 0

    this.currentY = Math.min(
      this.screenHeight - halfHeight,
      this.currentY + this.step
    )
    this.mainWindow.setPosition(
      Math.round(this.currentX),
      Math.round(this.currentY)
    )
  }

  public moveWindowUp(): void {
    if (!this.mainWindow) return

    const windowHeight = this.windowSize?.height || 0
    const halfHeight = windowHeight / 2

    // Ensure currentX and currentY are numbers
    this.currentX = Number(this.currentX) || 0
    this.currentY = Number(this.currentY) || 0

    this.currentY = Math.max(-halfHeight, this.currentY - this.step)
    this.mainWindow.setPosition(
      Math.round(this.currentX),
      Math.round(this.currentY)
    )
  }
}
