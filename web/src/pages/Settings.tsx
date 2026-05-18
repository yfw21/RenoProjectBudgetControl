import { useEffect, useState } from 'react'
import { get } from '../api'

type Settings = {
  llmProvider: string
  ollamaModel: string
  anthropicModel: string
  anthropicConfigured: boolean
  briefingRunAt: string
  currency: string
}

export function Settings() {
  const [s, setS] = useState<Settings | null>(null)
  useEffect(() => { get<Settings>('/api/settings').then(setS) }, [])

  if (!s) return <div className="page">Loading…</div>

  return (
    <div className="page">
      <h1>Settings</h1>

      <div className="card">
        <h2>AI provider</h2>
        <div className="settings-row">
          <div>
            <div><strong>Current provider:</strong> {s.llmProvider}</div>
            {s.llmProvider === 'ollama' && <div className="muted">Model: {s.ollamaModel} (local, free, slow)</div>}
            {s.llmProvider === 'anthropic' && <div className="muted">Model: {s.anthropicModel} {s.anthropicConfigured ? '(key configured)' : '(no API key set)'}</div>}
          </div>
        </div>
        <p className="muted" style={{ marginTop: 12 }}>
          To switch providers, edit <code>BudgetApi/appsettings.json</code> and restart the API.
          For Anthropic, also set the <code>ANTHROPIC_API_KEY</code> environment variable.
        </p>
      </div>

      <div className="card">
        <h2>Daily briefing</h2>
        <div className="muted">Runs at <strong>{s.briefingRunAt}</strong> local time, summarizing each active project.</div>
      </div>

      <div className="card">
        <h2>Currency</h2>
        <div className="muted">All amounts are in {s.currency}.</div>
      </div>
    </div>
  )
}
