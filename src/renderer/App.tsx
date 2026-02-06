import React, { useState, useEffect } from 'react';
import './App.css';
import ConfigScreen from './ConfigScreen';

interface Screenshot {
  id: number;
  preview: string;
  path: string;
}

interface ProcessedSolution {
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

interface Config {
  apiKey: string;
  language: string;
  mode?: 'auto' | 'coding' | 'mcq';
  activeModel?: string;
}

declare global {
  interface Window {
    electron: {
      minimize: () => void;
      maximize: () => void;
      close: () => void;
      quit: () => void;
      takeScreenshot: () => Promise<void>;
      processScreenshots: () => Promise<void>;
      resetQueue: () => Promise<void>;
      getConfig: () => Promise<Config | null>;
      saveConfig: (config: Config) => Promise<{ success: boolean; error?: string; model?: string }>;
      onProcessingComplete: (callback: (result: string) => void) => void;
      onScreenshotTaken: (callback: (data: Screenshot) => void) => void;
      onProcessingStarted: (callback: () => void) => void;
      onQueueReset: (callback: () => void) => void;
      onShowConfig: (callback: () => void) => void;
      onCycleMode: (callback: () => void) => void;
      onCycleLanguage: (callback: () => void) => void;
    };
  }
}

const App: React.FC = () => {
  const [isProcessing, setIsProcessing] = useState(false);
  const [result, setResult] = useState<ProcessedSolution | null>(null);
  const [screenshots, setScreenshots] = useState<Screenshot[]>([]);
  const [showConfig, setShowConfig] = useState(false);
  const [config, setConfig] = useState<Config | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isValidating, setIsValidating] = useState(false);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  useEffect(() => {
    const loadConfig = async () => {
      const savedConfig = await window.electron.getConfig();
      if (savedConfig && savedConfig.apiKey && savedConfig.activeModel) {
        setConfig(savedConfig);
        setShowConfig(false);
      } else {
        setShowConfig(true);
      }
    };

    loadConfig();
  }, []);

  useEffect(() => {
    console.log('Setting up event listeners...');

    // Listen for show config events
    window.electron.onShowConfig(() => {
      setShowConfig(prev => !prev);
    });

    window.electron.onCycleMode(() => {
      setConfig(prev => {
        if (!prev) return null;
        const modes: ('auto' | 'coding' | 'mcq')[] = ['auto', 'coding', 'mcq'];
        const nextIndex = (modes.indexOf(prev.mode || 'auto') + 1) % modes.length;
        const newMode = modes[nextIndex];
        const newConfig = { ...prev, mode: newMode };
        window.electron.saveConfig(newConfig); // Auto save? Maybe just update state for now? 
        // Let's autosave it for convenience
        return newConfig;
      });
      // We need a way to show toast for mode change
    });

    window.electron.onCycleLanguage(() => {
      setConfig(prev => {
        if (!prev) return null;
        const langs = ['Python', 'JavaScript', 'Java', 'C++', 'Go'];
        const nextIndex = (langs.indexOf(prev.language) + 1) % langs.length;
        const newConfig = { ...prev, language: langs[nextIndex] };
        window.electron.saveConfig(newConfig);
        return newConfig;
      });
    });

    // Listen for processing started events
    window.electron.onProcessingStarted(() => {
      console.log('Processing started');
      setIsProcessing(true);
      setResult(null);
    });

    // Keyboard event listener
    const handleKeyDown = async (event: KeyboardEvent) => {
      console.log('Key pressed:', event.key);

      // Check if Cmd/Ctrl is pressed
      const isCmdOrCtrl = event.metaKey || event.ctrlKey;

      switch (event.key.toLowerCase()) {
        case 'h':
          console.log('Screenshot hotkey pressed');
          await handleTakeScreenshot();
          break;
        case 'enter':
          console.log('Process hotkey pressed');
          await handleProcess();
          break;
        case 'r':
          console.log('Reset hotkey pressed');
          await handleReset();
          break;
        case 'p':
          if (isCmdOrCtrl) {
            console.log('Toggle config hotkey pressed');
            setShowConfig(prev => !prev);
          }
          break;
        case 'b':
          if (isCmdOrCtrl) {
            console.log('Toggle visibility hotkey pressed');
            // Toggle visibility logic here
          }
          break;
        case 'q':
          if (isCmdOrCtrl) {
            console.log('Quit hotkey pressed');
            handleQuit();
          }
          break;
      }
    };

    // Add keyboard event listener
    window.addEventListener('keydown', handleKeyDown);

    // Listen for processing complete events
    window.electron.onProcessingComplete((resultStr) => {
      console.log('Processing complete. Result:', resultStr);
      try {
        const parsedResult = JSON.parse(resultStr) as ProcessedSolution;
        setResult(parsedResult);
      } catch (error) {
        console.error('Error parsing result:', error);
      }
      setIsProcessing(false);
    });

    // Listen for new screenshots
    window.electron.onScreenshotTaken((screenshot) => {
      console.log('New screenshot taken:', screenshot);
      setScreenshots(prev => {
        const newScreenshots = [...prev, screenshot];
        console.log('Updated screenshots array:', newScreenshots);
        return newScreenshots;
      });
    });

    // Listen for queue reset
    window.electron.onQueueReset(() => {
      console.log('Queue reset triggered');
      setScreenshots([]);
      setResult(null);
    });

    // Cleanup
    return () => {
      console.log('Cleaning up event listeners...');
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, []);

  useEffect(() => {
    if (error) {
      const timer = setTimeout(() => {
        setError(null);
      }, 5000); // Hide error after 5 seconds
      return () => clearTimeout(timer);
    }
  }, [error]);

  const handleTakeScreenshot = async () => {
    console.log('Taking screenshot, current count:', screenshots.length);
    if (screenshots.length >= 4) {
      console.log('Maximum screenshots reached');
      return;
    }
    try {
      await window.electron.takeScreenshot();
      console.log('Screenshot taken successfully');
    } catch (error) {
      console.error('Error taking screenshot:', error);
    }
  };

  const handleProcess = async () => {
    console.log('Starting processing. Current screenshots:', screenshots);
    if (screenshots.length === 0) {
      console.log('No screenshots to process');
      return;
    }
    setIsProcessing(true);
    setResult(null);
    setError(null);
    try {
      await window.electron.processScreenshots();
      console.log('Process request sent successfully');
    } catch (error: any) {
      console.error('Error processing screenshots:', error);
      setError(error?.message || 'Error processing screenshots');
      setIsProcessing(false);
    }
  };

  const handleReset = async () => {
    console.log('Resetting queue...');
    await window.electron.resetQueue();
  };

  const handleQuit = () => {
    console.log('Quitting application...');
    window.electron.quit();
  };

  const handleConfigSave = async (newConfig: Config) => {
    setIsValidating(true);
    setError(null);
    try {
      const result = await window.electron.saveConfig(newConfig);
      if (result.success) {
        const updatedConfig = { ...newConfig, activeModel: result.model };
        setConfig(updatedConfig);
        setShowConfig(false);
        setSuccessMsg(`Saved! Connected to ${result.model}`);
        setTimeout(() => setSuccessMsg(null), 3000);
      } else {
        setError(result.error || 'Failed to save configuration');
      }
    } catch (error: any) {
      console.error('Error saving configuration:', error);
      setError(error?.message || 'Error saving configuration');
    } finally {
      setIsValidating(false);
    }
  };

  // Log state changes
  useEffect(() => {
    console.log('State update:', {
      isProcessing,
      result,
      screenshotCount: screenshots.length
    });
  }, [isProcessing, result, screenshots]);

  const formatCode = (code: string) => {
    return code.split('\n').map((line, index) => (
      <div key={index} className="code-line">
        <span className="line-number">{index + 1}</span>
        {line}
      </div>
    ));
  };

  return (
    <div className="app">
      {error && (
        <div className="error-bar">
          <span>{error}</span>
          <button onClick={() => setError(null)}>&times;</button>
        </div>
      )}

      {showConfig && (
        <ConfigScreen
          onSave={handleConfigSave}
          initialConfig={config || undefined}
          isValidating={isValidating}
        />
      )}

      {successMsg && (
        <div className="success-bar" style={{
          position: 'fixed', top: 20, left: '50%', transform: 'translateX(-50%)',
          background: 'rgba(40, 167, 69, 0.9)', color: 'white', padding: '10px 20px', borderRadius: 8, zIndex: 3000
        }}>
          {successMsg}
        </div>
      )}

      {/* Current Settings Indicator */}
      {config && !showConfig && (
        <div className="settings-indicator" style={{
          position: 'fixed', bottom: 10, right: 10,
          background: 'rgba(0,0,0,0.5)', padding: '5px 10px', borderRadius: 4, fontSize: '0.7em', color: '#888'
        }}>
          {config.mode?.toUpperCase() || 'AUTO'} | {config.language}
        </div>
      )}

      {/* Preview Row */}
      <div className="shortcuts-row">
        <div className="shortcut"><code>⌘/Ctrl + H</code> Screenshot</div>
        <div className="shortcut"><code>⌘/Ctrl + ↵</code> Solution</div>
        <div className="shortcut"><code>⌘/Ctrl + R</code> Reset</div>
        <div className="hover-shortcuts">
          <div className="hover-shortcuts-content">
            <div className="shortcut"><code>⌘/Ctrl + B</code> Show/Hide</div>
            <div className="shortcut"><code>⌘/Ctrl + P</code> Settings</div>
            <div className="shortcut"><code>⌘/Ctrl + Q</code> Quit</div>
            <div className="shortcut"><code>⌘/Ctrl + Arrow Keys</code> Move Around</div>
          </div>
        </div>
      </div>
      <div className="preview-row">
        {screenshots.map(screenshot => (
          <div key={screenshot.id} className="preview-item">
            <img src={screenshot.preview} alt="Screenshot preview" />
          </div>
        ))}
      </div>

      {/* Status Row */}
      <div className="status-row">
        {isProcessing ? (
          <div className="processing">Processing... ({screenshots.length} screenshots)</div>
        ) : result ? (
          <div className="result">
            {result.type === 'mcq' ? (
              <>
                <div className="solution-section">
                  <h3>Question</h3>
                  <p>{result.question}</p>
                </div>
                <div className="solution-section">
                  <h3>Options</h3>
                  <div className="mcq-options">
                    {result.options?.map((opt, i) => (
                      <div
                        key={i}
                        className={`mcq-option ${opt === result.correctOption ? 'correct' : ''}`}
                        style={{
                          padding: '8px',
                          margin: '4px 0',
                          borderRadius: '4px',
                          backgroundColor: opt === result.correctOption ? 'rgba(0, 255, 0, 0.2)' : 'rgba(255, 255, 255, 0.05)',
                          border: opt === result.correctOption ? '1px solid rgba(0, 255, 0, 0.5)' : '1px solid transparent'
                        }}
                      >
                        {opt}
                      </div>
                    ))}
                  </div>
                </div>
                <div className="solution-section">
                  <h3>Explanation</h3>
                  <p>{result.explanation}</p>
                </div>
              </>
            ) : (
              <>
                <div className="solution-section">
                  <h3>Approach</h3>
                  <p>{result.approach}</p>
                </div>
                <div className="solution-section">
                  <h3>Solution</h3>
                  <pre>
                    <code>{result.code && formatCode(result.code)}</code>
                  </pre>
                </div>
                <div className="solution-section">
                  <h3>Complexity</h3>
                  <p>Time: {result.timeComplexity}</p>
                  <p>Space: {result.spaceComplexity}</p>
                </div>
              </>
            )}
            <div className="hint">(Press ⌘/Ctrl + R to reset)</div>
          </div>
        ) : (
          <div className="empty-status">
            {screenshots.length > 0
              ? `Press ⌘/Ctrl + ↵ to process ${screenshots.length} screenshot${screenshots.length > 1 ? 's' : ''}`
              : 'Press ⌘/Ctrl + H to take a screenshot'}
          </div>
        )}
      </div>
    </div>
  );
};

export default App; 