import { useState } from 'react'
import { useProjects, type Project } from '../state/ProjectContext'
import { post, put, del } from '../api'

export function Projects() {
  const { projects, refresh, setCurrentId } = useProjects()
  const [editing, setEditing] = useState<Partial<Project> | null>(null)
  const [busy, setBusy] = useState(false)

  async function save() {
    if (!editing || !editing.name) return
    setBusy(true)
    try {
      if (editing.id) await put(`/api/projects/${editing.id}`, editing)
      else {
        const created = await post<Project>('/api/projects', { ...editing, status: editing.status ?? 'active' })
        setCurrentId(created.id)
      }
      setEditing(null)
      await refresh()
    } catch (e: any) { alert(e.message) }
    finally { setBusy(false) }
  }

  async function remove(id: number) {
    if (!confirm('Delete this project? This cannot be undone.')) return
    await del(`/api/projects/${id}`)
    await refresh()
  }

  return (
    <div className="page">
      <div className="page-head">
        <h1>Projects</h1>
        <button className="btn-primary" onClick={() => setEditing({ status: 'active' })}>+ New project</button>
      </div>

      {projects.length === 0 ? (
        <p style={{ color: 'var(--muted)' }}>No projects yet.</p>
      ) : (
        <table className="data">
          <thead>
            <tr><th>Name</th><th>Status</th><th>Client</th><th>Address</th><th></th></tr>
          </thead>
          <tbody>
            {projects.map(p => (
              <tr key={p.id}>
                <td><strong>{p.name}</strong></td>
                <td><span className={`pill pill-${p.status}`}>{p.status}</span></td>
                <td>{p.client || '—'}</td>
                <td>{p.address || '—'}</td>
                <td style={{ textAlign: 'right' }}>
                  <button className="btn-ghost" onClick={() => setEditing(p)}>Edit</button>
                  <button className="btn-ghost danger" onClick={() => remove(p.id)}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {editing && (
        <div className="modal-backdrop" onClick={() => !busy && setEditing(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>{editing.id ? 'Edit project' : 'New project'}</h2>
            <label>Name <input value={editing.name ?? ''} onChange={e => setEditing({ ...editing, name: e.target.value })} autoFocus /></label>
            <label>Status
              <select value={editing.status ?? 'active'} onChange={e => setEditing({ ...editing, status: e.target.value })}>
                <option value="active">active</option>
                <option value="paused">paused</option>
                <option value="complete">complete</option>
                <option value="archived">archived</option>
              </select>
            </label>
            <label>Client <input value={editing.client ?? ''} onChange={e => setEditing({ ...editing, client: e.target.value })} /></label>
            <label>Address <input value={editing.address ?? ''} onChange={e => setEditing({ ...editing, address: e.target.value })} /></label>
            <div className="modal-actions">
              <button className="btn-ghost" onClick={() => setEditing(null)} disabled={busy}>Cancel</button>
              <button className="btn-primary" onClick={save} disabled={busy || !editing.name}>{busy ? '…' : 'Save'}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
