import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import type { User } from './App'
import { useProjects } from './state/ProjectContext'
import { AiDrawer } from './AiDrawer'

const NAV = [
  { to: '/dashboard', label: 'Dashboard', icon: '◆' },
  { to: '/projects', label: 'Projects', icon: '▦' },
  { to: '/budget', label: 'Budget', icon: '▣' },
  { to: '/tasks', label: 'Tasks', icon: '▤' },
  { to: '/expenses', label: 'Expenses', icon: '$' },
  { to: '/resources', label: 'Resources', icon: '⚙' },
  { to: '/deliveries', label: 'Deliveries', icon: '⛟' },
  { to: '/notes', label: 'Notes', icon: '✎' },
  { to: '/reports', label: 'Reports', icon: '◈' },
  { to: '/settings', label: 'Settings', icon: '⚙' },
]

export function Layout({ user, onLogout }: { user: User; onLogout: () => void }) {
  const { projects, currentId, setCurrentId } = useProjects()
  const [aiOpen, setAiOpen] = useState(false)
  const [navOpen, setNavOpen] = useState(false)

  async function logout() {
    await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' })
    onLogout()
  }

  return (
    <div className="shell">
      <header className="topbar">
        <button className="hamburger" onClick={() => setNavOpen(v => !v)} title="Menu">☰</button>
        <div className="brand">Reno Ops</div>
        <div className="project-picker">
          <label>Project:</label>
          <select value={currentId ?? ''} onChange={e => setCurrentId(e.target.value ? parseInt(e.target.value) : null)}>
            <option value="">— all —</option>
            {projects.map(p => (
              <option key={p.id} value={p.id}>{p.name}</option>
            ))}
          </select>
        </div>
        <div className="topbar-right">
          <button className="ai-toggle" onClick={() => setAiOpen(v => !v)} title="AI assistant">✦ AI</button>
          <button className="logout" onClick={logout}>{user.username} · logout</button>
        </div>
      </header>

      <div className="body">
        <nav className={`sidebar ${navOpen ? 'open' : ''}`}>
          {NAV.map(item => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) => 'navlink' + (isActive ? ' active' : '')}
              onClick={() => setNavOpen(false)}
            >
              <span className="icon">{item.icon}</span>
              <span className="label">{item.label}</span>
            </NavLink>
          ))}
        </nav>

        <main className="content">
          <Outlet />
        </main>
      </div>

      <AiDrawer open={aiOpen} onClose={() => setAiOpen(false)} user={user} />
    </div>
  )
}
