export function TablePanel({ title, rows }: { title?: string; rows: any[] }) {
  if (!rows || rows.length === 0) return <div><strong>{title}</strong><div style={{ color: 'var(--muted)' }}>No rows.</div></div>
  const cols = Object.keys(rows[0])
  return (
    <div>
      {title && <div style={{ marginBottom: 8, fontWeight: 600 }}>{title}</div>}
      <div style={{ overflowX: 'auto' }}>
        <table style={{ borderCollapse: 'collapse', fontSize: 13, width: '100%' }}>
          <thead>
            <tr>
              {cols.map(c => (
                <th key={c} style={{ textAlign: 'left', padding: '6px 10px', borderBottom: '1px solid var(--border)', color: 'var(--muted)', fontWeight: 500 }}>{c}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={i}>
                {cols.map(c => (
                  <td key={c} style={{ padding: '6px 10px', borderBottom: '1px solid var(--border)' }}>{format(r[c])}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function format(v: any): string {
  if (v == null) return ''
  if (typeof v === 'number') return Number.isInteger(v) ? String(v) : v.toFixed(2)
  if (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(v)) return v.slice(0, 10)
  return String(v)
}
