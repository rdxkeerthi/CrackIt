import React, { useState, useRef, useEffect } from "react"
import { Mic, Square, Clock, FileText, ArrowLeft } from "lucide-react"
import ReactMarkdown from "react-markdown"
import remarkGfm from "remark-gfm"
import DragHandle from "../components/ui/DragHandle"

interface MeetingNotesProps {
    setView: React.Dispatch<React.SetStateAction<"main-menu" | "queue" | "solutions" | "debug" | "history" | "meeting-notes" | "api-setup" | "crackit" | "mcq">>
}

const MeetingNotes: React.FC<MeetingNotesProps> = ({ setView }) => {
    const [isRecording, setIsRecording] = useState(false)
    const [recordingTime, setRecordingTime] = useState(0)
    const [status, setStatus] = useState<"idle" | "recording" | "processing" | "completed">("idle")
    const [notes, setNotes] = useState<string>("")
    const mediaRecorderRef = useRef<MediaRecorder | null>(null)
    const timerRef = useRef<NodeJS.Timeout | null>(null)

    const formatTime = (seconds: number) => {
        const mins = Math.floor(seconds / 60)
        const secs = seconds % 60
        return `${mins.toString().padStart(2, "0")}:${secs.toString().padStart(2, "0")}`
    }

    const startRecording = async () => {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
            const mediaRecorder = new MediaRecorder(stream)
            mediaRecorderRef.current = mediaRecorder
            const chunks: Blob[] = []

            mediaRecorder.ondataavailable = (e) => {
                if (e.data.size > 0) chunks.push(e.data)
            }

            mediaRecorder.onstop = async () => {
                const blob = new Blob(chunks, { type: "audio/webm" })
                const reader = new FileReader()
                reader.onloadend = async () => {
                    const base64Data = (reader.result as string).split(",")[1]
                    processAudio(base64Data)
                }
                reader.readAsDataURL(blob)

                // Stop all tracks
                stream.getTracks().forEach(track => track.stop())
            }

            mediaRecorder.start()
            setIsRecording(true)
            setStatus("recording")

            timerRef.current = setInterval(() => {
                setRecordingTime(prev => prev + 1)
            }, 1000)

        } catch (err) {
            console.error("Error starting recording:", err)
            setStatus("idle")
        }
    }

    const stopRecording = () => {
        if (mediaRecorderRef.current && isRecording) {
            mediaRecorderRef.current.stop()
            setIsRecording(false)
            setStatus("processing")
            if (timerRef.current) {
                clearInterval(timerRef.current)
                timerRef.current = null
            }
        }
    }

    const processAudio = async (base64Data: string) => {
        try {
            const result = await window.electronAPI.invoke("summarize-meeting", base64Data)
            setNotes(result)
            setStatus("completed")
        } catch (error) {
            console.error("Error processing meeting notes:", error)
            setNotes("Error generating meeting notes. Please try again.")
            setStatus("idle")
        }
    }

    useEffect(() => {
        return () => {
            if (timerRef.current) clearInterval(timerRef.current)
            if (mediaRecorderRef.current && isRecording) {
                mediaRecorderRef.current.stop()
            }
        }
    }, [isRecording])

    return (
        <div className="relative w-full min-h-[300px] flex flex-col">
            <DragHandle />

            <div className="p-4 flex flex-col h-full">
                {/* Header */}
                <div className="flex items-center justify-between mb-6">
                    <div className="flex items-center gap-2">
                        <button
                            onClick={() => setView("solutions")}
                            className="p-1.5 hover:bg-transparent rounded-lg transition-colors glass-text-tertiary hover:glass-text-primary"
                        >
                            <ArrowLeft className="w-4 h-4" />
                        </button>
                        <h1 className="text-sm font-medium glass-text-primary flex items-center gap-2">
                            <FileText className="w-4 h-4 glass-text-secondary" />
                            Meeting Notes
                        </h1>
                    </div>
                    {status === "recording" && (
                        <div className="flex items-center gap-2 px-3 py-1 bg-red-500/20 rounded-full border border-red-500/30">
                            <div className="w-2 h-2 rounded-full bg-red-500 animate-pulse" />
                            <span className="text-xs font-mono text-red-200">{formatTime(recordingTime)}</span>
                        </div>
                    )}
                </div>

                {/* Main Action Area */}
                <div className="flex-1 flex flex-col items-center justify-center gap-6">
                    {status === "idle" || status === "completed" ? (
                        <button
                            onClick={startRecording}
                            className="group relative flex items-center justify-center w-20 h-20 rounded-full glass-card hover:shadow-lg transition-all hover:scale-105"
                        >
                            <Mic className="w-8 h-8 glass-text-primary" />
                            <span className="absolute -bottom-8 text-xs text-gray-400 opacity-0 group-hover:opacity-100 transition-opacity">
                                Start Recording
                            </span>
                        </button>
                    ) : status === "recording" ? (
                        <button
                            onClick={stopRecording}
                            className="group relative flex items-center justify-center w-20 h-20 rounded-full bg-red-500 hover:bg-red-400 transition-all shadow-lg hover:shadow-red-500/25 hover:scale-105"
                        >
                            <Square className="w-8 h-8 glass-text-primary fill-current" />
                            <span className="absolute -bottom-8 text-xs text-gray-400 opacity-0 group-hover:opacity-100 transition-opacity">
                                Stop Recording
                            </span>
                        </button>
                    ) : (
                        <div className="flex flex-col items-center gap-3">
                            <div className="w-16 h-16 rounded-full border-4 border-blue-500/30 border-t-blue-500 animate-spin" />
                            <span className="text-xs text-blue-300 animate-pulse">Summarizing meeting...</span>
                        </div>
                    )}

                    {/* Notes Display */}
                    {notes && (
                        <div className="w-full mt-6 glass-card rounded-xl border border-white/10 overflow-hidden">
                            <div className="px-4 py-2 glass-badge border-b border-white/5 flex items-center gap-2">
                                <FileText className="w-3.5 h-3.5 text-gray-400" />
                                <span className="text-xs font-medium text-gray-300">Generated Summary</span>
                            </div>
                            <div className="p-4 max-h-[300px] overflow-y-auto custom-scrollbar">
                                <ReactMarkdown
                                    remarkPlugins={[remarkGfm]}
                                    className="prose prose-invert prose-sm max-w-none prose-headings:text-blue-300 prose-strong:text-gray-200 prose-p:text-gray-300 prose-li:text-gray-300"
                                >
                                    {notes}
                                </ReactMarkdown>
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}

export default MeetingNotes
