export interface ElectronAPI {
  updateContentDimensions: (dimensions: {
    width: number
    height: number
  }) => Promise<void>
  getScreenshots: () => Promise<Array<{ path: string; preview: string }>>
  deleteScreenshot: (path: string) => Promise<{ success: boolean; error?: string }>
  onScreenshotTaken: (callback: (data: { path: string; preview: string }) => void) => () => void
  onSolutionsReady: (callback: (solutions: string) => void) => () => void
  onResetView: (callback: () => void) => () => void
  onSolutionStart: (callback: () => void) => () => void
  onSolutionError: (callback: (error: string) => void) => () => void
  onSolutionSuccess: (callback: (data: any) => void) => () => void
  onProblemExtracted: (callback: (data: any) => void) => () => void
  onUnauthorized: (callback: () => void) => () => void
  onProcessingNoScreenshots: (callback: () => void) => () => void

  onDebugStart: (callback: () => void) => () => void
  onDebugSuccess: (callback: (data: any) => void) => () => void
  onDebugError: (callback: (error: string) => void) => () => void

  onRestoreChat: (callback: (messages: any[]) => void) => () => void

  takeScreenshot: () => Promise<void>

  analyzeAudioFromBase64: (data: string, mimeType: string) => Promise<{ text: string; timestamp: number }>
  analyzeAudioFile: (path: string) => Promise<{ text: string; timestamp: number }>
  analyzeImageFile: (path: string) => Promise<void>

  moveWindowLeft: () => Promise<void>
  moveWindowRight: () => Promise<void>
  moveWindowUp: () => Promise<void>
  moveWindowDown: () => Promise<void>
  quitApp: () => Promise<void>

  // LLM Model Management
  getCurrentLlmConfig: () => Promise<{ provider: "ollama" | "gemini"; model: string; isOllama: boolean }>
  getAvailableOllamaModels: () => Promise<string[]>
  switchToOllama: (model?: string, url?: string) => Promise<{ success: boolean; error?: string }>
  switchToGemini: (apiKey?: string) => Promise<{ success: boolean; error?: string }>
  testLlmConnection: () => Promise<{ success: boolean; error?: string }>

  invoke: (channel: string, ...args: any[]) => Promise<any>

  // History
  getHistory: () => Promise<any[]>
  loadHistoryItem: (id: string) => Promise<{ success: boolean; error?: string }>
  saveHistoryItem: (item: any) => Promise<{ success: boolean; error?: string }>
}

declare global {
  interface Window {
    electronAPI: ElectronAPI
  }
}