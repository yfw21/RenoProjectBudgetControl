import { TablePanel } from './TablePanel'
import { GanttPanel } from './GanttPanel'
import { ConflictsPanel } from './ConflictsPanel'
import { SummaryPanel } from './SummaryPanel'

export type PanelData = { _component: string; title?: string; data: any }

export function Panel({ payload }: { payload: PanelData }) {
  switch (payload._component) {
    case 'table': return <TablePanel title={payload.title} rows={payload.data} />
    case 'gantt': return <GanttPanel title={payload.title} rows={payload.data} />
    case 'conflicts': return <ConflictsPanel title={payload.title} rows={payload.data} />
    case 'summary': return <SummaryPanel title={payload.title} data={payload.data} />
    default:
      return (
        <div>
          <strong>{payload.title ?? payload._component}</strong>
          <pre style={{ overflowX: 'auto', fontSize: 12 }}>{JSON.stringify(payload.data, null, 2)}</pre>
        </div>
      )
  }
}
