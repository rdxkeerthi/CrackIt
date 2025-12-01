import { GoogleGenerativeAI, GenerativeModel } from "@google/generative-ai"
import * as fs from "fs"

interface OllamaResponse {
  response: string
  done: boolean
}

export class LLMHelper {
  private model: GenerativeModel | null = null
  private readonly systemPrompt = `You are an expert coding interview assistant. Your role is to help solve coding problems during technical interviews.

GENERAL BEHAVIOR
- Always prioritize clarity and interview-readiness.
- Default language for code is Python unless explicitly specified otherwise.
- Use Markdown formatting with clear section headings, bullet points, and numbered lists.
- For code, always place the full solution in its own Markdown fenced code block with language identifier "python" so it renders like in a code editor.
- Keep explanations concise but complete.

DEFAULT ANSWER STRUCTURE (when NOT explicitly asked to return JSON)
Format every answer using these clearly separated sections:

## Problem Restatement
- 1–3 sentences restating the problem in your own words.

## Intuition / High-level Idea
- Short explanation of the key idea and data structures.

## Step-by-step Algorithm
- Numbered steps ("1.", "2.", "3.") describing the algorithm precisely.

## Complexity & Edge Cases
- Time: O(...)
- Space: O(...)
- Mention the most important edge cases and how they are handled.

## Code (Python)
- Provide complete, working Python code.
- Use proper indentation and line breaks.
- Put the code in a Markdown fenced code block with language identifier "python" so it looks like it does in a code editor.

IMPORTANT
- When a later prompt explicitly asks for JSON output, strictly follow the JSON format requested there and do not include additional commentary or Markdown formatting.`
  private useOllama: boolean = false
  private ollamaModel: string = "llama3.2"
  private ollamaUrl: string = "http://localhost:11434"

  constructor(apiKey?: string, useOllama: boolean = false, ollamaModel?: string, ollamaUrl?: string) {
    this.useOllama = useOllama

    if (useOllama) {
      this.ollamaUrl = ollamaUrl || "http://localhost:11434"
      this.ollamaModel = ollamaModel || "gemma:latest" // Default fallback
      console.log(`[LLMHelper] Using Ollama with model: ${this.ollamaModel}`)

      // Auto-detect and use first available model if specified model doesn't exist
      this.initializeOllamaModel()
    } else if (apiKey) {
      const genAI = new GoogleGenerativeAI(apiKey)
      this.model = genAI.getGenerativeModel({ model: "gemini-2.5-flash" })
      console.log("[LLMHelper] Using Google Gemini 2.5 Flash")
    } else {
      throw new Error("Either provide Gemini API key or enable Ollama mode")
    }
  }

  private async fileToGenerativePart(imagePath: string) {
    const imageData = await fs.promises.readFile(imagePath)
    return {
      inlineData: {
        data: imageData.toString("base64"),
        mimeType: "image/png"
      }
    }
  }

  private cleanJsonResponse(text: string): string {
    // Remove markdown code block syntax if present
    text = text.replace(/^```(?:json)?\n/, '').replace(/\n```$/, '');
    // Remove any leading/trailing whitespace
    text = text.trim();
    return text;
  }

  private formatPythonCode(code: string): string {
    // If code already has newlines, return as-is
    if (code.includes('\n')) {
      return code;
    }

    console.log("[LLMHelper] Formatting single-line code...");

    // Step 1: Add newlines after common Python patterns
    let formatted = code;

    // Add newline after function/class definitions
    formatted = formatted.replace(/def\s+\w+\([^)]*\):/g, (match) => match + '\n    ');
    formatted = formatted.replace(/class\s+\w+[^:]*:/g, (match) => match + '\n    ');

    // Add newline after if/elif/else/for/while statements
    formatted = formatted.replace(/\bif\s+[^:]+:/g, (match) => match + '\n        ');
    formatted = formatted.replace(/\belif\s+[^:]+:/g, (match) => match + '\n        ');
    formatted = formatted.replace(/\belse:/g, 'else:\n        ');
    formatted = formatted.replace(/\bfor\s+[^:]+:/g, (match) => match + '\n        ');
    formatted = formatted.replace(/\bwhile\s+[^:]+:/g, (match) => match + '\n        ');

    // Add newline after return/break/continue statements
    formatted = formatted.replace(/\breturn\s+[^\s]+\s+/g, (match) => match.trim() + '\n    ');
    formatted = formatted.replace(/\bbreak\s+/g, 'break\n    ');
    formatted = formatted.replace(/\bcontinue\s+/g, 'continue\n    ');

    // Add newline after variable assignments (but not in list comprehensions)
    formatted = formatted.replace(/([a-zA-Z_]\w*\s*=\s*[^=][^\n]*?)\s+([a-zA-Z_])/g, '$1\n    $2');

    // Add newline after print/function calls
    formatted = formatted.replace(/\)\s+([a-zA-Z_])/g, ')\n    $1');

    // Clean up excessive spacing
    formatted = formatted.replace(/\n\s*\n\s*\n/g, '\n\n');
    formatted = formatted.replace(/\n\s{8,}/g, '\n        '); // Max 2 indent levels

    // Ensure proper indentation structure
    const lines = formatted.split('\n');
    let indentLevel = 0;
    const formattedLines = lines.map(line => {
      const trimmed = line.trim();
      if (!trimmed) return '';

      // Decrease indent for dedent keywords
      if (trimmed.startsWith('return ') || trimmed.startsWith('break') ||
        trimmed.startsWith('continue') || trimmed.startsWith('pass')) {
        // Keep current indent
      } else if (trimmed.startsWith('elif ') || trimmed.startsWith('else:') ||
        trimmed.startsWith('except') || trimmed.startsWith('finally:')) {
        indentLevel = Math.max(0, indentLevel - 1);
      }

      const indentedLine = '    '.repeat(indentLevel) + trimmed;

      // Increase indent after colon
      if (trimmed.endsWith(':')) {
        indentLevel++;
      }

      return indentedLine;
    });

    return formattedLines.join('\n');
  }

  private async callOllama(prompt: string): Promise<string> {
    try {
      const response = await fetch(`${this.ollamaUrl}/api/generate`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          model: this.ollamaModel,
          prompt: prompt,
          stream: false,
          options: {
            temperature: 0.7,
            top_p: 0.9,
          }
        }),
      })

      if (!response.ok) {
        throw new Error(`Ollama API error: ${response.status} ${response.statusText}`)
      }

      const data: OllamaResponse = await response.json()
      return data.response
    } catch (error) {
      console.error("[LLMHelper] Error calling Ollama:", error)
      throw new Error(`Failed to connect to Ollama: ${error.message}. Make sure Ollama is running on ${this.ollamaUrl}`)
    }
  }

  private async checkOllamaAvailable(): Promise<boolean> {
    try {
      const response = await fetch(`${this.ollamaUrl}/api/tags`)
      return response.ok
    } catch {
      return false
    }
  }

  private async initializeOllamaModel(): Promise<void> {
    try {
      const availableModels = await this.getOllamaModels()
      if (availableModels.length === 0) {
        console.warn("[LLMHelper] No Ollama models found")
        return
      }

      // Check if current model exists, if not use the first available
      if (!availableModels.includes(this.ollamaModel)) {
        this.ollamaModel = availableModels[0]
        console.log(`[LLMHelper] Auto-selected first available model: ${this.ollamaModel}`)
      }

      // Test the selected model works
      const testResult = await this.callOllama("Hello")
      console.log(`[LLMHelper] Successfully initialized with model: ${this.ollamaModel}`)
    } catch (error) {
      console.error(`[LLMHelper] Failed to initialize Ollama model: ${error.message}`)
      // Try to use first available model as fallback
      try {
        const models = await this.getOllamaModels()
        if (models.length > 0) {
          this.ollamaModel = models[0]
          console.log(`[LLMHelper] Fallback to: ${this.ollamaModel}`)
        }
      } catch (fallbackError) {
        console.error(`[LLMHelper] Fallback also failed: ${fallbackError.message}`)
      }
    }
  }

  public async extractProblemFromImages(imagePaths: string[]) {
    try {
      const imageParts = await Promise.all(imagePaths.map(path => this.fileToGenerativePart(path)))

      const prompt = `${this.systemPrompt}\n\nAnalyze this screenshot of a coding problem. Extract the problem details and provide a complete solution in JSON format:\n{\n  "problem_statement": "Clear description of the coding problem (plain text, no markdown)",\n  "input_format": "Description of input parameters (plain text, no markdown)",\n  "output_format": "Description of expected output (plain text, no markdown)",\n  "constraints": "Any constraints or edge cases (plain text, no markdown)",\n  "examples": "Sample input/output if provided (plain text, no markdown)"\n}\nImportant: Return ONLY the JSON object, without any markdown formatting, code fences, or backticks.`

      const result = await this.model.generateContent([prompt, ...imageParts])
      const response = await result.response
      const text = this.cleanJsonResponse(response.text())
      return JSON.parse(text)
    } catch (error) {
      console.error("Error extracting problem from images:", error)
      throw error
    }
  }

  // New: extract MCQ (question + options) from image(s)
  public async extractMcqFromImages(imagePaths: string[]) {
    try {
      const imageParts = await Promise.all(imagePaths.map(path => this.fileToGenerativePart(path)))

      const prompt = `${this.systemPrompt}\n\nAnalyze this screenshot of a multiple choice question. Extract the question text and the options (A, B, C, D) and return a JSON object exactly in the following format (no extra text):\n{\n  "question": "...",\n  "options": ["option A text", "option B text", "option C text", "option D text"]\n}`

      const result = await this.model.generateContent([prompt, ...imageParts])
      const response = await result.response
      const text = this.cleanJsonResponse(response.text())
      return JSON.parse(text)
    } catch (error) {
      console.error("Error extracting MCQ from images:", error)
      throw error
    }
  }

  public async extractAndSolve(imagePaths: string[]) {
    try {
      const imageParts = await Promise.all(imagePaths.map(path => this.fileToGenerativePart(path)))

      const prompt = `${this.systemPrompt}\n\nAnalyze this screenshot of a coding problem. Extract the problem details AND provide a complete Python solution in a single JSON object.\n\nRequired JSON Format:\n{\n  "problem_info": {\n    "problem_statement": "Clear description (plain text)",\n    "input_format": "Description of input (plain text)",\n    "output_format": "Description of output (plain text)",\n    "constraints": "Constraints (plain text)",\n    "examples": "Examples (plain text)"\n  },\n  "solution": {\n    "code": "Complete working Python code. CRITICAL: Use literal \\n for line breaks. Indent with 4 spaces. NO markdown.",\n    "thoughts": ["Step 1...", "Step 2...", "Step 3..."],\n    "time_complexity": "O(n) - brief explanation",\n    "space_complexity": "O(1) - brief explanation"\n  }\n}\n\nImportant: Return ONLY the JSON object. NO markdown formatting.`

      console.log("[LLMHelper] Calling extractAndSolve (Single Shot - Python only)...")
      const result = await this.model.generateContent([prompt, ...imageParts])
      const response = await result.response
      const text = this.cleanJsonResponse(response.text())
      const parsed = JSON.parse(text)

      // Post-process solution code
      if (parsed.solution && parsed.solution.code) {
        parsed.solution.code = this.formatPythonCode(parsed.solution.code)
        // Create codes object for compatibility
        parsed.solution.codes = {
          python: parsed.solution.code,
          c: "// Not generated",
          java: "// Not generated",
          javascript: "// Not generated"
        }
      }

      return parsed
    } catch (error) {
      console.error("Error in extractAndSolve:", error)
      throw error
    }
  }

  public async generateSolution(problemInfo: any) {
    const prompt = `${this.systemPrompt}\n\nGiven this coding problem:\n${JSON.stringify(problemInfo, null, 2)}\n\nProvide a complete solution in the following JSON format:\n{\n  "solution": {\n    "codes": {\n      "python": "Complete working code solution in Python. CRITICAL: Each line of code must be separated by actual newline characters (\\n). Write the code exactly as it should appear in an editor. Use proper indentation with 4 spaces. Do NOT include markdown or backticks.",\n      "c": "Complete working code solution in C. Use \\n for line breaks. Do NOT include markdown.",\n      "java": "Complete working code solution in Java. Use \\n for line breaks. Do NOT include markdown.",\n      "javascript": "Complete working code solution in JavaScript. Use \\n for line breaks. Do NOT include markdown."\n    },\n    "thoughts": ["Step 1: Approach explanation in 1 concise sentence (no markdown)", "Step 2: Key insight in 1 concise sentence (no markdown)", "Step 3: Implementation detail in 1 concise sentence (no markdown)"],\n    "time_complexity": "O(n) - with explanation in plain text, no markdown",\n    "space_complexity": "O(1) - with explanation in plain text, no markdown"\n  }\n}\nCRITICAL: The code fields MUST use literal \\n characters for line breaks and MUST NOT contain any markdown formatting or backticks.`

    console.log("[LLMHelper] Calling Gemini LLM for solution...");
    try {
      const result = await this.model.generateContent(prompt)
      console.log("[LLMHelper] Gemini LLM returned result.");
      const response = await result.response
      const text = this.cleanJsonResponse(response.text())
      const parsed = JSON.parse(text)

      // Post-process: Format the code to ensure proper structure
      // Post-process: Format the code to ensure proper structure
      if (parsed.solution) {
        if (parsed.solution.codes) {
          // Format Python code
          if (parsed.solution.codes.python) {
            parsed.solution.codes.python = this.formatPythonCode(parsed.solution.codes.python);
          }
          // Assign to legacy code field for backward compatibility if needed, or just rely on codes
          parsed.solution.code = parsed.solution.codes.python;
        } else if (parsed.solution.code) {
          // Fallback if LLM messes up and returns old format
          parsed.solution.code = this.formatPythonCode(parsed.solution.code);
          parsed.solution.codes = {
            python: parsed.solution.code,
            c: "// C code not generated",
            java: "// Java code not generated",
            javascript: "// JavaScript code not generated"
          };
        }
      }

      console.log("[LLMHelper] Parsed LLM response:", parsed)
      return parsed
    } catch (error) {
      console.error("[LLMHelper] Error in generateSolution:", error);
      throw error;
    }
  }

  public async debugSolutionWithImages(problemInfo: any, currentCode: string, debugImagePaths: string[]) {
    try {
      const imageParts = await Promise.all(debugImagePaths.map(path => this.fileToGenerativePart(path)))

      const prompt = `${this.systemPrompt}\n\nGiven:\n1. Original coding problem: ${JSON.stringify(problemInfo, null, 2)}\n2. Current code solution: ${currentCode}\n3. Debug information in the provided screenshots\n\nAnalyze the debug info and provide an improved solution in this JSON format:\n{
  "solution": {\n    "codes": {\n      "python": "Complete corrected/improved code solution in Python. CRITICAL: Use literal \\n characters for line breaks. Do NOT include markdown.",\n      "c": "Complete corrected/improved code solution in C. Use \\n for line breaks. Do NOT include markdown.",\n      "java": "Complete corrected/improved code solution in Java. Use \\n for line breaks. Do NOT include markdown.",\n      "javascript": "Complete corrected/improved code solution in JavaScript. Use \\n for line breaks. Do NOT include markdown."\n    },\n    "thoughts": ["What was wrong (plain text, no markdown)", "How it's fixed (plain text, no markdown)", "Key improvements (plain text, no markdown)"],\n    "time_complexity": "O(n) - with explanation in plain text, no markdown",\n    "space_complexity": "O(1) - with explanation in plain text, no markdown"\n  }\n}\nCRITICAL: The code fields MUST use \\n for line breaks and MUST NOT contain any markdown formatting or backticks.`

      const result = await this.model.generateContent([prompt, ...imageParts])
      const response = await result.response
      const text = this.cleanJsonResponse(response.text())
      const parsed = JSON.parse(text)

      // Post-process: Format the code to ensure proper structure
      // Post-process: Format the code to ensure proper structure
      if (parsed.solution) {
        if (parsed.solution.codes) {
          if (parsed.solution.codes.python) {
            parsed.solution.codes.python = this.formatPythonCode(parsed.solution.codes.python);
          }
          parsed.solution.code = parsed.solution.codes.python;
        } else if (parsed.solution.code) {
          parsed.solution.code = this.formatPythonCode(parsed.solution.code);
          parsed.solution.codes = {
            python: parsed.solution.code,
            c: "// C code not generated",
            java: "// Java code not generated",
            javascript: "// JavaScript code not generated"
          };
        }
      }

      console.log("[LLMHelper] Parsed debug LLM response:", parsed)
      return parsed
    } catch (error) {
      console.error("Error debugging solution with images:", error)
      throw error
    }
  }

  public async analyzeAudioFile(audioPath: string) {
    try {
      const audioData = await fs.promises.readFile(audioPath);
      const audioPart = {
        inlineData: {
          data: audioData.toString("base64"),
          mimeType: "audio/mp3"
        }
      };
      const prompt = `${this.systemPrompt}\n\nListen to this audio clip. The user is asking a coding interview question. Provide:\n1. A brief problem understanding\n2. The complete working code solution (use Python unless specified otherwise)\n3. Brief explanation of the approach\n4. Time and space complexity\n\nUse your DEFAULT ANSWER STRUCTURE from the system prompt. Format the answer using Markdown headings and bullet points. For the code, return properly indented Python code inside a Markdown fenced code block with language identifier "python" so it renders like in a code editor.`;
      const result = await this.model.generateContent([prompt, audioPart]);
      const response = await result.response;
      const text = response.text();
      return { text, timestamp: Date.now() };
    } catch (error) {
      console.error("Error analyzing audio file:", error);
      throw error;
    }
  }

  public async analyzeAudioFromBase64(data: string, mimeType: string) {
    try {
      const audioPart = {
        inlineData: {
          data,
          mimeType
        }
      };
      const prompt = `${this.systemPrompt}\n\nListen to this audio clip. The user is asking a coding interview question. Provide:\n1. A brief problem understanding\n2. The complete working code solution (use Python unless specified otherwise)\n3. Brief explanation of the approach\n4. Time and space complexity\n\nUse your DEFAULT ANSWER STRUCTURE from the system prompt. Format the answer using Markdown headings and bullet points. For the code, return properly indented Python code inside a Markdown fenced code block with language identifier "python" so it renders like in a code editor.`;
      const result = await this.model.generateContent([prompt, audioPart]);
      const response = await result.response;
      const text = response.text();
      return { text, timestamp: Date.now() };
    } catch (error) {
      console.error("Error analyzing audio from base64:", error);
      throw error;
    }
  }

  public async analyzeImageFile(imagePath: string) {
    try {
      const imageData = await fs.promises.readFile(imagePath);
      const imagePart = {
        inlineData: {
          data: imageData.toString("base64"),
          mimeType: "image/png"
        }
      };
      const prompt = `${this.systemPrompt}\n\nAnalyze this screenshot of a coding problem and provide a direct solution. If it's a coding question, provide the complete working code. If it's example code, explain what it does concisely. Use your DEFAULT ANSWER STRUCTURE from the system prompt and format the entire answer using Markdown headings and bullet points. For the code, return properly indented code inside a Markdown fenced code block with an appropriate language identifier so it renders like in a code editor.`;
      const result = await this.model.generateContent([prompt, imagePart]);
      const response = await result.response;
      const text = response.text();
      return { text, timestamp: Date.now() };
    } catch (error) {
      console.error("Error analyzing image file:", error);
      throw error;
    }
  }

  public async chatWithGemini(message: string): Promise<string> {
    try {
      if (this.useOllama) {
        return this.callOllama(message);
      } else if (this.model) {
        const result = await this.model.generateContent(message);
        const response = await result.response;
        return response.text();
      } else {
        throw new Error("No LLM provider configured");
      }
    } catch (error) {
      console.error("[LLMHelper] Error in chatWithGemini:", error);
      throw error;
    }
  }

  public async chat(message: string): Promise<string> {
    return this.chatWithGemini(message);
  }

  public isUsingOllama(): boolean {
    return this.useOllama;
  }

  public async getOllamaModels(): Promise<string[]> {
    if (!this.useOllama) return [];

    try {
      const response = await fetch(`${this.ollamaUrl}/api/tags`);
      if (!response.ok) throw new Error('Failed to fetch models');

      const data = await response.json();
      return data.models?.map((model: any) => model.name) || [];
    } catch (error) {
      console.error("[LLMHelper] Error fetching Ollama models:", error);
      return [];
    }
  }

  public getCurrentProvider(): "ollama" | "gemini" {
    return this.useOllama ? "ollama" : "gemini";
  }

  public getCurrentModel(): string {
    return this.useOllama ? this.ollamaModel : "gemini-2.0-flash";
  }

  public async switchToOllama(model?: string, url?: string): Promise<void> {
    this.useOllama = true;
    if (url) this.ollamaUrl = url;

    if (model) {
      this.ollamaModel = model;
    } else {
      // Auto-detect first available model
      await this.initializeOllamaModel();
    }

    console.log(`[LLMHelper] Switched to Ollama: ${this.ollamaModel} at ${this.ollamaUrl}`);
  }

  public async switchToGemini(apiKey?: string): Promise<void> {
    if (apiKey) {
      const genAI = new GoogleGenerativeAI(apiKey);
      this.model = genAI.getGenerativeModel({ model: "gemini-2.0-flash-exp" });
    }

    if (!this.model && !apiKey) {
      throw new Error("No Gemini API key provided and no existing model instance");
    }

    this.useOllama = false;
    console.log("[LLMHelper] Switched to Gemini");
  }

  public async summarizeMeeting(audioBase64: string): Promise<string> {
    try {
      const model = this.model || new GoogleGenerativeAI(process.env.GEMINI_API_KEY || "").getGenerativeModel({ model: "gemini-2.0-flash-exp" });

      // If we don't have a model yet (e.g. no API key set), we can't proceed
      if (!model) throw new Error("Gemini model not initialized");

      const prompt = `You are an expert meeting secretary. Your task is to listen to the provided meeting audio and generate a structured report containing:
1. **Executive Summary**: A concise overview of the meeting's purpose and main outcomes.
2. **Key Discussion Points**: A bulleted list of the most important topics discussed.
3. **Action Items**: A list of tasks assigned, with owners if mentioned.
4. **Decisions Made**: A list of any formal or informal decisions reached.

Format the output in clean Markdown.`

      const result = await model.generateContent([
        prompt,
        {
          inlineData: {
            mimeType: "audio/webm",
            data: audioBase64
          }
        }
      ])

      const response = result.response
      return response.text()
    } catch (error) {
      console.error("Error summarizing meeting:", error)
      return "Failed to summarize meeting. Please try again."
    }
  }

  public async testConnection(): Promise<{ success: boolean; error?: string }> {
    try {
      if (this.useOllama) {
        const available = await this.checkOllamaAvailable();
        if (!available) {
          return { success: false, error: `Ollama not available at ${this.ollamaUrl}` };
        }
        // Test with a simple prompt
        await this.callOllama("Hello");
        return { success: true };
      } else {
        if (!this.model) {
          return { success: false, error: "No Gemini model configured" };
        }
        // Test with a simple prompt
        const result = await this.model.generateContent("Hello");
        const response = await result.response;
        const text = response.text(); // Ensure the response is valid
        if (text) {
          return { success: true };
        } else {
          return { success: false, error: "Empty response from Gemini" };
        }
      }
    } catch (error) {
      return { success: false, error: error.message };
    }
  }
} 