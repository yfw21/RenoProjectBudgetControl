import { useEffect, useState } from 'react'
import { get, post, del } from '../api'

type Crew = { id: number; name: string; headcount: number; skills?: string | null; dayRateUsd: number }
type Equipment = { id: number; name: string; type?: string | null; dayRateUsd: number; rented: boolean }

export function Resources() {
  const [tab, setTab] = useState<'crews' | 'equipment'>('crews')
  const [crews, setCrews] = useState<Crew[]>([])
  const [equip, setEquip] = useState<Equipment[]>([])
  const [showForm, setShowForm] = useState(false)
  const [crewDraft, setCrewDraft] = useState({ name: '', headcount: 1, skills: '', dayRateUsd: 0 })
  const [equipDraft, setEquipDraft] = useState({ name: '', type: '', dayRateUsd: 0, rented: false })

  async function load() {
    setCrews(await get('/api/crews'))
    setEquip(await get('/api/equipment'))
  }
  useEffect(() => { load() }, [])

  async function createCrew() {
    await post('/api/crews', crewDraft)
    setCrewDraft({ name: '', headcount: 1, skills: '', dayRateUsd: 0 })
    setShowForm(false)
    await load()
  }
  async function createEquip() {
    await post('/api/equipment', equipDraft)
    setEquipDraft({ name: '', type: '', dayRateUsd: 0, rented: false })
    setShowForm(false)
    await load()
  }

  return (
    <div className="page">
      <div className="page-head">
        <h1>Resources</h1>
        <button className="btn-primary" onClick={() => setShowForm(true)}>+ Add {tab === 'crews' ? 'crew' : 'equipment'}</button>
      </div>

      <div className="tabs">
        <button className={`tab ${tab === 'crews' ? 'active' : ''}`} onClick={() => setTab('crews')}>Crews ({crews.length})</button>
        <button className={`tab ${tab === 'equipment' ? 'active' : ''}`} onClick={() => setTab('equipment')}>Equipment ({equip.length})</button>
      </div>

      {tab === 'crews' && (
        <table className="data">
          <thead><tr><th>Name</th><th>Headcount</th><th>Skills</th><th style={{textAlign:'right'}}>Day rate</th><th></th></tr></thead>
          <tbody>
            {crews.length === 0 && <tr><td colSpan={5} className="muted">No crews yet.</td></tr>}
            {crews.map(c => (
              <tr key={c.id}>
                <td>{c.name}</td><td>{c.headcount}</td><td>{c.skills || '—'}</td>
                <td style={{textAlign:'right'}}>${c.dayRateUsd}</td>
                <td style={{textAlign:'right'}}>
                  <button className="btn-ghost danger" onClick={async () => { if (confirm('Delete crew?')) { await del(`/api/crews/${c.id}`); load() } }}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {tab === 'equipment' && (
        <table className="data">
          <thead><tr><th>Name</th><th>Type</th><th>Owned/Rented</th><th style={{textAlign:'right'}}>Day rate</th><th></th></tr></thead>
          <tbody>
            {equip.length === 0 && <tr><td colSpan={5} className="muted">No equipment yet.</td></tr>}
            {equip.map(e => (
              <tr key={e.id}>
                <td>{e.name}</td><td>{e.type || '—'}</td><td>{e.rented ? 'Rented' : 'Owned'}</td>
                <td style={{textAlign:'right'}}>${e.dayRateUsd}</td>
                <td style={{textAlign:'right'}}>
                  <button className="btn-ghost danger" onClick={async () => { if (confirm('Delete equipment?')) { await del(`/api/equipment/${e.id}`); load() } }}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {showForm && (
        <div className="modal-backdrop" onClick={() => setShowForm(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            {tab === 'crews' ? (
              <>
                <h2>New crew</h2>
                <label>Name <input value={crewDraft.name} onChange={e => setCrewDraft({ ...crewDraft, name: e.target.value })} autoFocus /></label>
                <label>Headcount <input type="number" value={crewDraft.headcount} onChange={e => setCrewDraft({ ...crewDraft, headcount: parseInt(e.target.value) || 1 })} /></label>
                <label>Skills <input value={crewDraft.skills} onChange={e => setCrewDraft({ ...crewDraft, skills: e.target.value })} placeholder="e.g. masonry, plumbing" /></label>
                <label>Day rate (USD) <input type="number" step="0.01" value={crewDraft.dayRateUsd} onChange={e => setCrewDraft({ ...crewDraft, dayRateUsd: parseFloat(e.target.value) || 0 })} /></label>
                <div className="modal-actions">
                  <button className="btn-ghost" onClick={() => setShowForm(false)}>Cancel</button>
                  <button className="btn-primary" onClick={createCrew} disabled={!crewDraft.name}>Create</button>
                </div>
              </>
            ) : (
              <>
                <h2>New equipment</h2>
                <label>Name <input value={equipDraft.name} onChange={e => setEquipDraft({ ...equipDraft, name: e.target.value })} autoFocus /></label>
                <label>Type <input value={equipDraft.type} onChange={e => setEquipDraft({ ...equipDraft, type: e.target.value })} placeholder="e.g. excavator" /></label>
                <label>Day rate (USD) <input type="number" step="0.01" value={equipDraft.dayRateUsd} onChange={e => setEquipDraft({ ...equipDraft, dayRateUsd: parseFloat(e.target.value) || 0 })} /></label>
                <label><input type="checkbox" checked={equipDraft.rented} onChange={e => setEquipDraft({ ...equipDraft, rented: e.target.checked })} /> Rented (uncheck if owned)</label>
                <div className="modal-actions">
                  <button className="btn-ghost" onClick={() => setShowForm(false)}>Cancel</button>
                  <button className="btn-primary" onClick={createEquip} disabled={!equipDraft.name}>Create</button>
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
