import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { get } from '../api'

export type Project = {
  id: number
  name: string
  address?: string | null
  client?: string | null
  status: string
  startDate?: string | null
  endDate?: string | null
}

type Ctx = {
  projects: Project[]
  currentId: number | null
  current: Project | null
  setCurrentId: (id: number | null) => void
  refresh: () => Promise<void>
}

const ProjectContext = createContext<Ctx | null>(null)

export function ProjectProvider({ children }: { children: ReactNode }) {
  const [projects, setProjects] = useState<Project[]>([])
  const [currentId, setCurrentIdState] = useState<number | null>(() => {
    const saved = localStorage.getItem('currentProjectId')
    return saved ? parseInt(saved, 10) : null
  })

  const setCurrentId = (id: number | null) => {
    setCurrentIdState(id)
    if (id == null) localStorage.removeItem('currentProjectId')
    else localStorage.setItem('currentProjectId', String(id))
  }

  const refresh = async () => {
    const list = await get<Project[]>('/api/projects')
    setProjects(list)
    if (currentId == null && list.length > 0) setCurrentId(list[0].id)
    if (currentId != null && !list.find(p => p.id === currentId)) {
      setCurrentId(list[0]?.id ?? null)
    }
  }

  useEffect(() => { refresh() }, [])

  const current = projects.find(p => p.id === currentId) ?? null
  return (
    <ProjectContext.Provider value={{ projects, currentId, current, setCurrentId, refresh }}>
      {children}
    </ProjectContext.Provider>
  )
}

export function useProjects() {
  const ctx = useContext(ProjectContext)
  if (!ctx) throw new Error('useProjects must be inside ProjectProvider')
  return ctx
}
