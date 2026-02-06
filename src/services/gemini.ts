import { GoogleGenerativeAI } from '@google/generative-ai';
import fs from 'fs/promises';

let genAI: GoogleGenerativeAI | null = null;
let language = process.env.LANGUAGE || "Python";
let apiKey = "";

interface Config {
  apiKey: string;
  language: string;
}

function updateConfig(config: Config) {
  if (!config.apiKey) {
    throw new Error('Gemini API key is required');
  }

  try {
    apiKey = config.apiKey.trim();
    genAI = new GoogleGenerativeAI(apiKey);
    language = config.language || 'Python';
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
  type: 'coding' | 'mcq';
  // Coding specific
  approach?: string;
  code?: string;
  timeComplexity?: string;
  spaceComplexity?: string;
  // MCQ specific
  question?: string;
  options?: string[];
  correctOption?: string;
  explanation?: string;
}

// Use 2026 models (old 1.5/1.0 models were shut down in late 2025)
const MODELS = [
  'gemini-3-flash-preview',  // Fast, great for code & MCQs
  'gemini-2.5-flash',         // Stable, non-preview option
  'gemini-3-pro-preview',     // Powerful for complex tasks
];

export async function processScreenshots(screenshots: { path: string }[]): Promise<ProcessedSolution> {
  if (!genAI) {
    throw new Error('Gemini client not initialized. Please configure API key first. Click CTRL/CMD + P to open settings and set the API key.');
  }

  const errors: string[] = [];

  for (const modelName of MODELS) {
    try {
      console.log(`Attempting to generate content with model: ${modelName}`);
      const model = genAI.getGenerativeModel({
        model: modelName,
        generationConfig: {
          responseMimeType: "application/json"
        }
      });

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

      const prompt = `
        You are an expert coding interview assistant and aptitude solver.
        Analyze the images provided. They can be either a CODING INTERVIEW QUESTION or a MULTIPLE CHOICE QUESTION (Aptitude/Quiz).

        Identify the type of the question and return a JSON object.

        IF IT IS A CODING QUESTION:
        Return strict JSON with this structure:
        {
          "type": "coding",
          "approach": "Detailed approach to solve the problem that the interviewee will speak out loud. Explain step-by-step.",
          "code": "The complete solution code in ${language}. Ensure it involves necessary imports and main function if needed.",
          "timeComplexity": "Big O analysis",
          "spaceComplexity": "Big O analysis"
        }

        IF IT IS A MULTIPLE CHOICE QUESTION (MCQ):
        Return strict JSON with this structure:
        {
          "type": "mcq",
          "question": "The question text detected",
          "options": ["Option A text", "Option B text", ...],
          "correctOption": "The correct option text exactly as in options array",
          "explanation": "Detailed explanation of why this option is correct"
        }
      `;

      const result = await model.generateContent([prompt, ...imageParts]);
      const response = result.response;
      const text = response.text();

      return JSON.parse(text) as ProcessedSolution;

    } catch (error: any) {
      console.warn(`Model ${modelName} failed:`, error.message || error);
      const errorMsg = error.message || 'Unknown error';

      // If rate limited (429), skip to next model immediately
      if (errorMsg.includes('429') || errorMsg.includes('quota')) {
        console.log(`Rate limited on ${modelName}, trying next model...`);
        errors.push(`${modelName}: Rate limit exceeded`);
        continue;
      }

      errors.push(`${modelName}: ${errorMsg}`);
      // Continue to next model
    }
  }

  // If we get here, all models failed
  console.error('All models failed:', errors);
  throw new Error(`Failed to process with any Gemini model. Details:\n${errors.join('\n')}`);
}

export default {
  processScreenshots,
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
    const model = testClient.getGenerativeModel({ model: 'gemini-3-flash-preview' });

    console.log('Testing with gemini-1.5-flash...');
    const result = await model.generateContent("Hi");
    console.log('✓ SUCCESS! Key is valid and working');
    return 'gemini-1.5-flash';
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
