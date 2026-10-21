import { useEffect, useRef, useState } from 'react'
import type { User } from './App'
import { Panel, type PanelData } from './components/Panel'

type Msg =
  | { role: 'user' | 'bot'; content: string }
  | { role: 'panel'; payload: PanelData }
  | { role: 'pending'; action: PendingAction; summary: string }

type PendingAction = { tool: string; args: any }

export function Chat({ user, onLogout }: { user: User; onLogout: () => void }) {
  const [msgs, setMsgs] = useState<Msg[]>([
    { role: 'bot', content: `Hi ${user.username}. Try: "show me a gantt of Bole", "list deliveries", or "give me a summary of Bole".` },
  ])
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [recording, setRecording] = useState(false)
  const [speakReplies, setSpeakReplies] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  const threadRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    threadRef.current?.scrollTo({ top: threadRef.current.scrollHeight, behavior: 'smooth' })
  }, [msgs])

  function speak(text: string) {
    if (!speakReplies || !window.speechSynthesis) return
    const u = new SpeechSynthesisUtterance(text)
    u.lang = 'en-US'
    window.speechSynthesis.cancel()
    window.speechSynthesis.speak(u)
  }

  async function send(text: string, opts?: { extraSystem?: string }) {
    if (!text.trim()) return
    setMsgs(m => [...m, { role: 'user', content: text }])
    setInput('')
    setBusy(true)
    try {
      const history = msgs
        .filter(m => m.role === 'user' || m.role === 'bot')
        .map(m => ({ role: m.role === 'bot' ? 'assistant' : 'user', content: (m as any).content }))
      const r = await fetch('/api/chat', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: text, history }),
      })
      if (!r.ok) {
        const err = await r.json().catch(() => null)
        setMsgs(m => [...m, { role: 'bot', content: err?.error ?? `Error (${r.status}).` }])
        return
      }
      const data = await r.json()
      const newMsgs: Msg[] = []
      for (const a of (data.actions ?? []) as Array<{ tool: string; result: any }>) {
        if (a.result && a.result._component) {
          newMsgs.push({ role: 'panel', payload: a.result })
        }
        if (a.result && a.result.pending && a.result.summary) {
          newMsgs.push({ role: 'pending', action: { tool: a.tool, args: a.result.args ?? {} }, summary: a.result.summary })
        }
      }
      if (data.reply) {
        newMsgs.push({ role: 'bot', content: data.reply })
        speak(data.reply)
      }
      setMsgs(m => [...m, ...newMsgs])
    } catch (e: any) {
      setMsgs(m => [...m, { role: 'bot', content: `Error: ${e.message}` }])
    } finally {
      setBusy(false)
    }
  }

  async function confirmPending(p: { action: PendingAction; summary: string }) {
    await send(`Yes, confirm: ${p.summary}`)
  }

  function startVoice() {
    const SR = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition
    if (!SR) { alert('Speech recognition not supported in this browser'); return }
    const rec = new SR()
    rec.lang = 'en-US'; rec.continuous = false; rec.interimResults = false
    rec.onresult = (e: any) => { setInput(e.results[0][0].transcript); setRecording(false) }
    rec.onerror = () => setRecording(false)
    rec.onend = () => setRecording(false)
    rec.start(); setRecording(true)
  }

  async function uploadFiles(files: File[]) {
    for (const f of files) {
      const fd = new FormData()
      fd.append('file', f)
      try {
        const r = await fetch('/api/files', { method: 'POST', credentials: 'include', body: fd })
        const data = await r.json()
        setMsgs(m => [...m, { role: 'bot', content: `Uploaded ${f.name}${data.indexed ? ' (indexed for search)' : ''}.` }])
      } catch (e: any) {
        setMsgs(m => [...m, { role: 'bot', content: `Upload failed: ${e.message}` }])
      }
    }
  }

  function handleDrop(e: React.DragEvent) {
    e.preventDefault(); setDragOver(false)
    const files = Array.from(e.dataTransfer.files)
    if (!files.length) return
    setMsgs(m => [...m, { role: 'user', content: `📎 ${files.map(f => f.name).join(', ')}` }])
    uploadFiles(files)
  }

  async function logout() {
    await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' })
    onLogout()
  }

  return (
    <div className="app"
         onDragOver={e => { e.preventDefault(); setDragOver(true) }}
         onDragLeave={() => setDragOver(false)}
         onDrop={handleDrop}>
      <div className="topbar">
        <strong>Reno Ops</strong>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <button className="who" onClick={() => setSpeakReplies(v => !v)}>{speakReplies ? '🔊 on' : '🔈 off'}</button>
          <button className="who" onClick={logout}>{user.username} · logout</button>
        </div>
      </div>
      <div className="thread" ref={threadRef}>
        {msgs.map((m, i) => {
          if (m.role === 'panel') return <div key={i} className="msg panel"><Panel payload={m.payload} /></div>
          if (m.role === 'pending') return (
            <div key={i} className="msg panel" style={{ borderColor: 'var(--accent)' }}>
              <div style={{ marginBottom: 8 }}>{m.summary}</div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button onClick={() => confirmPending(m)} style={{ padding: '6px 14px', background: 'var(--accent)', color: '#fff', border: 'none', borderRadius: 6, cursor: 'pointer' }}>Confirm</button>
                <button onClick={() => setMsgs(ms => ms.filter((_, j) => j !== i))} style={{ padding: '6px 14px', background: 'transparent', color: 'var(--muted)', border: '1px solid var(--border)', borderRadius: 6, cursor: 'pointer' }}>Cancel</button>
              </div>
            </div>
          )
          return <div key={i} className={`msg ${m.role}`}>{(m as any).content}</div>
        })}
        {busy && <div className="msg bot" style={{ color: 'var(--muted)', fontStyle: 'italic' }}>thinking…</div>}
        {dragOver && <div className="dropzone active">Drop file to upload</div>}
      </div>
      <div className="composer">
        <button className="icon" onClick={startVoice} title="Voice" disabled={busy}>{recording ? '⏺' : '🎤'}</button>
        <input value={input} onChange={e => setInput(e.target.value)}
               placeholder="Ask anything…" disabled={busy}
               onKeyDown={e => e.key === 'Enter' && send(input)} />
        <button onClick={() => send(input)} disabled={busy || !input.trim()}>Send</button>
      </div>
    </div>
  )
}
