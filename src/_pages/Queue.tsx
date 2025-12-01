import React, { useState, useEffect, useRef } from "react"
import { useQuery, useQueryClient } from "react-query"
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter'
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism'
import ScreenshotQueue from "../components/Queue/ScreenshotQueue"
import {
  Toast,
  ToastTitle,
  ToastDescription,
  ToastVariant,
  ToastMessage
} from "../components/ui/toast"
import QueueCommands from "../components/Queue/QueueCommands"
import ModelSelector from "../components/ui/ModelSelector"
import DragHandle from "../components/ui/DragHandle"

interface QueueProps {
  setView: React.Dispatch<React.SetStateAction<"main-menu" | "queue" | "solutions" | "debug" | "history" | "meeting-notes" | "api-setup" | "crackit" | "mcq">>
}

const Queue: React.FC<QueueProps> = ({ setView }) => {
  const queryClient = useQueryClient()
  const [toastOpen, setToastOpen] = useState(false)
  const [toastMessage, setToastMessage] = useState<ToastMessage>({
    title: "",
    description: "",
    variant: "neutral"
  })

  const [isTooltipVisible, setIsTooltipVisible] = useState(false)
  const [tooltipHeight, setTooltipHeight] = useState(0)
  const contentRef = useRef<HTMLDivElement>(null)

  const [chatInput, setChatInput] = useState("")
  const [chatMessages, setChatMessages] = useState<{ role: "user" | "gemini", text: string }[]>([])
  const [chatLoading, setChatLoading] = useState(false)
  const [isChatOpen, setIsChatOpen] = useState(false)
  const chatInputRef = useRef<HTMLInputElement>(null)

  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [currentModel, setCurrentModel] = useState<{ provider: string; model: string }>({ provider: "gemini", model: "gemini-2.0-flash" })

  const barRef = useRef<HTMLDivElement>(null)

  const { data: screenshots = [], refetch } = useQuery<Array<{ path: string; preview: string }>, Error>(
    ["screenshots"],
    async () => {
      try {
        const existing = await window.electronAPI.getScreenshots()
        return existing
      } catch (error) {
        console.error("Error loading screenshots:", error)
        showToast("Error", "Failed to load existing screenshots", "error")
        return []
      }
    },
    {
      staleTime: Infinity,
      cacheTime: Infinity,
      refetchOnWindowFocus: true,
      refetchOnMount: true
    }
  )

  const showToast = (
    title: string,
    description: string,
    variant: ToastVariant
  ) => {
    setToastMessage({ title, description, variant })
    setToastOpen(true)
  }

  const handleDeleteScreenshot = async (index: number) => {
    const screenshotToDelete = screenshots[index]

    try {
      const response = await window.electronAPI.deleteScreenshot(
        screenshotToDelete.path
      )

      if (response.success) {
        refetch()
      } else {
        console.error("Failed to delete screenshot:", response.error)
        showToast("Error", "Failed to delete the screenshot file", "error")
      }
    } catch (error) {
      console.error("Error deleting screenshot:", error)
    }
  }

  const handleChatSend = async () => {
    if (!chatInput.trim()) return
    setChatMessages((msgs) => [...msgs, { role: "user", text: chatInput }])
    setChatLoading(true)
    setChatInput("")
    try {
      const response = await window.electronAPI.invoke("gemini-chat", chatInput)
      setChatMessages((msgs) => [...msgs, { role: "gemini", text: response }])
    } catch (err) {
      setChatMessages((msgs) => [...msgs, { role: "gemini", text: "Error: " + String(err) }])
    } finally {
      setChatLoading(false)
      chatInputRef.current?.focus()
    }
  }

  // Load current model configuration on mount
  useEffect(() => {
    const loadCurrentModel = async () => {
      try {
        const config = await window.electronAPI.getCurrentLlmConfig();
        setCurrentModel({ provider: config.provider, model: config.model });
      } catch (error) {
        console.error('Error loading current model config:', error);
      }
    };
    loadCurrentModel();
  }, []);

  // Initialize chat from restored history if available
  useEffect(() => {
    const restoredMessages = queryClient.getQueryData<any[]>(["restored_chat"])
    if (restoredMessages) {
      setChatMessages(restoredMessages)
      setIsChatOpen(true)
      // Clear the query data so it doesn't persist on reload/navigation
      queryClient.setQueryData(["restored_chat"], null)
    }
  }, [queryClient])

  // Chat History Persistence
  const chatSessionId = useRef<string | null>(null);

  useEffect(() => {
    if (chatMessages.length > 0) {
      const saveChat = async () => {
        const historyItem = {
          id: chatSessionId.current || Date.now().toString(),
          timestamp: Date.now(),
          type: "chat",
          messages: chatMessages,
          problemInfo: {
            problem_statement: chatMessages[0].text.substring(0, 50) + "..." // Use first message as title
          }
        }

        if (!chatSessionId.current) {
          chatSessionId.current = historyItem.id
        }

        try {
          await window.electronAPI.saveHistoryItem(historyItem);
        } catch (error) {
          console.error("Failed to save chat history:", error);
        }
      };

      const timeoutId = setTimeout(saveChat, 1000)
      return () => clearTimeout(timeoutId)
    }
  }, [chatMessages]);

  useEffect(() => {
    const updateDimensions = () => {
      if (contentRef.current) {
        let contentHeight = contentRef.current.scrollHeight
        const contentWidth = contentRef.current.scrollWidth
        if (isTooltipVisible) {
          contentHeight += tooltipHeight
        }
        window.electronAPI.updateContentDimensions({
          width: contentWidth,
          height: contentHeight
        })
      }
    }

    const resizeObserver = new ResizeObserver(updateDimensions)
    if (contentRef.current) {
      resizeObserver.observe(contentRef.current)
    }
    updateDimensions()

    const cleanupFunctions = [
      window.electronAPI.onResetView(() => refetch()),
      window.electronAPI.onSolutionError((error: string) => {
        showToast(
          "Processing Failed",
          "There was an error processing your screenshots.",
          "error"
        )
        setView("queue")
        console.error("Processing error:", error)
      }),
      window.electronAPI.onProcessingNoScreenshots(() => {
        showToast(
          "No Screenshots",
          "There are no screenshots to process.",
          "neutral"
        )
      })
    ]

    return () => {
      resizeObserver.disconnect()
      cleanupFunctions.forEach((cleanup) => cleanup())
    }
  }, [isTooltipVisible, tooltipHeight])

  // Seamless screenshot-to-LLM flow
  useEffect(() => {
    // Listen for screenshot taken event
    const unsubscribe = window.electronAPI.onScreenshotTaken(async (data) => {
      // Refetch screenshots to update the queue
      await refetch();
      // Show loading in chat
      setChatLoading(true);
      try {
        // Get the latest screenshot path
        const latest = data?.path || (Array.isArray(data) && data.length > 0 && data[data.length - 1]?.path);
        if (latest) {
          // Call the LLM to process the screenshot
          const response = await window.electronAPI.invoke("analyze-image-file", latest);
          setChatMessages((msgs) => [...msgs, { role: "gemini", text: response.text }]);
        }
      } catch (err) {
        setChatMessages((msgs) => [...msgs, { role: "gemini", text: "Error: " + String(err) }]);
      } finally {
        setChatLoading(false);
      }
    });
    return () => {
      unsubscribe && unsubscribe();
    };
  }, [refetch]);

  const handleTooltipVisibilityChange = (visible: boolean, height: number) => {
    setIsTooltipVisible(visible)
    setTooltipHeight(height)
  }

  const handleChatToggle = () => {
    setIsChatOpen(!isChatOpen)
  }

  const handleSettingsToggle = () => {
    setIsSettingsOpen(!isSettingsOpen)
  }

  const handleModelChange = (provider: "ollama" | "gemini", model: string) => {
    setCurrentModel({ provider, model })
    // Update chat messages to reflect the model change
    const modelName = provider === "ollama" ? model : "Gemini 2.0 Flash"
    setChatMessages((msgs) => [...msgs, {
      role: "gemini",
      text: `🔄 Switched to ${provider === "ollama" ? "🏠" : "☁️"} ${modelName}. Ready for your questions!`
    }])
  }


  return (
    <div
      ref={barRef}
      style={{
        position: "relative",
        width: "100%",
        pointerEvents: "auto"
      }}
      className="select-none"
    >
      <DragHandle />
      <div className="bg-transparent w-full">
        <div className="px-2 py-1">
          <Toast
            open={toastOpen}
            onOpenChange={setToastOpen}
            variant={toastMessage.variant}
            duration={3000}
          >
            <ToastTitle>{toastMessage.title}</ToastTitle>
            <ToastDescription>{toastMessage.description}</ToastDescription>
          </Toast>
          <div className="w-fit">
            <QueueCommands
              screenshots={screenshots}
              onTooltipVisibilityChange={handleTooltipVisibilityChange}
              onChatToggle={handleChatToggle}
              onSettingsToggle={handleSettingsToggle}
              onMeetingNotesClick={() => setView("meeting-notes")}
            />
          </div>
          {/* Conditional Settings Interface */}
          {isSettingsOpen && (
            <div className="mt-4 w-full mx-auto">
              <ModelSelector onModelChange={handleModelChange} onChatOpen={() => setIsChatOpen(true)} />
            </div>
          )}

          {/* Conditional Chat Interface */}
          {isChatOpen && (
            <div className="mt-4 w-full mx-auto liquid-glass chat-container p-4 flex flex-col">
              <div className="flex-1 overflow-y-auto mb-3 p-3 rounded-lg glass-card max-h-64 min-h-[120px] glass-content border border-white/20 shadow-lg">
                {chatMessages.length === 0 ? (
                  <div className="text-sm text-gray-600 text-center mt-8">
                    💬 Chat with {currentModel.provider === "ollama" ? "🏠" : "☁️"} {currentModel.model}
                    <br />
                    <span className="text-xs text-gray-500">Take a screenshot (Cmd+H) for automatic analysis</span>
                    <br />
                    <span className="text-xs text-gray-500">Click ⚙️ Models to switch AI providers</span>
                  </div>
                ) : (
                  chatMessages.map((msg, idx) => (
                    <div
                      key={idx}
                      className={`w-full flex ${msg.role === "user" ? "justify-end" : "justify-start"} mb-3`}
                    >
                      <div
                        className={`max-w-[85%] px-3 py-2 rounded-xl text-xs shadow-md glass-badge border ${msg.role === "user"
                          ? "bg-gray-700/90 text-gray-100 ml-12 border-gray-600/40"
                          : "bg-white/90 text-gray-800 mr-12 border-gray-200/50"
                          }`}
                        style={{ wordBreak: "break-word", lineHeight: "1.5" }}
                      >
                        {msg.role === "user" ? (
                          msg.text
                        ) : (
                          <div className="markdown-content text-sm">
                            <ReactMarkdown
                              remarkPlugins={[remarkGfm]}
                              components={{
                                p: ({ node, ...props }) => <p className="mb-3 last:mb-0 leading-relaxed text-gray-800" {...props} />,
                                ul: ({ node, ...props }) => <ul className="list-disc pl-5 mb-3 space-y-1 text-gray-800" {...props} />,
                                ol: ({ node, ...props }) => <ol className="list-decimal pl-5 mb-3 space-y-1 text-gray-800" {...props} />,
                                li: ({ node, ...props }) => <li className="pl-1" {...props} />,
                                h1: ({ node, ...props }) => <h1 className="text-lg font-bold mb-3 mt-4 text-gray-900 border-b border-gray-200 pb-1" {...props} />,
                                h2: ({ node, ...props }) => <h2 className="text-base font-bold mb-2 mt-4 text-gray-900" {...props} />,
                                h3: ({ node, ...props }) => <h3 className="text-sm font-bold mb-2 mt-3 text-gray-900" {...props} />,
                                strong: ({ node, ...props }) => <strong className="font-bold text-gray-900" {...props} />,
                                code: ({ node, inline, className, children, ...props }: any) => {
                                  const match = /language-(\w+)/.exec(className || '')
                                  return !inline && match ? (
                                    <div className="rounded-lg overflow-hidden my-3 shadow-md">
                                      <div className="px-3 py-1.5 bg-[#1e1e1e] text-xs text-gray-400 border-b border-gray-700 flex justify-between items-center">
                                        <span className="font-mono">{match[1]}</span>
                                      </div>
                                      <SyntaxHighlighter
                                        style={vscDarkPlus}
                                        language={match[1]}
                                        PreTag="div"
                                        customStyle={{ margin: 0, borderRadius: 0, fontSize: '12px', lineHeight: '1.5' }}
                                        {...props}
                                      >
                                        {String(children).replace(/\n$/, '')}
                                      </SyntaxHighlighter>
                                    </div>
                                  ) : (
                                    <code className="bg-gray-100 px-1.5 py-0.5 rounded text-xs font-mono text-pink-600 border border-gray-200" {...props}>
                                      {children}
                                    </code>
                                  )
                                },
                                blockquote: ({ node, ...props }) => <blockquote className="border-l-4 border-blue-400 pl-3 italic text-gray-600 my-3 bg-gray-50/50 py-1 rounded-r" {...props} />,
                                a: ({ node, ...props }) => <a className="text-blue-600 hover:underline font-medium" target="_blank" rel="noopener noreferrer" {...props} />,
                              }}
                            >
                              {msg.text}
                            </ReactMarkdown>
                          </div>
                        )}
                      </div>
                    </div>
                  ))
                )}
                {chatLoading && (
                  <div className="flex justify-start mb-3">
                    <div className="bg-white/85 text-gray-600 px-3 py-1.5 rounded-xl text-xs backdrop-blur-sm border border-gray-200/50 shadow-md mr-12">
                      <span className="inline-flex items-center">
                        <span className="animate-pulse text-gray-400">●</span>
                        <span className="animate-pulse animation-delay-200 text-gray-400">●</span>
                        <span className="animate-pulse animation-delay-400 text-gray-400">●</span>
                        <span className="ml-2">{currentModel.model} is replying...</span>
                      </span>
                    </div>
                  </div>
                )}
              </div>
              <form
                className="flex gap-2 items-center glass-content"
                onSubmit={e => {
                  e.preventDefault();
                  handleChatSend();
                }}
              >
                <input
                  ref={chatInputRef}
                  className="flex-1 rounded-lg px-3 py-2 glass-input text-gray-800 placeholder-gray-500 text-xs focus:outline-none focus:ring-1 focus:ring-gray-400/60 border border-white/40 shadow-lg transition-all duration-200"
                  placeholder="Type your message..."
                  value={chatInput}
                  onChange={e => setChatInput(e.target.value)}
                  disabled={chatLoading}
                />
                <button
                  type="submit"
                  className="p-2 rounded-lg glass-button hover:bg-gray-700/80 border border-gray-500/60 flex items-center justify-center transition-all duration-200 backdrop-blur-sm shadow-lg disabled:opacity-50"
                  disabled={chatLoading || !chatInput.trim()}
                  tabIndex={-1}
                  aria-label="Send"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="white" className="w-4 h-4">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 19.5l15-7.5-15-7.5v6l10 1.5-10 1.5v6z" />
                  </svg>
                </button>
              </form>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

export default Queue
