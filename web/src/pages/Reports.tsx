import { useEffect, useRef, useState } from 'react'
import { get } from '../api'
import { useProjects } from '../state/ProjectContext'

type Line = { id: number; description: string; budget: number; spent: number; percentComplete: number; earned: number; status: string | null }
type Category = { category: string; totalBudget: number; totalSpent: number; totalEarned: number; lineCount: number; lines: Line[] }
type BudgetView = { project: string; totalBudget: number; totalSpent: number; totalEarned: number; categoryCount: number; lineCount: number; categories: Category[] }

type MonthMap = Record<string, number>
type SpendRow = { lineId: number; category: string; description: string; budget: number; byMonth: MonthMap; total: number; remaining: number }
type SpendByMonth = { months: string[]; rows: SpendRow[]; unassigned: { byMonth: MonthMap; total: number } | null; monthTotals: MonthMap; grandTotal: number; totalBudget: number }
type TaxRow = { category: string; byMonth: MonthMap; total: number }
type TaxPivotData = { months: string[]; categories: TaxRow[]; monthTotals: MonthMap; grandTotal: number }

type ReportKind = 'sov' | 'variance' | 'cost-summary' | 'spend-month' | 'tax-pivot'

export function Reports() {
  const { current } = useProjects()
  const [data, setData] = useState<BudgetView | null>(null)
  const [spend, setSpend] = useState<SpendByMonth | null>(null)
  const [tax, setTax] = useState<TaxPivotData | null>(null)
  const [report, setReport] = useState<ReportKind | null>('sov')
  const [askText, setAskText] = useState('')
  const [recording, setRecording] = useState(false)
  const [asking, setAsking] = useState(false)
  const [answer, setAnswer] = useState<string | null>(null)
  const askRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    if (!current) { setData(null); setSpend(null); setTax(null); return }
    get<BudgetView>(`/api/budget?projectId=${current.id}`).then(setData)
    setSpend(null); setTax(null)
  }, [current])

  // Lazy-load the heavier pivot reports only when their tab is opened.
  useEffect(() => {
    if (!current) return
    if (report === 'spend-month' && !spend) {
      get<SpendByMonth>(`/api/reports/spend-by-month?projectId=${current.id}`).then(setSpend).catch(() => {})
    }
    if (report === 'tax-pivot' && !tax) {
      get<TaxPivotData>(`/api/reports/tax-pivot?projectId=${current.id}`).then(setTax).catch(() => {})
    }
  }, [report, current, spend, tax])

  function startVoice() {
    const SR = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition
    if (!SR) { alert('Speech recognition not supported in this browser. Use Chrome or Edge.'); return }
    const rec = new SR()
    rec.lang = 'en-US'; rec.continuous = false; rec.interimResults = false
    rec.onresult = (e: any) => { setAskText(prev => (prev + ' ' + e.results[0][0].transcript).trim()); setRecording(false); askRef.current?.focus() }
    rec.onerror = () => setRecording(false)
    rec.onend = () => setRecording(false)
    rec.start()
    setRecording(true)
  }

  async function ask() {
    if (!current || !askText.trim()) return
    setAsking(true); setAnswer(null)
    try {
      const r = await fetch(`/api/reports/ask?projectId=${current.id}`, {
        method: 'POST', credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: askText.trim() })
      })
      const data = await r.json()
      if (!r.ok) { setAnswer(`Error: ${data.error || r.statusText}`); return }
      setAnswer(data.answer || '(no answer)')
    } catch (e: any) { setAnswer(`Error: ${e.message}`) }
    finally { setAsking(false) }
  }

  if (!current) return <div className="page"><h1>Reports</h1><p className="muted">Select a project first.</p></div>
  if (!data) return <div className="page"><h1>Reports</h1><p className="muted">Loading…</p></div>

  const allLines = data.categories.flatMap(c => c.lines.map(l => ({ ...l, category: c.category })))

  return (
    <div className="page">
      <h1>Reports — {current.name}</h1>

      <div className="card">
        <div style={{ marginBottom: 8, fontSize: 14, color: 'var(--muted)' }}>Ask anything about this project</div>
        <div style={{ display: 'flex', gap: 8 }}>
          <textarea ref={askRef} value={askText} onChange={e => setAskText(e.target.value)}
                    placeholder='e.g. "What lines are over budget?" or "Where am I behind on completion?"'
                    rows={2}
                    style={{ flex: 1, padding: 10, background: 'var(--bg)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: 6, fontSize: 14, resize: 'vertical' }}
                    onKeyDown={e => { if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) ask() }} />
          <button onClick={startVoice} disabled={asking}
                  style={{ padding: '0 16px', background: recording ? 'var(--danger)' : 'var(--panel-2)', color: 'var(--text)', border: '1px solid var(--border)', borderRadius: 6, cursor: 'pointer', fontSize: 22 }}
                  title={recording ? 'Listening…' : 'Voice input'}>{recording ? '⏺' : '🎤'}</button>
          <button className="btn-primary" onClick={ask} disabled={asking || !askText.trim()}>{asking ? '… asking' : 'Ask'}</button>
        </div>
        {answer && (
          <div style={{ marginTop: 12, padding: 12, background: 'var(--bg)', border: '1px solid var(--border)', borderRadius: 6, whiteSpace: 'pre-wrap', fontSize: 14, lineHeight: 1.5 }}>
            {answer}
          </div>
        )}
      </div>

      <div className="tabs">
        <button className={`tab ${report === 'sov' ? 'active' : ''}`} onClick={() => setReport('sov')}>Schedule of Values</button>
        <button className={`tab ${report === 'variance' ? 'active' : ''}`} onClick={() => setReport('variance')}>Variance Alerts</button>
        <button className={`tab ${report === 'cost-summary' ? 'active' : ''}`} onClick={() => setReport('cost-summary')}>Cost Summary</button>
        <button className={`tab ${report === 'spend-month' ? 'active' : ''}`} onClick={() => setReport('spend-month')}>Spend by Month</button>
        <button className={`tab ${report === 'tax-pivot' ? 'active' : ''}`} onClick={() => setReport('tax-pivot')}>Tax Pivot</button>
      </div>

      {report === 'sov' && <ScheduleOfValues data={data} />}
      {report === 'variance' && <VarianceReport lines={allLines} />}
      {report === 'cost-summary' && <CostSummary data={data} />}
      {report === 'spend-month' && (spend
        ? <SpendByMonthReport data={spend} />
        : <p className="muted" style={{ marginTop: 16 }}>Loading…</p>)}
      {report === 'tax-pivot' && (tax
        ? <TaxPivotReport data={tax} />
        : <p className="muted" style={{ marginTop: 16 }}>Loading…</p>)}
    </div>
  )
}

function ScheduleOfValues({ data }: { data: BudgetView }) {
  return (
    <>
      <div className="kpi-row">
        <div className="kpi"><div className="kpi-label">Planned</div><div className="kpi-value">${data.totalBudget.toLocaleString()}</div></div>
        <div className="kpi"><div className="kpi-label">Earned</div><div className="kpi-value">${data.totalEarned.toLocaleString()}</div></div>
        <div className="kpi"><div className="kpi-label">Spent</div><div className="kpi-value">${data.totalSpent.toLocaleString()}</div></div>
        <div className="kpi">
          <div className="kpi-label">Cost Variance</div>
          <div className="kpi-value" style={{ color: (data.totalEarned - data.totalSpent) >= 0 ? 'var(--success)' : 'var(--danger)' }}>
            ${(data.totalEarned - data.totalSpent).toLocaleString()}
          </div>
        </div>
      </div>

      {data.categories.map(cat => (
        <div key={cat.category} className="card" style={{ padding: 0 }}>
          <div className="cat-header">
            <strong>{cat.category}</strong>
            <div style={{ display: 'flex', gap: 12, fontSize: 13 }}>
              <span>Budget: <strong>${cat.totalBudget.toLocaleString()}</strong></span>
              <span>Earned: <strong>${cat.totalEarned.toLocaleString()}</strong></span>
              <span>Spent: <strong>${cat.totalSpent.toLocaleString()}</strong></span>
            </div>
          </div>
          <table className="data" style={{ borderRadius: 0 }}>
            <thead>
              <tr>
                <th>Description</th>
                <th style={{ textAlign: 'right' }}>Scheduled</th>
                <th style={{ textAlign: 'center' }}>% Done</th>
                <th style={{ textAlign: 'right' }}>Earned</th>
                <th style={{ textAlign: 'right' }}>Spent</th>
                <th style={{ textAlign: 'right' }}>Remaining</th>
              </tr>
            </thead>
            <tbody>
              {cat.lines.map(l => (
                <tr key={l.id}>
                  <td>{l.description}</td>
                  <td style={{ textAlign: 'right' }}>${l.budget.toLocaleString()}</td>
                  <td style={{ textAlign: 'center' }}>
                    <div style={{ background: 'var(--bg)', borderRadius: 4, overflow: 'hidden', position: 'relative', height: 14, width: 60, margin: '0 auto' }}>
                      <div style={{ width: `${l.percentComplete}%`, height: '100%', background: 'var(--accent)' }} />
                    </div>
                    <span style={{ fontSize: 11, color: 'var(--muted)' }}>{l.percentComplete}%</span>
                  </td>
                  <td style={{ textAlign: 'right' }}>${l.earned.toLocaleString()}</td>
                  <td style={{ textAlign: 'right' }}>${l.spent.toLocaleString()}</td>
                  <td style={{ textAlign: 'right', color: (l.budget - l.spent) >= 0 ? 'var(--success)' : 'var(--danger)' }}>
                    ${(l.budget - l.spent).toLocaleString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}
    </>
  )
}

function VarianceReport({ lines }: { lines: (Line & { category: string })[] }) {
  const overSpent = lines.filter(l => l.spent > l.budget && l.budget > 0)
  const overEarned = lines.filter(l => l.spent > l.earned && l.earned > 0 && l.spent > 0)
  const stalled = lines.filter(l => l.percentComplete === 0 && l.spent > 0)
  const nearLimit = lines.filter(l => l.budget > 0 && l.spent / l.budget >= 0.8 && l.spent <= l.budget)

  const empty = overSpent.length === 0 && overEarned.length === 0 && stalled.length === 0 && nearLimit.length === 0

  if (empty) return <p className="muted" style={{ marginTop: 16 }}>No variances to flag — every line is on track.</p>

  return (
    <>
      {overSpent.length > 0 && (
        <div className="card" style={{ borderColor: 'var(--danger)' }}>
          <strong style={{ color: 'var(--danger)' }}>🛑 Over budget ({overSpent.length})</strong>
          <ul style={{ marginTop: 8 }}>
            {overSpent.map(l => (
              <li key={l.id} style={{ fontSize: 14, marginBottom: 4 }}>
                <strong>[{l.category}]</strong> {l.description} — spent <strong>${l.spent.toLocaleString()}</strong> on a <strong>${l.budget.toLocaleString()}</strong> budget (over by ${(l.spent - l.budget).toLocaleString()})
              </li>
            ))}
          </ul>
        </div>
      )}
      {overEarned.length > 0 && (
        <div className="card" style={{ borderColor: 'var(--warning)' }}>
          <strong style={{ color: 'var(--warning)' }}>⚠ Spending ahead of progress ({overEarned.length})</strong>
          <p className="muted" style={{ fontSize: 12, margin: '4px 0' }}>You've paid more than the work earned so far.</p>
          <ul style={{ marginTop: 8 }}>
            {overEarned.map(l => (
              <li key={l.id} style={{ fontSize: 14, marginBottom: 4 }}>
                <strong>[{l.category}]</strong> {l.description} — earned <strong>${l.earned.toLocaleString()}</strong>, spent <strong>${l.spent.toLocaleString()}</strong>
              </li>
            ))}
          </ul>
        </div>
      )}
      {nearLimit.length > 0 && (
        <div className="card" style={{ borderColor: 'var(--warning)' }}>
          <strong style={{ color: 'var(--warning)' }}>📊 Approaching budget ({nearLimit.length})</strong>
          <ul style={{ marginTop: 8 }}>
            {nearLimit.map(l => (
              <li key={l.id} style={{ fontSize: 14, marginBottom: 4 }}>
                <strong>[{l.category}]</strong> {l.description} — {Math.round(l.spent / l.budget * 100)}% of budget used
              </li>
            ))}
          </ul>
        </div>
      )}
      {stalled.length > 0 && (
        <div className="card" style={{ borderColor: 'var(--accent)' }}>
          <strong style={{ color: 'var(--accent)' }}>🐢 Spent but no progress ({stalled.length})</strong>
          <p className="muted" style={{ fontSize: 12, margin: '4px 0' }}>You've paid for these but % complete is still 0.</p>
          <ul style={{ marginTop: 8 }}>
            {stalled.map(l => (
              <li key={l.id} style={{ fontSize: 14, marginBottom: 4 }}>
                <strong>[{l.category}]</strong> {l.description} — spent ${l.spent.toLocaleString()}
              </li>
            ))}
          </ul>
        </div>
      )}
    </>
  )
}

function fmtMonth(key: string): string {
  if (key === 'undated') return 'Undated'
  const [y, m] = key.split('-')
  const names = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
  const idx = parseInt(m, 10) - 1
  return names[idx] ? `${names[idx]} ${y}` : key
}
const money = (n: number) => '$' + (n || 0).toLocaleString(undefined, { maximumFractionDigits: 0 })

function SpendByMonthReport({ data }: { data: SpendByMonth }) {
  const { months, rows, unassigned, monthTotals, grandTotal, totalBudget } = data
  const spentRows = rows.filter(r => r.total > 0)
  if (months.length === 0) {
    return <p className="muted" style={{ marginTop: 16 }}>No expenses recorded yet. Add some to see the monthly breakdown.</p>
  }
  return (
    <div className="card" style={{ padding: 0, overflowX: 'auto' }}>
      <div className="cat-header">
        <strong>Spend by month</strong>
        <div style={{ display: 'flex', gap: 12, fontSize: 13 }}>
          <span>Spent: <strong>{money(grandTotal)}</strong></span>
          <span>Budget: <strong>{money(totalBudget)}</strong></span>
        </div>
      </div>
      <table className="data" style={{ borderRadius: 0 }}>
        <thead>
          <tr>
            <th>Budget line</th>
            <th style={{ textAlign: 'right' }}>Budget</th>
            {months.map(m => <th key={m} style={{ textAlign: 'right' }}>{fmtMonth(m)}</th>)}
            <th style={{ textAlign: 'right' }}>Total</th>
            <th style={{ textAlign: 'right' }}>Remaining</th>
          </tr>
        </thead>
        <tbody>
          {spentRows.map(r => (
            <tr key={r.lineId}>
              <td><span className="muted" style={{ fontSize: 12 }}>{r.category}</span><br />{r.description}</td>
              <td style={{ textAlign: 'right' }}>{money(r.budget)}</td>
              {months.map(m => (
                <td key={m} style={{ textAlign: 'right' }}>{r.byMonth[m] ? money(r.byMonth[m]) : <span className="muted">–</span>}</td>
              ))}
              <td style={{ textAlign: 'right' }}><strong>{money(r.total)}</strong></td>
              <td style={{ textAlign: 'right', color: r.remaining >= 0 ? 'var(--success)' : 'var(--danger)' }}>{money(r.remaining)}</td>
            </tr>
          ))}
          {unassigned && (
            <tr>
              <td><em className="muted">Unassigned (no budget line)</em></td>
              <td style={{ textAlign: 'right' }} className="muted">–</td>
              {months.map(m => (
                <td key={m} style={{ textAlign: 'right' }}>{unassigned.byMonth[m] ? money(unassigned.byMonth[m]) : <span className="muted">–</span>}</td>
              ))}
              <td style={{ textAlign: 'right' }}><strong>{money(unassigned.total)}</strong></td>
              <td style={{ textAlign: 'right' }} className="muted">–</td>
            </tr>
          )}
        </tbody>
        <tfoot>
          <tr>
            <th>Monthly total</th>
            <th />
            {months.map(m => <th key={m} style={{ textAlign: 'right' }}>{money(monthTotals[m] || 0)}</th>)}
            <th style={{ textAlign: 'right' }}>{money(grandTotal)}</th>
            <th />
          </tr>
        </tfoot>
      </table>
    </div>
  )
}

function TaxPivotReport({ data }: { data: TaxPivotData }) {
  const { months, categories, monthTotals, grandTotal } = data
  if (months.length === 0) {
    return <p className="muted" style={{ marginTop: 16 }}>No expenses recorded yet.</p>
  }
  return (
    <div className="card" style={{ padding: 0, overflowX: 'auto' }}>
      <div className="cat-header">
        <strong>Expenses by category &amp; month</strong>
        <span style={{ fontSize: 13 }}>Total: <strong>{money(grandTotal)}</strong></span>
      </div>
      <table className="data" style={{ borderRadius: 0 }}>
        <thead>
          <tr>
            <th>Category</th>
            {months.map(m => <th key={m} style={{ textAlign: 'right' }}>{fmtMonth(m)}</th>)}
            <th style={{ textAlign: 'right' }}>Total</th>
          </tr>
        </thead>
        <tbody>
          {categories.map(c => (
            <tr key={c.category}>
              <td>{c.category}</td>
              {months.map(m => (
                <td key={m} style={{ textAlign: 'right' }}>{c.byMonth[m] ? money(c.byMonth[m]) : <span className="muted">–</span>}</td>
              ))}
              <td style={{ textAlign: 'right' }}><strong>{money(c.total)}</strong></td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th>Total</th>
            {months.map(m => <th key={m} style={{ textAlign: 'right' }}>{money(monthTotals[m] || 0)}</th>)}
            <th style={{ textAlign: 'right' }}>{money(grandTotal)}</th>
          </tr>
        </tfoot>
      </table>
    </div>
  )
}

function CostSummary({ data }: { data: BudgetView }) {
  const max = Math.max(...data.categories.map(c => c.totalBudget), 1)
  return (
    <div className="card">
      <h3 style={{ marginTop: 0 }}>By category</h3>
      {data.categories.map(c => {
        const planned = (c.totalBudget / max) * 100
        const spent = (c.totalSpent / max) * 100
        return (
          <div key={c.category} style={{ marginBottom: 16 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13, marginBottom: 4 }}>
              <strong>{c.category}</strong>
              <span className="muted">${c.totalSpent.toLocaleString()} / ${c.totalBudget.toLocaleString()}</span>
            </div>
            <div style={{ position: 'relative', background: 'var(--bg)', borderRadius: 4, height: 18, overflow: 'hidden' }}>
              <div style={{ position: 'absolute', left: 0, top: 0, height: '100%', width: `${planned}%`, background: 'var(--panel-2)' }} />
              <div style={{ position: 'absolute', left: 0, top: 0, height: '100%', width: `${spent}%`, background: c.totalSpent > c.totalBudget ? 'var(--danger)' : 'var(--accent)', opacity: 0.85 }} />
            </div>
          </div>
        )
      })}
    </div>
  )
}
