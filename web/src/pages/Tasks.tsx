import { useEffect, useState } from 'react'
import { get, post, del } from '../api'
import { useProjects } from '../state/ProjectContext'
import { GanttPanel } from '../components/GanttPanel'

type Task = { id: number; projectId: number; name: string; categoryValue: string; startDate: string; endDate: string; workHours: number; budgetLineId?: number | null }
type BudgetLine = { id: number; category: string; description: string; budget: number }

export function Tasks() {
  const { current } = useProjects()
  const [tasks, setTasks] = useState<Task[]>([])
  const [budgetLines, setBudgetLines] = useState<BudgetLine[]>([])
  const [showForm, setShowForm] = useState(false)
  const [draft, setDraft] = useState({ name: '', categoryValue: 'General', startDate: today(), endDate: today(), workHours: 8, budgetLineId: null as number | null })
  const [busy, setBusy] = useState(false)

  async function load() {
    if (!current) { setTasks([]); setBudgetLines([]); return }
    const [list, lines] = await Promise.all([
      get<Task[]>(`/api/categoryactivities?projectId=${current.id}`),
      get<BudgetLine[]>(`/api/budget/lines?projectId=${current.id}`).catch(() => []),
    ])
    setTasks(list)
    setBudgetLines(lines)
  }
  useEffect(() => { load() }, [current])

  async function create() {
    if (!current || !draft.name) return
    setBusy(true)
    try {
      await post('/api/categoryactivities', { ...draft, projectId: current.id, budgetLineId: draft.budgetLineId })
      setShowForm(false)
      setDraft({ name: '', categoryValue: 'General', startDate: today(), endDate: today(), workHours: 8, budgetLineId: null })
      await load()
    } catch (e: any) { alert(e.message) }
    finally { setBusy(false) }
  }

  async function remove(id: number) {
    if (!confirm('Delete task?')) return
    await del(`/api/categoryactivities/${id}`)
    await load()
  }

  async function importExcel(file: File) {
    if (!current) return
    const fd = new FormData()
    fd.append('file', file)
    fd.append('projectId', String(current.id))
    const r = await fetch('/api/imports/tasks', { method: 'POST', credentials: 'include', body: fd })
    const data = await r.json()
    if (!r.ok) { alert(`Import failed: ${data.error || r.statusText}`); return }
    alert(`Imported ${data.inserted} rows${data.skipped ? `, skipped ${data.skipped}` : ''}${data.errors?.length ? `\n\nErrors:\n${data.errors.slice(0, 5).join('\n')}` : ''}`)
    await load()
  }

  if (!current) return <div className="page"><h1>Tasks</h1><p className="muted">Select a project first.</p></div>

  const ganttRows = tasks.map(t => ({
    id: t.id, name: t.name, project: current.name, category: t.categoryValue,
    start: t.startDate?.slice(0, 10), end: t.endDate?.slice(0, 10),
  }))
  const exportUrl = `/api/exports/tasks.xlsx?projectId=${current.id}`

  return (
    <div className="page">
      <div className="page-head">
        <h1>Tasks — {current.name}</h1>
        <div className="toolbar">
          <a className="btn-ghost" href={exportUrl}>⤓ Export .xlsx</a>
          <label className="btn-ghost" style={{ cursor: 'pointer' }}>
            ⤒ Import .xlsx
            <input type="file" accept=".xlsx" style={{ display: 'none' }}
                   onChange={e => { const f = e.target.files?.[0]; if (f) importExcel(f) }} />
          </label>
          <a className="btn-ghost" href="/api/exports/template/tasks.xlsx" title="Download a template">Template</a>
          <button className="btn-primary" onClick={() => setShowForm(true)}>+ New task</button>
        </div>
      </div>

      {tasks.length > 0 && (
        <div className="card">
          <GanttPanel title="Schedule" rows={ganttRows} />
        </div>
      )}

      <table className="data">
        <thead>
          <tr><th>Name</th><th>Category</th><th>Start</th><th>End</th><th>Hours</th><th></th></tr>
        </thead>
        <tbody>
          {tasks.length === 0 && <tr><td colSpan={6} className="muted">No tasks yet.</td></tr>}
          {tasks.map(t => (
            <tr key={t.id}>
              <td>{t.name}</td>
              <td>{t.categoryValue}</td>
              <td>{t.startDate?.slice(0, 10)}</td>
              <td>{t.endDate?.slice(0, 10)}</td>
              <td>{t.workHours}</td>
              <td style={{ textAlign: 'right' }}>
                <button className="btn-ghost danger" onClick={() => remove(t.id)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {showForm && (
        <div className="modal-backdrop" onClick={() => !busy && setShowForm(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>New task</h2>
            <label>Name <input value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} autoFocus /></label>
            <label>Category <input value={draft.categoryValue} onChange={e => setDraft({ ...draft, categoryValue: e.target.value })} /></label>
            <label>Start <input type="date" value={draft.startDate} onChange={e => setDraft({ ...draft, startDate: e.target.value })} /></label>
            <label>End <input type="date" value={draft.endDate} onChange={e => setDraft({ ...draft, endDate: e.target.value })} /></label>
            <label>Work hours <input type="number" value={draft.workHours} onChange={e => setDraft({ ...draft, workHours: parseInt(e.target.value) || 0 })} /></label>
            <label>Budget line (which line does this task deliver?)
              <select value={draft.budgetLineId ?? ''} onChange={e => setDraft({ ...draft, budgetLineId: e.target.value ? parseInt(e.target.value) : null })}>
                <option value="">— unlinked —</option>
                {budgetLines.map(l => (
                  <option key={l.id} value={l.id}>[{l.category}] {l.description.length > 50 ? l.description.slice(0, 50) + '…' : l.description} (${l.budget.toLocaleString()})</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button className="btn-ghost" onClick={() => setShowForm(false)} disabled={busy}>Cancel</button>
              <button className="btn-primary" onClick={create} disabled={busy || !draft.name}>{busy ? '…' : 'Create'}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function today() { return new Date().toISOString().slice(0, 10) }
