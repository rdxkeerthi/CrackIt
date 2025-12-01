import React, { useState, useEffect, useRef } from "react"
import { ArrowLeft, Clock, MessageSquare, FileText, ChevronRight } from "lucide-react"
import DragHandle from "../components/ui/DragHandle"

interface HistoryItem {
    id: string
    timestamp: number
    type: "solution" | "chat"
    problemInfo?: any
    solution?: any
    messages?: { role: "user" | "gemini"; text: string }[]
}

interface HistoryProps {
    setView: React.Dispatch<React.SetStateAction<"main-menu" | "queue" | "solutions" | "debug" | "history" | "meeting-notes" | "api-setup" | "crackit" | "mcq">>
}

const History: React.FC<HistoryProps> = ({ setView }) => {
    const [history, setHistory] = useState<HistoryItem[]>([])
    const [isLoading, setIsLoading] = useState(false)
    const contentRef = useRef<HTMLDivElement>(null)

    useEffect(() => {
        loadHistory()
    }, [])

    const loadHistory = async () => {
        setIsLoading(true)
        try {
            const items = await window.electronAPI.getHistory()
            setHistory(items)
        } catch (error) {
            console.error("Failed to load history:", error)
        } finally {
            setIsLoading(false)
        }
    }

    const handleSelect = async (id: string) => {
        await window.electronAPI.loadHistoryItem(id)
        // The loadHistoryItem handler in main process will send events to switch view/restore state.
    }

    const formatTime = (timestamp: number) => {
        return new Date(timestamp).toLocaleString(undefined, {
            month: "short",
            day: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        })
    }

    // Height update logic
    useEffect(() => {
        const updateDimensions = () => {
            if (contentRef.current) {
                const contentHeight = contentRef.current.scrollHeight
                const contentWidth = contentRef.current.scrollWidth
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

        return () => {
            resizeObserver.disconnect()
        }
    }, [history])

    return (
        <div ref={contentRef} className="relative w-full">
            <DragHandle />
            <div className="p-4 space-y-4">
                {/* Header */}
                <div className="flex items-center justify-between liquid-glass-bar px-4">
                    <div className="flex items-center gap-3">
                        <button
                            onClick={() => setView("queue")}
                            className="p-1 hover:bg-transparent rounded-md transition-colors glass-text-tertiary hover:glass-text-primary"
                        >
                            <ArrowLeft className="w-4 h-4" />
                        </button>
                        <span className="text-xs font-medium glass-text-primary flex items-center gap-2">
                            <Clock className="w-3.5 h-3.5 glass-text-secondary" />
                            History
                        </span>
                    </div>
                </div>

                {/* Content */}
                <div className="liquid-glass p-2 min-h-[200px]">
                    {isLoading ? (
                        <div className="text-center py-8 text-gray-400 text-xs animate-pulse">
                            Loading history...
                        </div>
                    ) : history.length === 0 ? (
                        <div className="text-center py-8 text-gray-400 text-xs">
                            No history yet
                        </div>
                    ) : (
                        <div className="space-y-2">
                            {history.map((item) => (
                                <button
                                    key={item.id}
                                    onClick={() => handleSelect(item.id)}
                                    className="w-full text-left p-3 rounded-lg bg-white/5 hover:bg-white/10 border border-white/5 hover:border-white/10 transition-all group relative overflow-hidden"
                                >
                                    <div className="flex justify-between items-start mb-1 relative z-10">
                                        <span className="text-[10px] glass-text-secondary font-medium flex items-center gap-1.5">
                                            {item.type === "chat" ? (
                                                <MessageSquare className="w-3 h-3" />
                                            ) : (
                                                <FileText className="w-3 h-3" />
                                            )}
                                            {formatTime(item.timestamp)}
                                        </span>
                                        <ChevronRight className="w-3 h-3 text-gray-500 group-hover:text-gray-300 transition-colors" />
                                    </div>
                                    <p className="text-xs text-gray-300 line-clamp-2 leading-relaxed relative z-10 pl-4.5">
                                        {item.problemInfo?.problem_statement || (item.type === "chat" ? "Chat Session" : "No description")}
                                    </p>
                                </button>
                            ))}
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}

export default History
