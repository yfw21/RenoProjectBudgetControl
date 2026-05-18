import { useEffect, useState } from 'react'
import { get, post } from '../api'
import { useProjects } from '../state/ProjectContext'

type PO = { id: number; vendor: string; project: string; status: string; expectedDate: string; actualDate?: string | null; totalUsd: number }

export function Deliveries() {
  const { current } = useProjects()
  const [pos, setPos] = useState<PO[]>([])

  async function load() {
    const url = current ? `/api/purchase-orders?projectId=${current.id}` : '/api/purchase-orders'
    setPos(await get(url))
  }
  useEffect(() => { load() }, [current])

  async function receive(id: number) {
    if (!confirm('Mark as received?')) return
    await post(`/api/purchase-orders/${id}/receive`, {})
    await load()
  }

  return (
    <div className="page">
      <div className="page-head">
        <h1>Deliveries {current && `— ${current.name}`}</h1>
        <span className="muted">Use AI or chat to create new POs for now</span>
      </div>

      <table className="data">
        <thead>
          <tr><th>Vendor</th><th>Project</th><th>Status</th><th>Expected</th><th>Actual</th><th style={{textAlign:'right'}}>Total</th><th></th></tr>
        </thead>
        <tbody>
          {pos.length === 0 && <tr><td colSpan={7} className="muted">No purchase orders yet.</td></tr>}
          {pos.map(p => (
            <tr key={p.id}>
              <td><strong>{p.vendor}</strong></td>
              <td>{p.project}</td>
              <td><span className={`pill pill-${p.status}`}>{p.status}</span></td>
              <td>{p.expectedDate?.slice(0, 10)}</td>
              <td>{p.actualDate?.slice(0, 10) || '—'}</td>
              <td style={{textAlign:'right'}}>${p.totalUsd?.toLocaleString()}</td>
              <td style={{textAlign:'right'}}>
                {!p.actualDate && <button className="btn-primary" onClick={() => receive(p.id)}>Mark received</button>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
