type Row = { id: number; name: string; project: string; category: string; start: string; end: string }

export function GanttPanel({ title, rows }: { title?: string; rows: Row[] }) {
  if (!rows || rows.length === 0) return <div><strong>{title}</strong><div style={{ color: 'var(--muted)' }}>No tasks.</div></div>
  const starts = rows.map(r => new Date(r.start).getTime())
  const ends = rows.map(r => new Date(r.end).getTime())
  const min = Math.min(...starts)
  const max = Math.max(...ends)
  const span = Math.max(1, max - min)
  return (
    <div>
      {title && <div style={{ marginBottom: 8, fontWeight: 600 }}>{title}</div>}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        {rows.map(r => {
          const left = ((new Date(r.start).getTime() - min) / span) * 100
          const width = Math.max(2, ((new Date(r.end).getTime() - new Date(r.start).getTime()) / span) * 100)
          return (
            <div key={r.id} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12 }}>
              <div style={{ width: 140, color: 'var(--muted)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={`${r.project} · ${r.name}`}>{r.name}</div>
              <div style={{ position: 'relative', flex: 1, height: 18, background: 'var(--bg)', borderRadius: 4 }}>
                <div style={{ position: 'absolute', left: `${left}%`, width: `${width}%`, top: 0, bottom: 0, background: 'var(--accent)', borderRadius: 4, opacity: 0.85 }} title={`${r.start} → ${r.end}`} />
              </div>
              <div style={{ width: 160, color: 'var(--muted)', fontSize: 11, whiteSpace: 'nowrap' }}>{r.start} → {r.end}</div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
