type Row = { kind: string; resource: string; task_a: string; task_b: string; from: string; to: string }

export function ConflictsPanel({ title, rows }: { title?: string; rows: Row[] }) {
  return (
    <div>
      {title && <div style={{ marginBottom: 8, fontWeight: 600 }}>{title}</div>}
      {(!rows || rows.length === 0) ? (
        <div style={{ color: 'var(--muted)' }}>No conflicts. Schedule looks clean.</div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {rows.map((r, i) => (
            <div key={i} style={{ padding: 10, border: '1px solid var(--danger)', borderRadius: 8, background: 'rgba(248,81,73,0.08)', fontSize: 13 }}>
              <strong style={{ color: 'var(--danger)' }}>{r.resource}</strong> ({r.kind}) double-booked
              <div style={{ color: 'var(--muted)', fontSize: 12, marginTop: 2 }}>{r.task_a} ↔ {r.task_b} · {r.from} → {r.to}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
