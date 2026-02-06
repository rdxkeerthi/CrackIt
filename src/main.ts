import { app, BrowserWindow, ipcMain, globalShortcut } from 'electron';
import * as path from 'path';
import * as fs from 'fs/promises';
import { execFile } from 'child_process';
import { promisify } from 'util';
import geminiService from './services/gemini';

const execFileAsync = promisify(execFile);

interface Screenshot {
  id: number;
  preview: string;
  path: string;
}

// Handle uncaught exceptions
process.on('uncaughtException', (error) => {
  console.error('Uncaught Exception:', error);
});

process.on('unhandledRejection', (reason, promise) => {
  console.error('Unhandled Rejection at:', promise, 'reason:', reason);
});

let CONFIG_FILE: string;
try {
  CONFIG_FILE = path.join(app.getPath('userData'), 'config.json');
} catch (e) {
  // If getting path fails early, use a fallback or wait for ready
  CONFIG_FILE = 'config.json';
}

console.log('Config file path:', CONFIG_FILE);

interface Config {
  apiKey: string;
  language: string;
  mode?: 'auto' | 'coding' | 'mcq';
  activeModel?: string;
}

let config: Config | null = null;

let mainWindow: BrowserWindow | null = null;
let screenshotQueue: Screenshot[] = [];
let isProcessing = false;
const MAX_SCREENSHOTS = 4;
const SCREENSHOT_DIR = path.join(app.getPath('temp'), 'screenshots');

async function ensureScreenshotDir() {
  try {
    await fs.mkdir(SCREENSHOT_DIR, { recursive: true });
  } catch (error) {
    console.error('Error creating screenshot directory:', error);
  }
}

async function loadConfig(): Promise<Config | null> {
  try {
    // First try loading from environment variables
    const envApiKey = process.env.GEMINI_API_KEY;
    const envLanguage = process.env.APP_LANGUAGE;

    if (envApiKey && envLanguage) {
      const envConfig = {
        apiKey: envApiKey,
        language: envLanguage
      };
      geminiService.updateConfig(envConfig);
      return envConfig;
    }

    // If env vars not found, try loading from config file
    try {
      const data = await fs.readFile(CONFIG_FILE, 'utf-8');
      const loadedConfig = JSON.parse(data);
      if (loadedConfig && loadedConfig.apiKey && loadedConfig.language) {
        geminiService.updateConfig(loadedConfig);
        return loadedConfig;
      }
    } catch (fsError: any) {
      // Ignore ENOENT error (config file doesn't exist yet)
      if (fsError.code !== 'ENOENT') {
        console.error('Error reading config file:', fsError);
      }
    }
    return null;
  } catch (error) {
    console.error('Error loading config:', error);
    return null;
  }
}

async function saveConfig(newConfig: Config): Promise<{ success: boolean; error?: string; model?: string }> {
  try {
    if (!newConfig.apiKey || !newConfig.language) {
      throw new Error('Invalid configuration');
    }

    // Validate key and find best model
    const activeModel = await geminiService.validateKey(newConfig.apiKey);
    if (!activeModel) {
      // If no model works, warn but maybe allow saving if user insists? 
      // For now, let's be strict or return error.
      throw new Error('Invalid API Key or no available Gemini models found.');
    }

    newConfig.activeModel = activeModel;
    await fs.writeFile(CONFIG_FILE, JSON.stringify(newConfig, null, 2));
    config = newConfig;
    geminiService.updateConfig(newConfig);

    return { success: true, model: activeModel };
  } catch (error: any) {
    console.error('Error saving config:', error);
    return { success: false, error: error.message };
  }
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 800,
    height: 600,
    frame: false,
    transparent: true,
    backgroundColor: "#00000000",
    hasShadow: false,
    alwaysOnTop: true,
    webPreferences: {
      nodeIntegration: true,
      contextIsolation: true,
      preload: path.join(__dirname, 'preload.js')
    }
  });

  // Open DevTools by default in development
  if (process.env.NODE_ENV === 'development') {
    mainWindow.webContents.openDevTools({ mode: 'detach' });
  }

  // Register DevTools shortcut
  globalShortcut.register('CommandOrControl+Shift+I', () => {
    if (mainWindow) {
      mainWindow.webContents.toggleDevTools();
    }
  });

  // Enable content protection to prevent screen capture
  mainWindow.setContentProtection(true);

  // Platform specific enhancements for macOS
  if (process.platform === 'darwin') {
    mainWindow.setHiddenInMissionControl(true);
    mainWindow.setVisibleOnAllWorkspaces(true, {
      visibleOnFullScreen: true
    });
    mainWindow.setAlwaysOnTop(true, "floating");
  }

  // Load the index.html file from the dist directory
  mainWindow.loadFile(path.join(__dirname, '../dist/renderer/index.html'));

  // Register global shortcuts
  try {
    registerShortcuts();
  } catch (e) {
    console.error('Failed to register shortcuts:', e);
  }
}

function registerShortcuts() {
  // Screenshot & Processing shortcuts
  globalShortcut.register('CommandOrControl+H', handleTakeScreenshot);
  globalShortcut.register('CommandOrControl+Enter', handleProcessScreenshots);
  globalShortcut.register('CommandOrControl+R', handleResetQueue);
  globalShortcut.register('CommandOrControl+Q', () => app.quit());

  // Window visibility
  globalShortcut.register('CommandOrControl+B', handleToggleVisibility);

  // Window movement
  globalShortcut.register('CommandOrControl+Left', () => moveWindow('left'));
  globalShortcut.register('CommandOrControl+Right', () => moveWindow('right'));
  globalShortcut.register('CommandOrControl+Up', () => moveWindow('up'));
  globalShortcut.register('CommandOrControl+Down', () => moveWindow('down'));

  // Config shortcut
  globalShortcut.register('CommandOrControl+P', () => {
    mainWindow?.webContents.send('show-config');
  });

  // New Shortcuts
  globalShortcut.register('CommandOrControl+M', () => {
    mainWindow?.webContents.send('cycle-mode');
  });
  globalShortcut.register('CommandOrControl+L', () => {
    mainWindow?.webContents.send('cycle-language');
  });
}

async function captureScreenshot(): Promise<Buffer> {
  if (process.platform === 'darwin') {
    const tmpPath = path.join(SCREENSHOT_DIR, `${Date.now()}.png`);
    await execFileAsync('screencapture', ['-x', tmpPath]);
    const buffer = await fs.readFile(tmpPath);
    await fs.unlink(tmpPath);
    return buffer;
  } else {
    // Windows implementation
    const tmpPath = path.join(SCREENSHOT_DIR, `${Date.now()}.png`);
    const script = `
      Add-Type -AssemblyName System.Windows.Forms
      Add-Type -AssemblyName System.Drawing
      $screen = [System.Windows.Forms.Screen]::PrimaryScreen
      $bitmap = New-Object System.Drawing.Bitmap $screen.Bounds.Width, $screen.Bounds.Height
      $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
      $graphics.CopyFromScreen($screen.Bounds.X, $screen.Bounds.Y, 0, 0, $bitmap.Size)
      $bitmap.Save('${tmpPath.replace(/\\/g, "\\\\")}')
      $graphics.Dispose()
      $bitmap.Dispose()
    `;
    await execFileAsync('powershell', ['-command', script]);
    const buffer = await fs.readFile(tmpPath);
    await fs.unlink(tmpPath);
    return buffer;
  }
}

async function handleTakeScreenshot() {
  if (screenshotQueue.length >= MAX_SCREENSHOTS) return;

  try {
    // Hide window before taking screenshot
    mainWindow?.hide();
    await new Promise(resolve => setTimeout(resolve, 100));

    const buffer = await captureScreenshot();
    const id = Date.now();
    const screenshotPath = path.join(SCREENSHOT_DIR, `${id}.png`);

    await fs.writeFile(screenshotPath, buffer);
    const preview = `data:image/png;base64,${buffer.toString('base64')}`;

    const screenshot = { id, preview, path: screenshotPath };
    screenshotQueue.push(screenshot);

    mainWindow?.show();
    mainWindow?.webContents.send('screenshot-taken', screenshot);
  } catch (error) {
    console.error('Error taking screenshot:', error);
    mainWindow?.show();
  }
}

async function handleProcessScreenshots() {
  if (isProcessing || screenshotQueue.length === 0) return;

  isProcessing = true;
  mainWindow?.webContents.send('processing-started');

  try {
    const result = await geminiService.processScreenshots(screenshotQueue);
    // Check if processing was cancelled
    if (!isProcessing) return;
    mainWindow?.webContents.send('processing-complete', JSON.stringify(result));
  } catch (error: any) {
    console.error('Error processing screenshots:', error);
    // Check if processing was cancelled
    if (!isProcessing) return;

    // Extract the most relevant error message
    let errorMessage = 'Error processing screenshots';
    if (error?.error?.message) {
      errorMessage = error.error.message;
    } else if (error?.message) {
      errorMessage = error.message;
    }

    mainWindow?.webContents.send('processing-complete', JSON.stringify({
      error: errorMessage,
      approach: 'Error occurred while processing',
      code: 'Error: ' + errorMessage,
      timeComplexity: 'N/A',
      spaceComplexity: 'N/A'
    }));
  } finally {
    isProcessing = false;
  }
}

async function handleResetQueue() {
  // Cancel any ongoing processing
  if (isProcessing) {
    isProcessing = false;
    mainWindow?.webContents.send('processing-complete', JSON.stringify({
      approach: 'Processing cancelled',
      code: '',
      timeComplexity: '',
      spaceComplexity: ''
    }));
  }

  // Delete all screenshot files
  for (const screenshot of screenshotQueue) {
    try {
      await fs.unlink(screenshot.path);
    } catch (error) {
      console.error('Error deleting screenshot:', error);
    }
  }

  screenshotQueue = [];
  mainWindow?.webContents.send('queue-reset');
}

function handleToggleVisibility() {
  if (!mainWindow) return;
  if (mainWindow.isVisible()) {
    mainWindow.hide();
  } else {
    mainWindow.show();
  }
}

function moveWindow(direction: 'left' | 'right' | 'up' | 'down') {
  if (!mainWindow) return;

  const [x, y] = mainWindow.getPosition();
  const moveAmount = 50;

  switch (direction) {
    case 'left':
      mainWindow.setPosition(x - moveAmount, y);
      break;
    case 'right':
      mainWindow.setPosition(x + moveAmount, y);
      break;
    case 'up':
      mainWindow.setPosition(x, y - moveAmount);
      break;
    case 'down':
      mainWindow.setPosition(x, y + moveAmount);
      break;
  }
}

// This method will be called when Electron has finished initialization
app.whenReady().then(async () => {
  try {
    // Re-initialize config path if needed (though usually userData is fine early)
    CONFIG_FILE = path.join(app.getPath('userData'), 'config.json');
    console.log('App Ready. Config path:', CONFIG_FILE);

    await ensureScreenshotDir();
    // Load config before creating window
    config = await loadConfig();
    createWindow();

    app.on('activate', function () {
      if (BrowserWindow.getAllWindows().length === 0) createWindow();
    });
  } catch (error) {
    console.error('Fatal error during startup:', error);
  }
});

app.on('will-quit', () => {
  globalShortcut.unregisterAll();
  handleResetQueue();
});

app.on('window-all-closed', function () {
  // On macOS it is common for applications and their menu bar
  // to stay active until the user quits explicitly with Cmd + Q
  if (process.platform !== 'darwin') app.quit();
});

// IPC Handlers
ipcMain.handle('take-screenshot', handleTakeScreenshot);
ipcMain.handle('process-screenshots', handleProcessScreenshots);
ipcMain.handle('reset-queue', handleResetQueue);

// Window control events
ipcMain.on('minimize-window', () => {
  mainWindow?.minimize();
});

ipcMain.on('maximize-window', () => {
  if (mainWindow?.isMaximized()) {
    mainWindow?.unmaximize();
  } else {
    mainWindow?.maximize();
  }
});

ipcMain.on('close-window', () => {
  mainWindow?.close();
});

ipcMain.on('quit-app', () => {
  app.quit();
});

ipcMain.on('toggle-visibility', handleToggleVisibility);

// Add these IPC handlers before app.whenReady()
ipcMain.handle('get-config', async () => {
  try {
    if (!config) {
      config = await loadConfig();
    }
    return config;
  } catch (error) {
    console.error('Error getting config:', error);
    return null;
  }
});

ipcMain.handle('save-config', async (_, newConfig: Config) => {
  return await saveConfig(newConfig);
}); 