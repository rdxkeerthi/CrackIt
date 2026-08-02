import { GoogleGenerativeAI } from '@google/generative-ai';
import fs from 'fs/promises';

let genAI: GoogleGenerativeAI | null = null;
let language = process.env.LANGUAGE || "Python";
let apiKey = "";

let activeMode: 'auto' | 'coding' | 'mcq' | 'interview' | 'qa' = 'auto';

interface Config {
  apiKey: string;
  language: string;
  mode?: 'auto' | 'coding' | 'mcq' | 'interview' | 'qa';
}

function updateConfig(config: Config) {
  if (!config.apiKey) {
    throw new Error('Gemini API key is required');
  }

  try {
    apiKey = config.apiKey.trim();
    genAI = new GoogleGenerativeAI(apiKey);
    language = config.language || 'Python';
    activeMode = config.mode || 'auto';
  } catch (error) {
    console.error('Error initializing Gemini client:', error);
    throw error;
  }
}

// Initialize with environment variables if available
if (process.env.GEMINI_API_KEY) {
  try {
    updateConfig({
      apiKey: process.env.GEMINI_API_KEY,
      language: process.env.LANGUAGE || 'Python'
    });
  } catch (error) {
    console.error('Error initializing Gemini with environment variables:', error);
  }
}

export interface ProcessedSolution {
  type: 'coding' | 'mcq' | 'interview' | 'qa';
  // Coding / Interview specific
  approach?: string;
  code?: string;
  timeComplexity?: string;
  spaceComplexity?: string;
  // MCQ & QA specific
  question?: string;
  options?: string[];
  correctOption?: string;
  explanation?: string;
  // QA specific
  answer?: string;
}

// Candidate models for auto-failover (using active, non-deprecated 2026 Gemini model names)
const MODELS = [
  'gemini-2.5-flash',         // Primary stable fast model
  'gemini-2.0-flash',         // Ultra-fast stable model
  'gemini-3-flash-preview',  // Gemini 3 preview model
  'gemini-2.0-flash-lite',    // Lightweight high-throughput model
  'gemini-1.5-flash-8b',      // Fast 8B fallback
  'gemini-2.5-pro',           // High-reasoning model
  'gemini-3-pro-preview'      // Reasoning preview model
];

// Helper to execute API call with per-model timeout
async function withTimeout<T>(promise: Promise<T>, timeoutMs: number = 25000): Promise<T> {
  let timeoutId: NodeJS.Timeout;
  const timeoutPromise = new Promise<never>((_, reject) => {
    timeoutId = setTimeout(() => {
      reject(new Error(`Model request timed out after ${timeoutMs}ms`));
    }, timeoutMs);
  });

  return Promise.race([promise, timeoutPromise]).finally(() => {
    clearTimeout(timeoutId!);
  });
}

export async function processScreenshots(screenshots: { path: string }[]): Promise<ProcessedSolution> {
  if (!genAI) {
    throw new Error('Gemini client not initialized. Please configure API key first. Click CTRL/CMD + P to open settings and set the API key.');
  }

  const errors: string[] = [];

  const imageParts = [];
  for (const screenshot of screenshots) {
    const imageBuffer = await fs.readFile(screenshot.path);
    const base64Image = imageBuffer.toString('base64');
    imageParts.push({
      inlineData: {
        data: base64Image,
        mimeType: "image/png"
      }
    });
  }

  const modeInstruction = activeMode !== 'auto' ? `FORCE OUTPUT MODE: ${activeMode.toUpperCase()}` : `AUTO DETECT MODE (coding, mcq, interview, or qa)`;
  const multiImageInstruction = screenshots.length > 1
    ? `CRITICAL: You are provided with ${screenshots.length} screenshots. They contain parts of the SAME question (e.g. problem statement top, code/test cases bottom). Merge ALL context across ALL ${screenshots.length} images together into ONE cohesive answer.`
    : '';

  const prompt = `
    You are an expert technical interview assistant and academic problem solver.
    Target Programming Language: ${language}
    Analyze the image(s) provided. ${modeInstruction}
    ${multiImageInstruction}

    Return strict JSON matching one of these structures based on the target type:

    IF IT IS A DIRECT QUESTION & ANSWER (QA MODE or short theoretical question like "What is X used for?"):
    {
      "type": "qa",
      "question": "[Full question text detected from image, e.g., '15. What is System.out.printf() used for?']",
      "answer": "[Direct, clear, formatted answer explaining the concept in full detail. Provide any code examples, syntax references, or language-specific explanations specifically in ${language}]"
    }

    IF IT IS A CODING PROBLEM:
    {
      "type": "coding",
      "approach": "Detailed approach step-by-step.",
      "code": "Complete code solution in ${language}.",
      "timeComplexity": "Big O time complexity",
      "spaceComplexity": "Big O space complexity"
    }

    IF IT IS A MULTIPLE CHOICE QUESTION (MCQ):
    {
      "type": "mcq",
      "question": "Question text detected",
      "options": ["Option A", "Option B", ...],
      "correctOption": "Exact text of the correct option",
      "explanation": "Detailed explanation of why this option is correct (referencing ${language} if applicable)"
    }

    IF IT IS AN INTERVIEW SCRIPT / VERBAL QUESTION:
    {
      "type": "interview",
      "approach": "🗣️ SPEAK THIS OUT LOUD TO INTERVIEWER:\n\"[Direct spoken response]\"\n\n📌 KEY TALKING POINTS:\n• [Point 1]\n• [Point 2]",
      "code": "Code snippet in ${language} if applicable.",
      "timeComplexity": "Time complexity",
      "spaceComplexity": "Space complexity"
    }
  `;

  for (const modelName of MODELS) {
    try {
      console.log(`Attempting to generate content with model: ${modelName}`);
      const model = genAI.getGenerativeModel({
        model: modelName,
        generationConfig: {
          responseMimeType: "application/json"
        }
      });

      const generateCall = model.generateContent([prompt, ...imageParts]);
      const result = await withTimeout(generateCall, 25000);
      const response = result.response;
      const text = response.text();

      return JSON.parse(text) as ProcessedSolution;

    } catch (error: any) {
      console.warn(`Model ${modelName} failed or timed out:`, error.message || error);
      const errorMsg = error.message || 'Unknown error';
      errors.push(`${modelName}: ${errorMsg}`);
    }
  }

  console.error('All models failed:', errors);
  throw new Error(`Failed to process with any Gemini model. Details:\n${errors.join('\n')}`);
}

export async function processAudioQuestion(audioBase64: string, mimeType: string = 'audio/webm'): Promise<ProcessedSolution> {
  if (!genAI) {
    throw new Error('Gemini client not initialized. Please configure API key first.');
  }

  const errors: string[] = [];

  const audioPart = {
    inlineData: {
      data: audioBase64,
      mimeType: mimeType
    }
  };

  const prompt = `
    You are an expert technical interview assistant helping a candidate in a LIVE VERBAL INTERVIEW.
    Listen to the audio question provided. It can be a technical question, coding problem, conceptual programming query, or aptitude question.

    Identify the question asked and generate an immediate VERBAL INTERVIEW ANSWER script that the user can speak out loud to the interviewer.

    Return strict JSON with this exact structure:

    IF IT IS A TECHNICAL / CODING / CONCEPTUAL QUESTION:
    {
      "type": "coding",
      "approach": "🗣️ SPEAK THIS OUT LOUD TO INTERVIEWER:\n\"[Direct, natural 2-3 sentence verbal answer you can immediately speak out loud to the interviewer]\"\n\n📌 KEY TALKING POINTS & EXPLANATION:\n• [Point 1 to elaborate on]\n• [Point 2 to elaborate on]\n• [Point 3 - trade-offs / optimization]",
      "code": "Clean, well-commented solution code in ${language} if applicable.",
      "timeComplexity": "Time: O(...)",
      "spaceComplexity": "Space: O(...)"
    }

    IF IT IS AN MCQ OR QUIZ QUESTION:
    {
      "type": "mcq",
      "question": "[Transcribed audio question]",
      "options": ["Option A", "Option B", ...],
      "correctOption": "The correct answer",
      "explanation": "🗣️ SPEAK THIS OUT LOUD:\n\"The correct answer is [Correct Option]. The reason is [concise 1-2 sentence spoken justification].\""
    }
  `;

  for (const modelName of MODELS) {
    try {
      console.log(`Attempting audio processing with model: ${modelName}`);
      const model = genAI.getGenerativeModel({
        model: modelName,
        generationConfig: {
          responseMimeType: "application/json"
        }
      });

      const generateCall = model.generateContent([prompt, audioPart]);
      const result = await withTimeout(generateCall, 15000);
      const response = result.response;
      const text = response.text();

      return JSON.parse(text) as ProcessedSolution;

    } catch (error: any) {
      console.warn(`Model ${modelName} audio processing failed:`, error.message || error);
      errors.push(`${modelName}: ${error.message || error}`);
    }
  }

  throw new Error(`Failed to process audio with any Gemini model. Details:\n${errors.join('\n')}`);
}

export default {
  processScreenshots,
  processAudioQuestion,
  updateConfig,
  validateKey
};

// EXTREMELY PERMISSIVE validation - accept almost any key
export async function validateKey(apiKey: string): Promise<string | null> {
  console.log('=== API KEY VALIDATION START ===');
  console.log(`Key length: ${apiKey.length}, First 10 chars: ${apiKey.substring(0, 10)}...`);

  // Create client once
  const testClient = new GoogleGenerativeAI(apiKey);

  // Just try to create a client - if this works, key format is valid
  try {
    const model = testClient.getGenerativeModel({ model: 'gemini-2.5-flash' });

    console.log('Testing with gemini-2.5-flash...');
    const result = await model.generateContent("Hi");
    console.log('✓ SUCCESS! Key is valid and working');
    return 'gemini-2.5-flash';
  } catch (error: any) {
    const errorMsg = error.message || error.toString();
    console.log(`Error: ${errorMsg}`);

    // If it's a rate limit or quota error, the key IS valid
    if (errorMsg.includes('429') || errorMsg.includes('quota') || errorMsg.includes('exceeded')) {
      console.log('✓ Key is VALID but rate limited - ACCEPTING');
      return 'gemini-3-flash-preview';
    }

    // Try gemini-2.5-flash as backup
    try {
      console.log('Trying gemini-2.5-flash...');
      const model2 = testClient.getGenerativeModel({ model: 'gemini-2.5-flash' });
      const result2 = await model2.generateContent("Hi");
      console.log('✓ SUCCESS with gemini-2.5-flash!');
      return 'gemini-2.5-flash';
    } catch (error2: any) {
      const errorMsg2 = error2.message || error2.toString();
      console.log(`gemini-pro error: ${errorMsg2}`);

      // Again, check for rate limit
      if (errorMsg2.includes('429') || errorMsg2.includes('quota') || errorMsg2.includes('exceeded')) {
        console.log('✓ Key is VALID but rate limited - ACCEPTING gemini-2.5-flash');
        return 'gemini-2.5-flash';
      }
    }

    // If we get here, check if it's actually an invalid key
    if (errorMsg.includes('API_KEY_INVALID') || errorMsg.includes('invalid') || errorMsg.includes('401') || errorMsg.includes('403')) {
      console.log('✗ Key appears to be invalid');
      return null;
    }

    // For ANY other error, just accept the key
    // (It might be a temporary network issue, model issue, etc.)
    console.log('⚠ Got error but accepting key anyway (might be temporary issue)');
    return 'gemini-3-flash-preview';
  }
}
