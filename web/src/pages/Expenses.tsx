import { useEffect, useRef, useState } from 'react'
import { get, post, del } from '../api'
import { useProjects } from '../state/ProjectContext'
import { ImageLightbox } from '../components/ImageLightbox'

type Expense = {
  id: number; projectId: number; category: string; description: string;
  budget: number; actual: number; date?: string | null; vendor?: string | null;
  status?: string | null; receiptFileBlobId?: number | null;
  budgetLineId?: number | null
}
type BudgetLine = { id: number; category: string; description: string; budget: number }

export function Expenses() {
  const { current } = useProjects()
  const [expenses, setExpenses] = useState<Expense[]>([])
  const [showForm, setShowForm] = useState(false)
  const [draft, setDraft] = useState({ category: 'General', description: '', actual: 0, budget: 0, vendor: '', date: today(), budgetLineId: null as number | null })
  const [receiptFile, setReceiptFile] = useState<File | null>(null)
  const [budgetLines, setBudgetLines] = useState<BudgetLine[]>([])
  const [missing, setMissing] = useState<string[]>([])
  const [parseNote, setParseNote] = useState<string | null>(null)
  const [lineSuggestionSource, setLineSuggestionSource] = useState<'server' | 'local' | null>(null)
  const [suggestingLine, setSuggestingLine] = useState(false)
  const userPickedLineRef = useRef(false)
  const suggestSeqRef = useRef(0)
  const [rawText, setRawText] = useState<string | null>(null)
  const [showRaw, setShowRaw] = useState(false)
  const [parsing, setParsing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [importing, setImporting] = useState(false)
  const importInputRef = useRef<HTMLInputElement>(null)
  const smartInputRef = useRef<HTMLInputElement>(null)
  const [lightbox, setLightbox] = useState<string | null>(null)
  const [showQuickAdd, setShowQuickAdd] = useState(false)
  const [quickText, setQuickText] = useState('')
  const [quickRecording, setQuickRecording] = useState(false)
  const [quickParsing, setQuickParsing] = useState(false)

  async function load() {
    if (!current) { setExpenses([]); setBudgetLines([]); return }
    const [exps, lines] = await Promise.all([
      get<Expense[]>(`/api/budgetitems?projectId=${current.id}`),
      get<BudgetLine[]>(`/api/budget/lines?projectId=${current.id}`).catch(() => []),
    ])
    setExpenses(exps.filter(e => e.actual > 0))
    setBudgetLines(lines)
  }
  useEffect(() => { load() }, [current])

  function suggestBudgetLine(category: string): number | null {
    if (!category) return null
    const exact = budgetLines.find(l => l.category.toLowerCase() === category.toLowerCase())
    if (exact) return exact.id
    const partial = budgetLines.find(l =>
      l.description.toLowerCase().includes(category.toLowerCase()) ||
      category.toLowerCase().includes(l.category.toLowerCase()))
    return partial?.id ?? null
  }

  function resetForm() {
    setDraft({ category: 'General', description: '', actual: 0, budget: 0, vendor: '', date: today(), budgetLineId: null })
    setReceiptFile(null)
    setMissing([])
    setParseNote(null)
    setRawText(null)
    setShowRaw(false)
    setLineSuggestionSource(null)
    setSuggestingLine(false)
    userPickedLineRef.current = false
    suggestSeqRef.current++ // invalidate any in-flight suggestion from a prior receipt
  }

  // Fires after the form is open. Async — does not block the parse round trip.
  // If the user picks a line manually before this returns, we drop the suggestion.
  async function requestLineSuggestion(description: string, vendor: string | null, category: string | null) {
    if (!current) return
    const seq = ++suggestSeqRef.current
    setSuggestingLine(true)
    try {
      const r = await fetch('/api/expenses/suggest-line', {
        method: 'POST', credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ projectId: current.id, description, vendor, category }),
      })
      if (!r.ok) return
      const data = await r.json()
      if (seq !== suggestSeqRef.current) return        // stale: form was reset or replaced
      if (userPickedLineRef.current) return            // user already chose; don't overwrite
      const id = data.budgetLineId as number | null
      if (id == null) return
      setDraft(d => ({ ...d, budgetLineId: id }))
      setLineSuggestionSource('server')
    } catch { /* network blip — silent; dropdown stays as-is */ }
    finally { if (seq === suggestSeqRef.current) setSuggestingLine(false) }
  }

  async function quickParse() {
    if (!quickText.trim()) return
    setQuickParsing(true)
    try {
      const r = await fetch('/api/expenses/parse/text', {
        method: 'POST', credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: quickText.trim() }),
      })
      const data = await r.json()
      if (!r.ok) { alert(`Parse failed: ${data.error || r.statusText}`); return }
      const cat = data.category || 'General'
      const localGuess = suggestBudgetLine(cat)
      userPickedLineRef.current = false
      setDraft({
        category: cat,
        description: data.description || '',
        actual: data.amount ?? 0,
        budget: 0,
        vendor: data.vendor || '',
        date: data.date || today(),
        budgetLineId: localGuess,
      })
      setLineSuggestionSource(localGuess == null ? null : 'local')
      setReceiptFile(null)
      setMissing(data.missing || [])
      setParseNote(data.note || `Parsed from: "${quickText.trim()}"`)
      setRawText(quickText.trim())
      setShowRaw(false)
      setShowQuickAdd(false)
      setQuickText('')
      setShowForm(true)
      // fire-and-forget — the dropdown will update when the LLM matcher returns
      requestLineSuggestion(data.description || '', data.vendor || null, cat)
    } catch (e: any) {
      alert(`Parse failed: ${e.message}`)
    } finally {
      setQuickParsing(false)
    }
  }

  function startQuickVoice() {
    const SR = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition
    if (!SR) { alert('Speech recognition not supported in this browser. Use Chrome or Edge.'); return }
    const rec = new SR()
    rec.lang = 'en-US'; rec.continuous = false; rec.interimResults = false
    rec.onresult = (e: any) => { setQuickText(prev => (prev + ' ' + e.results[0][0].transcript).trim()); setQuickRecording(false) }
    rec.onerror = () => setQuickRecording(false)
    rec.onend = () => setQuickRecording(false)
    rec.start()
    setQuickRecording(true)
  }

  async function smartUpload(file: File) {
    setParsing(true)
    try {
      const fd = new FormData()
      fd.append('file', file)
      const r = await fetch('/api/expenses/parse', { method: 'POST', credentials: 'include', body: fd })
      const data = await r.json()
      if (!r.ok) { alert(`Parse failed: ${data.error || r.statusText}`); return }
      const cat = data.category || 'General'
      const localGuess = suggestBudgetLine(cat)
      userPickedLineRef.current = false
      setDraft({
        category: cat,
        description: data.description || '',
        actual: data.amount ?? 0,
        budget: 0,
        vendor: data.vendor || '',
        date: data.date || today(),
        budgetLineId: localGuess,
      })
      setLineSuggestionSource(localGuess == null ? null : 'local')
      setReceiptFile(file)
      setMissing(data.missing || [])
      setParseNote(data.note || null)
      setRawText(data.rawText || null)
      setShowRaw(false)
      setShowForm(true)
      // fire-and-forget — the dropdown will update when the LLM matcher returns
      requestLineSuggestion(data.description || '', data.vendor || null, cat)
    } catch (e: any) {
      alert(`Parse failed: ${e.message}`)
    } finally {
      setParsing(false)
      if (smartInputRef.current) smartInputRef.current.value = ''
    }
  }

  async function create() {
    if (!current || !draft.description) return
    if (!draft.actual || draft.actual <= 0) { alert('Amount is required.'); return }
    setBusy(true)
    try {
      const created = await post<Expense>('/api/budgetitems', { ...draft, projectId: current.id, status: 'paid', budgetLineId: draft.budgetLineId })
      if (receiptFile && created.id) {
        const fd = new FormData()
        fd.append('file', receiptFile)
        await fetch(`/api/budgetitems/${created.id}/receipt`, { method: 'POST', credentials: 'include', body: fd })
      }
      setShowForm(false)
      resetForm()
      await load()
    } catch (e: any) { alert(e.message) }
    finally { setBusy(false) }
  }

  async function remove(id: number) {
    if (!confirm('Delete expense?')) return
    await del(`/api/budgetitems/${id}`)
    await load()
  }

  async function attachReceipt(id: number, file: File) {
    const fd = new FormData()
    fd.append('file', file)
    await fetch(`/api/budgetitems/${id}/receipt`, { method: 'POST', credentials: 'include', body: fd })
    await load()
  }

  async function importExcel(file: File) {
    if (!current) return
    setImporting(true)
    try {
      const fd = new FormData()
      fd.append('file', file)
      fd.append('projectId', String(current.id))
      const r = await fetch('/api/imports/expenses', { method: 'POST', credentials: 'include', body: fd })
      const data = await r.json()
      if (!r.ok) { alert(`Import failed: ${data.error || r.statusText}`); return }
      alert(`Imported ${data.inserted} rows${data.skipped ? `, skipped ${data.skipped}` : ''}${data.errors?.length ? `\n\nErrors:\n${data.errors.slice(0, 5).join('\n')}` : ''}`)
      await load()
    } finally {
      setImporting(false)
      if (importInputRef.current) importInputRef.current.value = ''
    }
  }

  if (!current) return <div className="page"><h1>Expenses</h1><p className="muted">Select a project first.</p></div>

  const total = expenses.reduce((s, e) => s + (e.actual ?? 0), 0)
  const budget = expenses.reduce((s, e) => s + (e.budget ?? 0), 0)
  const exportUrl = `/api/exports/expenses.xlsx?projectId=${current.id}`
  const isMissing = (field: string) => missing.includes(field)
  const cls = (field: string) => isMissing(field) ? 'input-missing' : ''

  return (
    <div className="page">
      <div className="page-head">
        <h1>Expenses — {current.name}</h1>
        <div className="toolbar">
          <button className="btn-primary" onClick={() => setShowQuickAdd(true)} title="Type or say what you spent">
            ⚡ Quick add
          </button>
          <button className="btn-primary" onClick={() => smartInputRef.current?.click()} disabled={parsing} title="Upload a receipt — fields auto-extracted">
            {parsing ? '… reading' : '✦ Smart upload'}
          </button>
          <input
            ref={smartInputRef}
            type="file"
            accept="image/*,application/pdf,text/*"
            style={{ display: 'none' }}
            onChange={e => { const f = e.target.files?.[0]; if (f) smartUpload(f) }}
          />
          <a className="btn-ghost" href={exportUrl}>⤓ Export .xlsx</a>
          <button className="btn-ghost" onClick={() => importInputRef.current?.click()} disabled={importing}>
            {importing ? '… importing' : '⤒ Import .xlsx'}
          </button>
          <a className="btn-ghost" href="/api/exports/template/expenses.xlsx" title="Download a template you can fill in">Template</a>
          <input ref={importInputRef} type="file" accept=".xlsx" style={{ display: 'none' }}
                 onChange={e => { const f = e.target.files?.[0]; if (f) importExcel(f) }} />
          <button className="btn-ghost" onClick={() => { resetForm(); setShowForm(true) }}>+ Add manually</button>
        </div>
      </div>

      <div className="kpi-row">
        <div className="kpi"><div className="kpi-label">Spent</div><div className="kpi-value">${total.toLocaleString()}</div></div>
        <div className="kpi"><div className="kpi-label">Budget</div><div className="kpi-value">${budget.toLocaleString()}</div></div>
        <div className="kpi"><div className="kpi-label">Items</div><div className="kpi-value">{expenses.length}</div></div>
      </div>

      <table className="data">
        <thead>
          <tr>
            <th>Date</th><th>Category</th><th>Description</th><th>Vendor</th>
            <th style={{textAlign:'right'}}>Amount</th><th>Receipt</th><th></th>
          </tr>
        </thead>
        <tbody>
          {expenses.length === 0 && <tr><td colSpan={7} className="muted">No expenses logged. Try ✦ Smart upload to import a receipt.</td></tr>}
          {expenses.map(e => (
            <tr key={e.id}>
              <td>{e.date?.slice(0, 10) || '—'}</td>
              <td>{e.category}</td>
              <td>{e.description}</td>
              <td>{e.vendor || '—'}</td>
              <td style={{textAlign:'right'}}>${(e.actual ?? 0).toLocaleString()}</td>
              <td>
                {e.receiptFileBlobId ? (
                  <img
                    className="receipt-thumb"
                    src={`/api/files/${e.receiptFileBlobId}`}
                    alt="receipt"
                    onClick={() => setLightbox(`/api/files/${e.receiptFileBlobId}`)}
                    onError={ev => { (ev.target as HTMLImageElement).style.display = 'none' }}
                  />
                ) : (
                  <label className="btn-ghost receipt-upload" title="Attach receipt">
                    📎
                    <input type="file" accept="image/*,application/pdf" style={{ display: 'none' }}
                           onChange={ev => { const f = ev.target.files?.[0]; if (f) attachReceipt(e.id, f) }} />
                  </label>
                )}
              </td>
              <td style={{ textAlign: 'right' }}>
                <button className="btn-ghost danger" onClick={() => remove(e.id)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {showForm && (
        <div className="modal-backdrop" onClick={() => !busy && setShowForm(false)}>
          <div className="modal" onClick={ev => ev.stopPropagation()}>
            <h2>{receiptFile ? 'Confirm expense' : 'Add expense'}</h2>

            {missing.length > 0 && (
              <div className="banner-warn">
                Couldn't read {missing.join(', ')} from the receipt. Please fill them in.
              </div>
            )}
            {parseNote && (
              <div className="banner-info">{parseNote}</div>
            )}
            {receiptFile && missing.length === 0 && (
              <div className="banner-ok">Fields extracted from {receiptFile.name}. Review and confirm.</div>
            )}

            <label>Description {isMissing('description') && <span className="req">required</span>}
              <input className={cls('description')} value={draft.description}
                     onChange={ev => setDraft({ ...draft, description: ev.target.value })} autoFocus />
            </label>
            <label>Category
              <input value={draft.category} onChange={ev => setDraft({ ...draft, category: ev.target.value })} />
            </label>
            <label>Budget line
              <select
                value={draft.budgetLineId ?? ''}
                onChange={ev => {
                  const v = ev.target.value
                  userPickedLineRef.current = true
                  setDraft({ ...draft, budgetLineId: v === '' ? null : parseInt(v, 10) })
                  setLineSuggestionSource(null)
                }}
              >
                <option value="">— None (uncategorized) —</option>
                {budgetLines.map(l => (
                  <option key={l.id} value={l.id}>{l.category} · {l.description}</option>
                ))}
              </select>
              {suggestingLine && (
                <span className="muted" style={{ fontSize: 11 }}>✦ AI is suggesting a match… (you can pick now to skip)</span>
              )}
              {!suggestingLine && lineSuggestionSource && draft.budgetLineId != null && (
                <span className="muted" style={{ fontSize: 11 }}>
                  {lineSuggestionSource === 'server' ? '✦ Auto-matched by AI' : 'Matched by category'} — change if wrong
                </span>
              )}
              {!suggestingLine && budgetLines.length === 0 && (
                <span className="muted" style={{ fontSize: 11 }}>No budget lines yet — import a budget to enable auto-matching</span>
              )}
            </label>
            <label>Amount (USD) {isMissing('amount') && <span className="req">required</span>}
              <input className={cls('amount')} type="number" step="0.01" value={draft.actual}
                     onChange={ev => setDraft({ ...draft, actual: parseFloat(ev.target.value) || 0 })} />
            </label>
            <label>Vendor {isMissing('vendor') && <span className="req">required</span>}
              <input className={cls('vendor')} value={draft.vendor}
                     onChange={ev => setDraft({ ...draft, vendor: ev.target.value })} />
            </label>
            <label>Date
              <input type="date" value={draft.date} onChange={ev => setDraft({ ...draft, date: ev.target.value })} />
            </label>

            {!receiptFile && (
              <label>Receipt (optional)
                <input type="file" accept="image/*,application/pdf"
                       onChange={ev => setReceiptFile(ev.target.files?.[0] ?? null)} />
              </label>
            )}
            {receiptFile && (
              <div className="muted" style={{ fontSize: 12 }}>
                📎 {receiptFile.name} ({Math.round(receiptFile.size / 1024)} KB) will be saved as the receipt.
              </div>
            )}

            {rawText && (
              <div style={{ marginTop: 12 }}>
                <button className="btn-ghost" style={{ padding: '4px 0' }} onClick={() => setShowRaw(s => !s)}>
                  {showRaw ? '▾' : '▸'} What the parser saw
                </button>
                {showRaw && (
                  <pre style={{
                    maxHeight: 200, overflowY: 'auto',
                    background: 'var(--bg)', border: '1px solid var(--border)',
                    borderRadius: 6, padding: 10, fontSize: 11, lineHeight: 1.4,
                    whiteSpace: 'pre-wrap', wordWrap: 'break-word', marginTop: 4
                  }}>{rawText}</pre>
                )}
              </div>
            )}

            <div className="modal-actions">
              <button className="btn-ghost" onClick={() => { setShowForm(false); resetForm() }} disabled={busy}>Cancel</button>
              <button className="btn-primary" onClick={create} disabled={busy || !draft.description || !draft.actual}>
                {busy ? '…' : (receiptFile ? 'Confirm & save' : 'Add')}
              </button>
            </div>
          </div>
        </div>
      )}

      {lightbox && <ImageLightbox src={lightbox} onClose={() => setLightbox(null)} />}

      {showQuickAdd && (
        <div className="modal-backdrop" onClick={() => !quickParsing && setShowQuickAdd(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>⚡ Quick add expense</h2>
            <p className="muted" style={{ fontSize: 13, marginTop: 0 }}>
              Type or say what you spent. Examples:
              <br />• "I spent 100 for clean up today"
              <br />• "Paid 250 to Home Depot for paint"
              <br />• "800 dollars to the electrician yesterday"
            </p>
            <div style={{ display: 'flex', gap: 8, alignItems: 'stretch' }}>
              <textarea
                value={quickText}
                onChange={e => setQuickText(e.target.value)}
                placeholder="What did you spend on?"
                autoFocus
                rows={3}
                style={{ flex: 1, padding: 10, background: 'var(--bg)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: 6, fontSize: 15, resize: 'vertical' }}
                onKeyDown={e => { if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) quickParse() }}
              />
              <button
                onClick={startQuickVoice}
                disabled={quickParsing}
                title={quickRecording ? 'Listening…' : 'Voice input'}
                style={{ padding: '0 18px', background: quickRecording ? 'var(--danger)' : 'var(--panel-2)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: 6, cursor: 'pointer', fontSize: 22 }}
              >
                {quickRecording ? '⏺' : '🎤'}
              </button>
            </div>
            <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
              Press the mic to speak. ⌘/Ctrl+Enter to parse.
            </div>
            <div className="modal-actions">
              <button className="btn-ghost" onClick={() => { setShowQuickAdd(false); setQuickText('') }} disabled={quickParsing}>Cancel</button>
              <button className="btn-primary" onClick={quickParse} disabled={quickParsing || !quickText.trim()}>
                {quickParsing ? '… parsing' : 'Parse'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function today() { return new Date().toISOString().slice(0, 10) }
