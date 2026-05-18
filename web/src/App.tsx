import { useEffect, useState } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { Login } from './Login'
import { Layout } from './Layout'
import { ProjectProvider } from './state/ProjectContext'
import { Dashboard } from './pages/Dashboard'
import { Projects } from './pages/Projects'
import { Tasks } from './pages/Tasks'
import { Expenses } from './pages/Expenses'
import { Budget } from './pages/Budget'
import { Resources } from './pages/Resources'
import { Deliveries } from './pages/Deliveries'
import { Notes } from './pages/Notes'
import { Reports } from './pages/Reports'
import { Settings as SettingsPage } from './pages/Settings'

export type User = { id: number; username: string; role: string }

export function App() {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/auth/me', { credentials: 'include' })
      .then(r => (r.ok ? r.json() : null))
      .then(setUser)
      .finally(() => setLoading(false))
  }, [])

  if (loading) return <div className="app-loading" />
  if (!user) return <Login onAuthed={setUser} />

  return (
    <ProjectProvider>
      <BrowserRouter>
        <Routes>
          <Route element={<Layout user={user} onLogout={() => setUser(null)} />}>
            <Route index element={<Navigate to="/dashboard" replace />} />
            <Route path="dashboard" element={<Dashboard />} />
            <Route path="projects" element={<Projects />} />
            <Route path="tasks" element={<Tasks />} />
            <Route path="budget" element={<Budget />} />
            <Route path="expenses" element={<Expenses />} />
            <Route path="resources" element={<Resources />} />
            <Route path="deliveries" element={<Deliveries />} />
            <Route path="notes" element={<Notes />} />
            <Route path="reports" element={<Reports />} />
            <Route path="settings" element={<SettingsPage />} />
            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </ProjectProvider>
  )
}
