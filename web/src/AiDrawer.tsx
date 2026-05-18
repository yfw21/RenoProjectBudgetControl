import { useEffect, useRef, useState } from 'react'
import type { User } from './App'
import { Panel, type PanelData } from './components/Panel'

type Msg =
  | { role: 'user' | 'bot'; content: string }
  | { role: 'panel'; payload: PanelData }

export function AiDrawer({ open, onClose, user }: { open: boolean; onClose: () => void; user: User }) {
  const [msgs, setMsgs] = useState<Msg[]>([
    { role: 'bot', content: `Hi ${user.username}. Ask me anything about your projects.` },
  ])
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const threadRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (open) threadRef.current?.scrollTo({ top: threadRef.current.scrollHeight, behavior: 'smooth' })
  }, [msgs, open])

  async function send(text: string) {
    if (!text.trim()) return
    setMsgs(m => [...m, { role: 'user', content: text }])
    setInput('')
    setBusy(true)
    try {
      const history = msgs
        .filter(m => m.role === 'user' || m.role === 'bot')
        .map(m => ({ role: m.role === 'bot' ? 'assistant' : 'user', content: (m as any).content }))
      const r = await fetch('/api/chat', {
        method: 'POST', credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: text, history }),
      })
      if (!r.ok) {
        setMsgs(m => [...m, { role: 'bot', content: `Error (${r.status}). Check that the LLM provider is configured in Settings.` }])
        return
      }
      const data = await r.json()
      const newMsgs: Msg[] = []
      for (const a of (data.actions ?? []) as Array<{ tool: string; result: any }>) {
        if (a.result && a.result._component) newMsgs.push({ role: 'panel', payload: a.result })
      }
      if (data.reply) newMsgs.push({ role: 'bot', content: data.reply })
      setMsgs(m => [...m, ...newMsgs])
    } catch (e: any) {
      setMsgs(m => [...m, { role: 'bot', content: `Error: ${e.message}` }])
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      {open && <div className="ai-backdrop" onClick={onClose} />}
      <aside className={`ai-drawer ${open ? 'open' : ''}`}>
        <div className="ai-head">
          <strong>AI assistant</strong>
          <button onClick={onClose} aria-label="Close">×</button>
        </div>
        <div className="ai-thread" ref={threadRef}>
          {msgs.map((m, i) => {
            if (m.role === 'panel') return <div key={i} className="msg panel"><Panel payload={m.payload} /></div>
            return <div key={i} className={`msg ${m.role}`}>{(m as any).content}</div>
          })}
          {busy && <div className="msg bot" style={{ color: 'var(--muted)', fontStyle: 'italic' }}>thinking…</div>}
        </div>
        <div className="composer">
          <input value={input} onChange={e => setInput(e.target.value)}
                 placeholder="Ask anything…" disabled={busy}
                 onKeyDown={e => e.key === 'Enter' && send(input)} />
          <button onClick={() => send(input)} disabled={busy || !input.trim()}>Send</button>
        </div>
      </aside>
    </>
  )
}
