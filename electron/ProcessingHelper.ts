// ProcessingHelper.ts

import { AppState } from "./main"
import { LLMHelper } from "./LLMHelper"
import dotenv from "dotenv"

dotenv.config()

const isDev = process.env.NODE_ENV === "development"
const isDevTest = process.env.IS_DEV_TEST === "true"
const MOCK_API_WAIT_TIME = Number(process.env.MOCK_API_WAIT_TIME) || 500

export class ProcessingHelper {
  private appState: AppState
  private llmHelper: LLMHelper | null = null
  private currentProcessingAbortController: AbortController | null = null
  private currentExtraProcessingAbortController: AbortController | null = null

  constructor(appState: AppState) {
    this.appState = appState

    // Check if user wants to use Ollama
    const useOllama = process.env.USE_OLLAMA === "true"
    const ollamaModel = process.env.OLLAMA_MODEL // Don't set default here, let LLMHelper auto-detect
    const ollamaUrl = process.env.OLLAMA_URL || "http://localhost:11434"

    if (useOllama) {
      console.log("[ProcessingHelper] Initializing with Ollama")
      this.llmHelper = new LLMHelper(undefined, true, ollamaModel, ollamaUrl)
    } else {
      const apiKey = process.env.GEMINI_API_KEY
      if (!apiKey) {
        console.warn("[ProcessingHelper] GEMINI_API_KEY not found — deferring LLM initialization. Set API key in settings to enable processing.")
        // Do not throw here; defer initialization until API key is provided via settings
        this.llmHelper = null
      } else {
        console.log("[ProcessingHelper] Initializing with Gemini")
        this.llmHelper = new LLMHelper(apiKey, false)
      }
    }
  }

  public async processScreenshots(): Promise<void> {
    const mainWindow = this.appState.getMainWindow()
    if (!mainWindow) return

    // If LLM is not initialized (no API key and not using Ollama), inform frontend
    if (!this.llmHelper) {
      console.warn('[ProcessingHelper] LLM not initialized. Aborting processing and notifying UI.')
      mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.UNAUTHORIZED)
      return
    }

    const view = this.appState.getView()

    if (view === "queue") {
      const screenshotQueue = this.appState.getScreenshotHelper().getScreenshotQueue()
      if (screenshotQueue.length === 0) {
        mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.NO_SCREENSHOTS)
        return
      }

      // Check if last screenshot is an audio file
      const allPaths = this.appState.getScreenshotHelper().getScreenshotQueue();
      const lastPath = allPaths[allPaths.length - 1];
      if (lastPath.endsWith('.mp3') || lastPath.endsWith('.wav')) {
        mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.INITIAL_START);
        this.appState.setView('solutions');
        try {
          const audioResult = await this.llmHelper.analyzeAudioFile(lastPath);
          mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.PROBLEM_EXTRACTED, audioResult);
          this.appState.setProblemInfo({ problem_statement: audioResult.text, input_format: {}, output_format: {}, constraints: [], test_cases: [] });
          return;
        } catch (err: any) {
          console.error('Audio processing error:', err);
          mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.INITIAL_SOLUTION_ERROR, err.message);
          return;
        }
      }

      // Extract coding problem from screenshot and generate solution
      mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.INITIAL_START)
      this.appState.setView("solutions")
      this.currentProcessingAbortController = new AbortController()
      try {
        // Step 1: Extract the coding problem from screenshot
        const extractedProblem = await this.llmHelper.extractProblemFromImages([lastPath]);

        const problemInfo = {
          problem_statement: extractedProblem.problem_statement,
          input_format: { description: extractedProblem.input_format || "See problem statement", parameters: [] as any[] },
          output_format: { description: extractedProblem.output_format || "See problem statement", type: "string", subtype: "code" },
          complexity: { time: "N/A", space: "N/A" },
          constraints: extractedProblem.constraints || [],
          test_cases: [] as any[],
          validation_type: "auto",
          difficulty: "custom"
        };

        mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.PROBLEM_EXTRACTED, problemInfo);
        this.appState.setProblemInfo(problemInfo);

        // Step 2: Generate the code solution in all 4 languages
        const solution = await this.llmHelper.generateSolution(problemInfo);

        console.log("[ProcessingHelper] Generated solution (all 4 languages). Adding to history...")
        // Add to history
        this.appState.addToHistory({
          id: Date.now().toString(),
          timestamp: Date.now(),
          type: "solution",
          problemInfo,
          solution: solution.solution,
          screenshotPath: lastPath
        });
        console.log("[ProcessingHelper] Added to history.")

        mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.SOLUTION_SUCCESS, { solution: solution.solution });
      } catch (error: any) {
        console.error("Image processing error:", error)
        mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.INITIAL_SOLUTION_ERROR, error.message)
      } finally {
        this.currentProcessingAbortController = null
      }
      return;
    } else {
      // Debug mode
      const extraScreenshotQueue = this.appState.getScreenshotHelper().getExtraScreenshotQueue()
      if (extraScreenshotQueue.length === 0) {
        console.log("No extra screenshots to process")
        mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.NO_SCREENSHOTS)
        return
      }

      mainWindow.webContents.send(this.appState.PROCESSING_EVENTS.DEBUG_START)
      this.currentExtraProcessingAbortController = new AbortController()

      try {
        // Get problem info and current solution
        const problemInfo = this.appState.getProblemInfo()
        if (!problemInfo) {
          throw new Error("No problem info available")
        }

        // Get current solution from state
        const currentSolution = await this.llmHelper.generateSolution(problemInfo)
        const currentCode = currentSolution.solution.code

        // Debug the solution using vision model
        const debugResult = await this.llmHelper.debugSolutionWithImages(
          problemInfo,
          currentCode,
          extraScreenshotQueue
        )

        this.appState.setHasDebugged(true)
        mainWindow.webContents.send(
          this.appState.PROCESSING_EVENTS.DEBUG_SUCCESS,
          debugResult
        )
      } catch (error: any) {
        console.error("Debug processing error:", error)
        mainWindow.webContents.send(
          this.appState.PROCESSING_EVENTS.DEBUG_ERROR,
          error.message
        )
      } finally {
        this.currentExtraProcessingAbortController = null
      }
    }
  }

  public cancelOngoingRequests(): void {
    if (this.currentProcessingAbortController) {
      this.currentProcessingAbortController.abort()
      this.currentProcessingAbortController = null
    }

    if (this.currentExtraProcessingAbortController) {
      this.currentExtraProcessingAbortController.abort()
      this.currentExtraProcessingAbortController = null
    }

    this.appState.setHasDebugged(false)
  }

  public async processAudioBase64(data: string, mimeType: string) {
    // Directly use LLMHelper to analyze inline base64 audio
    return this.llmHelper.analyzeAudioFromBase64(data, mimeType);
  }

  // Add audio file processing method
  public async processAudioFile(filePath: string) {
    return this.llmHelper.analyzeAudioFile(filePath);
  }

  public getLLMHelper() {
    return this.llmHelper;
  }
}
