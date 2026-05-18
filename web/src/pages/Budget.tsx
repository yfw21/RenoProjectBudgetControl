import { useEffect, useRef, useState } from 'react'
import { get, put, del, post } from '../api'
import { useProjects } from '../state/ProjectContext'

type Line = { id: number; description: string; budget: number; spent: number; percentComplete: number; earned: number; status: string | null }
type Category = { category: string; totalBudget: number; totalSpent: number; totalEarned: number; lineCount: number; lines: Line[] }
type BudgetView = { project: string; totalBudget: number; totalSpent: number; totalEarned: number; categoryCount: number; lineCount: number; categories: Category[] }
type Flag = { lineId: number; description: string; issue: string; suggestion: string }
type EditProposal = { description: string | null; category: string | null; budget: number | null; actual: number | null; status: string | null; summary: string }

export function Budget() {
  const { current } = useProjects()
  const [data, setData] = useState<BudgetView | null>(null)
  const [loading, setLoading] = useState(false)
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})
  const [editing, setEditing] = useState<Line | null>(null)
  const [importing, setImporting] = useState(false)
  const [importMode, setImportMode] = useState<'append' | 'replace'>('append')
  const importInputRef = useRef<HTMLInputElement>(null)

  // AI validation
  const [validating, setValidating] = useState(false)
  const [flags, setFlags] = useState<Flag[]>([])

  // Voice/text edit
  const [voiceLine, setVoiceLine] = useState<Line | null>(null)
  const [voiceText, setVoiceText] = useState('')
  const [voiceRecording, setVoiceRecording] = useState(false)
  const [voiceParsing, setVoiceParsing] = useState(false)
  const [proposal, setProposal] = useState<EditProposal | null>(null)

  async function load() {
    if (!current) { setData(null); return }
    setLoading(true)
    try { setData(await get<BudgetView>(`/api/budget?projectId=${current.id}`)) }
    finally { setLoading(false) }
  }
  useEffect(() => { load() }, [current])

  async function importExcel(file: File) {
    if (!current) return
    setImporting(true)
    try {
      const fd = new FormData()
      fd.append('file', file)
      fd.append('projectId', String(current.id))
      fd.append('replaceExisting', importMode === 'replace' ? 'true' : 'false')
      const r = await fetch('/api/imports/budget', { method: 'POST', credentials: 'include', body: fd })
      const result = await r.json()
      if (!r.ok) { alert(`Import failed: ${result.error || r.statusText}`); return }
      const cats = result.categories?.length ? `\n\nCategories: ${result.categories.join(', ')}` : ''
      alert(`Imported ${result.inserted} lines${result.skipped ? `, skipped ${result.skipped}` : ''}.${cats}`)
      setFlags([])
      await load()
    } finally {
      setImporting(false)
      if (importInputRef.current) importInputRef.current.value = ''
    }
  }

  async function runValidation() {
    if (!current) return
    setValidating(true)
    try {
      const r = await fetch(`/api/budget/validate?projectId=${current.id}`, { method: 'POST', credentials: 'include' })
      const data = await r.json()
      if (!r.ok) { alert(`Validation failed: ${data.error || r.statusText}`); return }
      setFlags(data.flags || [])
      if ((data.flags?.length ?? 0) === 0) alert('Nothing flagged — budget looks consistent.')
    } catch (e: any) {
      alert(`Validation failed: ${e.message}`)
    } finally {
      setValidating(false)
    }
  }

  async function saveLine() {
    if (!editing) return
    await put(`/api/budget/line/${editing.id}`, {
      description: editing.description,
      budget: editing.budget,
      status: editing.status,
      percentComplete: editing.percentComplete,
    })
    setEditing(null)
    await load()
  }

  async function deleteLine(id: number) {
    if (!confirm('Delete this budget line?')) return
    await del(`/api/budgetitems/${id}`)
    await load()
  }

  function openVoiceEdit(line: Line) {
    setVoiceLine(line)
    setVoiceText('')
    setProposal(null)
  }

  async function parseVoiceEdit() {
    if (!voiceLine || !voiceText.trim()) return
    setVoiceParsing(true)
    try {
      const r = await fetch(`/api/budget/line/${voiceLine.id}/edit-text`, {
        method: 'POST', credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: voiceText.trim() }),
      })
      const data = await r.json()
      if (!r.ok) { alert(`Parse failed: ${data.error || r.statusText}`); return }
      setProposal(data)
    } finally {
      setVoiceParsing(false)
    }
  }

  async function applyProposal() {
    if (!voiceLine || !proposal) return
    const payload: any = {}
    if (proposal.description != null) payload.description = proposal.description
    if (proposal.category != null) payload.category = proposal.category
    if (proposal.budget != null) payload.budget = proposal.budget
    if (proposal.actual != null) payload.actual = proposal.actual
    if (proposal.status != null) payload.status = proposal.status
    if (Object.keys(payload).length === 0) { alert('Nothing to apply.'); return }
    await put(`/api/budget/line/${voiceLine.id}`, payload)
    setVoiceLine(null)
    setVoiceText('')
    setProposal(null)
    await load()
  }

  function startVoice() {
    const SR = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition
    if (!SR) { alert('Speech recognition not supported in this browser. Use Chrome or Edge.'); return }
    const rec = new SR()
    rec.lang = 'en-US'; rec.continuous = false; rec.interimResults = false
    rec.onresult = (e: any) => { setVoiceText(prev => (prev + ' ' + e.results[0][0].transcript).trim()); setVoiceRecording(false) }
    rec.onerror = () => setVoiceRecording(false)
    rec.onend = () => setVoiceRecording(false)
    rec.start()
    setVoiceRecording(true)
  }

  if (!current) return <div className="page"><h1>Budget</h1><p className="muted">Select a project first.</p></div>

  const variance = data ? data.totalBudget - data.totalSpent : 0
  const variancePct = data && data.totalBudget > 0 ? (variance / data.totalBudget) * 100 : 0
  const flagsByLine = new Map(flags.map(f => [f.lineId, f]))

  return (
    <div className="page">
      <div className="page-head">
        <h1>Budget — {current.name}</h1>
        <div className="toolbar">
          <button className="btn-ghost" onClick={runValidation} disabled={validating || !data || data.lineCount === 0}
                  title="Have the AI scan all lines for inconsistencies">
            {validating ? '… checking' : '✦ Validate with AI'}
          </button>
          <select value={importMode} onChange={e => setImportMode(e.target.value as any)}
                  style={{ background: 'var(--bg)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: 6, padding: '6px 10px', fontSize: 13 }}>
            <option value="append">Append to existing</option>
            <option value="replace">Replace planned lines</option>
          </select>
          <button className="btn-primary" onClick={() => importInputRef.current?.click()} disabled={importing}>
            {importing ? '… importing' : '⤒ Import Excel'}
          </button>
          <input ref={importInputRef} type="file" accept=".xlsx" style={{ display: 'none' }}
                 onChange={e => { const f = e.target.files?.[0]; if (f) importExcel(f) }} />
        </div>
      </div>

      {loading && <div className="muted">Loading…</div>}

      {flags.length > 0 && (
        <div className="card" style={{ borderColor: 'var(--warning)' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <strong style={{ color: 'var(--warning)' }}>⚠ {flags.length} line{flags.length === 1 ? '' : 's'} flagged for review</strong>
            <button className="btn-ghost" onClick={() => setFlags([])}>Dismiss</button>
          </div>
          <ul style={{ margin: '8px 0 0 16px', padding: 0, fontSize: 13 }}>
            {flags.map(f => (
              <li key={f.lineId} style={{ marginBottom: 6 }}>
                <strong>Line {f.lineId}</strong> — <span className="muted">{f.description.length > 60 ? f.description.slice(0, 60) + '…' : f.description}</span>
                <div style={{ marginLeft: 8 }}><span style={{ color: 'var(--warning)' }}>Issue:</span> {f.issue}</div>
                {f.suggestion && <div style={{ marginLeft: 8 }}><span style={{ color: 'var(--success)' }}>Suggestion:</span> {f.suggestion}</div>}
              </li>
            ))}
          </ul>
        </div>
      )}

      {data && (
        <>
          <div className="kpi-row">
            <div className="kpi"><div className="kpi-label">Planned</div><div className="kpi-value">${data.totalBudget.toLocaleString()}</div></div>
            <div className="kpi"><div className="kpi-label">Spent</div><div className="kpi-value">${data.totalSpent.toLocaleString()}</div></div>
            <div className="kpi">
              <div className="kpi-label">Remaining</div>
              <div className="kpi-value" style={{ color: variance >= 0 ? 'var(--success)' : 'var(--danger)' }}>
                ${variance.toLocaleString()} {data.totalBudget > 0 && <span style={{ fontSize: 12 }}>({variancePct.toFixed(0)}%)</span>}
              </div>
            </div>
            <div className="kpi"><div className="kpi-label">Categories</div><div className="kpi-value">{data.categoryCount}</div></div>
            <div className="kpi"><div className="kpi-label">Lines</div><div className="kpi-value">{data.lineCount}</div></div>
          </div>

          {data.categories.length === 0 && (
            <p className="muted">No budget yet. Import an Excel cost estimate above.</p>
          )}

          {data.categories.map(cat => {
            const isCollapsed = collapsed[cat.category]
            const catVar = cat.totalBudget - cat.totalSpent
            return (
              <div key={cat.category} className="card" style={{ padding: 0 }}>
                <div className="cat-header" onClick={() => setCollapsed(c => ({ ...c, [cat.category]: !isCollapsed }))}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span style={{ width: 12, color: 'var(--muted)' }}>{isCollapsed ? '▸' : '▾'}</span>
                    <strong>{cat.category}</strong>
                    <span className="muted" style={{ fontSize: 12 }}>({cat.lineCount} {cat.lineCount === 1 ? 'line' : 'lines'})</span>
                  </div>
                  <div style={{ display: 'flex', gap: 16, fontSize: 13 }}>
                    <span>Budget: <strong>${cat.totalBudget.toLocaleString()}</strong></span>
                    <span>Spent: <strong>${cat.totalSpent.toLocaleString()}</strong></span>
                    <span style={{ color: catVar >= 0 ? 'var(--success)' : 'var(--danger)' }}>
                      ±${Math.abs(catVar).toLocaleString()}
                    </span>
                  </div>
                </div>
                {!isCollapsed && (
                  <table className="data" style={{ borderRadius: 0 }}>
                    <thead>
                      <tr>
                        <th>Description</th>
                        <th style={{ textAlign: 'right' }}>Budget</th>
                        <th style={{ textAlign: 'center' }}>% Done</th>
                        <th style={{ textAlign: 'right' }}>Earned</th>
                        <th style={{ textAlign: 'right' }}>Spent</th>
                        <th></th>
                      </tr>
                    </thead>
                    <tbody>
                      {cat.lines.map(line => {
                        const flag = flagsByLine.get(line.id)
                        return (
                          <tr key={line.id} style={flag ? { background: 'rgba(210,153,34,0.06)' } : undefined}>
                            <td>
                              {flag && <span title={`${flag.issue} → ${flag.suggestion}`} style={{ color: 'var(--warning)', marginRight: 6 }}>⚠</span>}
                              {line.description}
                            </td>
                            <td style={{ textAlign: 'right' }}>${line.budget.toLocaleString()}</td>
                            <td style={{ textAlign: 'center' }}>
                              <div style={{ background: 'var(--bg)', borderRadius: 4, overflow: 'hidden', height: 12, width: 60, margin: '0 auto' }}>
                                <div style={{ width: `${line.percentComplete}%`, height: '100%', background: 'var(--accent)' }} />
                              </div>
                              <span style={{ fontSize: 11, color: 'var(--muted)' }}>{line.percentComplete}%</span>
                            </td>
                            <td style={{ textAlign: 'right' }}>${line.earned.toLocaleString()}</td>
                            <td style={{ textAlign: 'right' }}>${line.spent.toLocaleString()}</td>
                            <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                              <button className="btn-ghost" onClick={() => openVoiceEdit(line)} title="Edit with voice/text">🎤</button>
                              <button className="btn-ghost" onClick={() => setEditing(line)}>Edit</button>
                              <button className="btn-ghost danger" onClick={() => deleteLine(line.id)}>×</button>
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                )}
              </div>
            )
          })}
        </>
      )}

      {editing && (
        <div className="modal-backdrop" onClick={() => setEditing(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>Edit line</h2>
            <label>Description <input value={editing.description} onChange={e => setEditing({ ...editing, description: e.target.value })} /></label>
            <label>Budget (USD) <input type="number" step="0.01" value={editing.budget} onChange={e => setEditing({ ...editing, budget: parseFloat(e.target.value) || 0 })} /></label>
            <label>% Complete: <strong>{editing.percentComplete}%</strong>
              <input type="range" min="0" max="100" step="5" value={editing.percentComplete}
                     onChange={e => setEditing({ ...editing, percentComplete: parseInt(e.target.value) })}
                     style={{ width: '100%' }} />
            </label>
            <label>Status
              <select value={editing.status ?? ''} onChange={e => setEditing({ ...editing, status: e.target.value || null })}>
                <option value="">—</option>
                <option value="planned">planned</option>
                <option value="in_progress">in_progress</option>
                <option value="paid">paid</option>
                <option value="cancelled">cancelled</option>
              </select>
            </label>
            <div className="modal-actions">
              <button className="btn-ghost" onClick={() => setEditing(null)}>Cancel</button>
              <button className="btn-primary" onClick={saveLine}>Save</button>
            </div>
          </div>
        </div>
      )}

      {voiceLine && (
        <div className="modal-backdrop" onClick={() => !voiceParsing && setVoiceLine(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>🎤 Edit "{voiceLine.description.length > 40 ? voiceLine.description.slice(0, 40) + '…' : voiceLine.description}"</h2>
            <p className="muted" style={{ fontSize: 13, marginTop: 0 }}>
              Speak or type the change. Examples:
              <br />• "change amount to 5000"
              <br />• "spent 2500 and mark paid"
              <br />• "move to Materials"
              <br />• "rename to Permits and Inspection"
            </p>
            <div style={{ display: 'flex', gap: 8 }}>
              <textarea value={voiceText} onChange={e => setVoiceText(e.target.value)}
                        placeholder="What's the change?" autoFocus rows={2}
                        style={{ flex: 1, padding: 10, background: 'var(--bg)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: 6, fontSize: 15, resize: 'vertical' }} />
              <button onClick={startVoice} disabled={voiceParsing}
                      style={{ padding: '0 18px', background: voiceRecording ? 'var(--danger)' : 'var(--panel-2)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: 6, cursor: 'pointer', fontSize: 22 }}
                      title={voiceRecording ? 'Listening…' : 'Voice input'}>
                {voiceRecording ? '⏺' : '🎤'}
              </button>
            </div>

            {proposal && (
              <div className="banner-info" style={{ marginTop: 12 }}>
                <strong>Proposed change:</strong> {proposal.summary}
              </div>
            )}

            <div className="modal-actions">
              <button className="btn-ghost" onClick={() => { setVoiceLine(null); setProposal(null) }} disabled={voiceParsing}>Cancel</button>
              {!proposal && (
                <button className="btn-primary" onClick={parseVoiceEdit} disabled={voiceParsing || !voiceText.trim()}>
                  {voiceParsing ? '… parsing' : 'Parse'}
                </button>
              )}
              {proposal && (
                <button className="btn-primary" onClick={applyProposal}>Apply</button>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
