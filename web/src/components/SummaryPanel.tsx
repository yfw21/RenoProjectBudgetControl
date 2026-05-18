type Data = {
  project: string; status: string; address?: string; client?: string
  spend_usd: number; budget_usd: number
  tasks_total: number; tasks_in_progress: number; tasks_upcoming: number; tasks_overdue: number
  deliveries_pending: number
}

export function SummaryPanel({ title, data }: { title?: string; data: Data }) {
  const stats: [string, string][] = [
    ['Status', data.status],
    ['Client', data.client ?? '—'],
    ['Address', data.address ?? '—'],
    ['Spend', `$${(data.spend_usd ?? 0).toLocaleString()}`],
    ['Budget', `$${(data.budget_usd ?? 0).toLocaleString()}`],
    ['Tasks', String(data.tasks_total)],
    ['In progress', String(data.tasks_in_progress)],
    ['Upcoming', String(data.tasks_upcoming)],
    ['Overdue', String(data.tasks_overdue)],
    ['Deliveries pending', String(data.deliveries_pending)],
  ]
  return (
    <div>
      {title && <div style={{ marginBottom: 8, fontWeight: 600, fontSize: 16 }}>{title}</div>}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))', gap: 8 }}>
        {stats.map(([k, v]) => (
          <div key={k} style={{ padding: 10, background: 'var(--bg)', border: '1px solid var(--border)', borderRadius: 8 }}>
            <div style={{ color: 'var(--muted)', fontSize: 11, textTransform: 'uppercase', letterSpacing: 0.5 }}>{k}</div>
            <div style={{ fontSize: 16, marginTop: 2 }}>{v}</div>
          </div>
        ))}
      </div>
    </div>
  )
}
