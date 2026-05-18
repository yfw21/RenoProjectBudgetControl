import { useState } from 'react'
import type { User } from './App'

export function Login({ onAuthed }: { onAuthed: (u: User) => void }) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit() {
    setBusy(true)
    setError('')
    try {
      const r = await fetch(`/api/auth/${mode}`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      })
      const data = await r.json()
      if (!r.ok) {
        setError(data.error || 'Failed')
        return
      }
      onAuthed(data)
    } catch (e: any) {
      setError(e.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login">
      <h2>{mode === 'login' ? 'Sign in' : 'Create account'}</h2>
      <input
        placeholder="Username"
        value={username}
        onChange={e => setUsername(e.target.value)}
        autoFocus
      />
      <input
        type="password"
        placeholder="Password"
        value={password}
        onChange={e => setPassword(e.target.value)}
        onKeyDown={e => e.key === 'Enter' && submit()}
      />
      {error && <div className="error">{error}</div>}
      <button onClick={submit} disabled={busy || !username || !password}>
        {busy ? '...' : mode === 'login' ? 'Sign in' : 'Register'}
      </button>
      <div className="switch" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
        {mode === 'login' ? 'No account? Register' : 'Have an account? Sign in'}
      </div>
    </div>
  )
}
