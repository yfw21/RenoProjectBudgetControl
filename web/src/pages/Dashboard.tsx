import { useEffect, useState } from 'react'
import { get } from '../api'
import { useProjects } from '../state/ProjectContext'
import { SummaryPanel } from '../components/SummaryPanel'

type Conflict = { kind: string; resource: string; taskA: string; taskB: string; overlapStart: string; overlapEnd: string }

export function Dashboard() {
  const { current, projects } = useProjects()
  const [summary, setSummary] = useState<any>(null)
  const [conflicts, setConflicts] = useState<Conflict[]>([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    setLoading(true)
    const tasks: Promise<any>[] = []
    if (current) {
      tasks.push(buildSummary(current.id).then(setSummary))
    }
    tasks.push(get<Conflict[]>('/api/conflicts').then(setConflicts).catch(() => setConflicts([])))
    Promise.all(tasks).finally(() => setLoading(false))
  }, [current])

  if (!current) {
    return (
      <div className="page">
        <h1>Dashboard</h1>
        <p style={{ color: 'var(--muted)' }}>
          {projects.length === 0
            ? 'No projects yet. Go to Projects → New project.'
            : 'Pick a project in the top bar to see its dashboard.'}
        </p>
      </div>
    )
  }

  return (
    <div className="page">
      <h1>{current.name}</h1>
      {loading && <div style={{ color: 'var(--muted)' }}>Loading…</div>}
      {summary && <SummaryPanel data={summary} />}

      <h2 style={{ marginTop: 32 }}>Conflicts</h2>
      {conflicts.length === 0 ? (
        <p style={{ color: 'var(--muted)' }}>No resource conflicts.</p>
      ) : (
        <table className="data">
          <thead>
            <tr><th>Resource</th><th>Task A</th><th>Task B</th><th>From</th><th>To</th></tr>
          </thead>
          <tbody>
            {conflicts.map((c, i) => (
              <tr key={i}>
                <td>{c.resource} <span className="muted">({c.kind})</span></td>
                <td>{c.taskA}</td>
                <td>{c.taskB}</td>
                <td>{c.overlapStart?.slice(0, 10)}</td>
                <td>{c.overlapEnd?.slice(0, 10)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

async function buildSummary(projectId: number) {
  const [project, expenses, tasks, deliveries] = await Promise.all([
    get(`/api/projects/${projectId}`),
    get<any[]>(`/api/budgetitems?projectId=${projectId}`),
    get<any[]>(`/api/categoryactivities?projectId=${projectId}`),
    get<any[]>(`/api/purchase-orders?projectId=${projectId}`),
  ])
  const today = new Date().toISOString().slice(0, 10)
  return {
    project: project.name,
    status: project.status,
    address: project.address,
    client: project.client,
    spend_usd: expenses.reduce((s, e) => s + (e.actual ?? 0), 0),
    budget_usd: expenses.reduce((s, e) => s + (e.budget ?? 0), 0),
    tasks_total: tasks.length,
    tasks_in_progress: tasks.filter(t => t.startDate?.slice(0, 10) <= today && t.endDate?.slice(0, 10) >= today).length,
    tasks_upcoming: tasks.filter(t => t.startDate?.slice(0, 10) > today).length,
    tasks_overdue: tasks.filter(t => t.endDate?.slice(0, 10) < today).length,
    deliveries_pending: deliveries.filter(d => !d.actualDate).length,
  }
}
