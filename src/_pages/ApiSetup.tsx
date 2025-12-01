import React, { useEffect, useState } from "react"

const ApiSetup: React.FC<{ onDone?: () => void }> = ({ onDone }) => {
  const [apiKeys, setApiKeys] = useState<any[]>([])
  const [provider, setProvider] = useState<string>("openai")
  const [key, setKey] = useState<string>("")
  const [label, setLabel] = useState<string>("")
  const [currentId, setCurrentId] = useState<string | null>(null)
  const [status, setStatus] = useState<string | null>(null)

  useEffect(() => {
    const load = async () => {
      try {
        const res = await (window as any).electronAPI.getApiKeys()
        setApiKeys(res.apiKeys || [])
        setCurrentId(res.currentKeyId || null)
      } catch (err) {
        console.error("Error loading api keys", err)
      }
    }
    load()
  }, [])

  const handleSave = async () => {
    setStatus("Saving...")
    try {
      const entry = { provider, key, label }
      const res = await (window as any).electronAPI.saveApiKey(entry)
      if (res.success) {
        setApiKeys(res.apiKeys)
        setCurrentId(res.currentKeyId)
        setStatus("Saved and selected")
        setKey("")
        setLabel("")
        try {
          // Ask main process to show the main window (in case it was hidden)
          await (window as any).electronAPI.invoke('show-main-window')
        } catch (e) {
          console.warn('Failed to show main window after API save:', e)
        }
        if (onDone) setTimeout(() => onDone(), 500)
      } else {
        setStatus("Failed to save key")
      }
    } catch (err) {
      console.error(err)
      setStatus("Error saving key")
    }
  }

  const handleDelete = async (id: string) => {
    try {
      const res = await (window as any).electronAPI.deleteApiKey(id)
      if (res.success) {
        setApiKeys(res.apiKeys)
        setCurrentId(res.currentKeyId)
      }
    } catch (err) {
      console.error(err)
    }
  }

  const handleSelect = async (id: string) => {
    try {
      const res = await (window as any).electronAPI.setCurrentApiKey(id)
      if (res.success) setCurrentId(res.currentKeyId)
    } catch (err) {
      console.error(err)
    }
  }

  const handleTest = async () => {
    setStatus("Testing connection...")
    try {
      const res = await (window as any).electronAPI.testLlmConnection()
      if (res && res.success) setStatus("Connection OK")
      else setStatus(res?.error || "Connection failed")
    } catch (err) {
      console.error(err)
      setStatus("Test failed")
    }
  }

  return (
    <div className="p-4">
      <h2 className="text-lg font-semibold">API Key Setup</h2>

      <div className="mt-4">
        <label className="block">Provider</label>
        <select value={provider} onChange={(e) => setProvider(e.target.value)}>
          <option value="openai">OpenAI</option>
          <option value="gemini">Google Gemini</option>
          <option value="anthropic">Anthropic</option>
          <option value="ollama">Ollama (local)</option>
        </select>
      </div>

      <div className="mt-2">
        <label className="block">Label (optional)</label>
        <input value={label} onChange={(e) => setLabel(e.target.value)} placeholder="My OpenAI Key" />
      </div>

      <div className="mt-2">
        <label className="block">API Key</label>
        <input value={key} onChange={(e) => setKey(e.target.value)} placeholder="sk-... or other key" />
      </div>

      <div className="mt-4">
        <button onClick={handleSave} className="btn">Save & Select</button>
        <button onClick={handleTest} className="btn ml-2">Test Current</button>
      </div>

      {status && <div className="mt-2">Status: {status}</div>}

      <hr className="my-4" />

      <h3 className="font-semibold">Saved Keys</h3>
      <div className="mt-2">
        {apiKeys.length === 0 && <div>No keys saved</div>}
        <ul>
          {apiKeys.map((k: any) => (
            <li key={k.id} className="py-1 flex items-center justify-between">
              <div>
                <strong>{k.label || `${k.provider} (${k.id.slice(0,6)})`}</strong>
                <div className="text-sm text-gray-500">{k.provider}</div>
              </div>
              <div className="flex items-center gap-2">
                <button className="btn" onClick={() => handleSelect(k.id)} disabled={currentId === k.id}>{currentId === k.id ? "Selected" : "Select"}</button>
                <button className="btn btn-danger" onClick={() => handleDelete(k.id)}>Delete</button>
              </div>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}

export default ApiSetup
