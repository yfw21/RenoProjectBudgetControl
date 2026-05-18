import { useEffect, useState } from 'react'
import { get, post, del } from '../api'
import { useProjects } from '../state/ProjectContext'

type Note = { id: number; projectId?: number | null; text: string; source: string; fileBlobId?: number | null; createdAt: string }

export function Notes() {
  const { current } = useProjects()
  const [notes, setNotes] = useState<Note[]>([])
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)

  async function load() {
    const url = current ? `/api/notes?projectId=${current.id}` : '/api/notes'
    setNotes(await get(url))
  }
  useEffect(() => { load() }, [current])

  async function add() {
    if (!text.trim()) return
    setBusy(true)
    try {
      await post('/api/notes', { projectId: current?.id ?? null, text })
      setText('')
      await load()
    } finally { setBusy(false) }
  }

  async function upload(files: FileList | null) {
    if (!files || files.length === 0) return
    setBusy(true)
    try {
      for (const f of Array.from(files)) {
        const fd = new FormData()
        fd.append('file', f)
        if (current) fd.append('projectId', String(current.id))
        await fetch('/api/files', { method: 'POST', credentials: 'include', body: fd })
      }
      await load()
    } finally { setBusy(false) }
  }

  async function remove(id: number) {
    if (!confirm('Delete note?')) return
    await del(`/api/notes/${id}`)
    await load()
  }

  return (
    <div className="page">
      <div className="page-head">
        <h1>Notes {current && `— ${current.name}`}</h1>
      </div>

      <div className="card">
        <textarea value={text} onChange={e => setText(e.target.value)} placeholder="Write a note…" rows={3} style={{ width: '100%', background: 'var(--bg)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: 6, padding: 10 }} />
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 8 }}>
          <label className="btn-ghost" style={{ cursor: 'pointer' }}>
            📎 Upload file
            <input type="file" multiple style={{ display: 'none' }} onChange={e => upload(e.target.files)} />
          </label>
          <button className="btn-primary" onClick={add} disabled={busy || !text.trim()}>Add note</button>
        </div>
      </div>

      <div className="notes-list">
        {notes.length === 0 && <p className="muted">No notes yet.</p>}
        {notes.map(n => (
          <div key={n.id} className="note-item">
            <div className="note-meta">
              <span className="muted">{new Date(n.createdAt).toLocaleString()}</span>
              <span className="muted"> · {n.source}</span>
              <button className="btn-ghost danger" style={{ float: 'right' }} onClick={() => remove(n.id)}>×</button>
            </div>
            <div className="note-text">{n.text.length > 600 ? n.text.slice(0, 600) + '…' : n.text}</div>
            {n.fileBlobId && <a className="note-link" href={`/api/files/${n.fileBlobId}`} target="_blank">Download file</a>}
          </div>
        ))}
      </div>
    </div>
  )
}
