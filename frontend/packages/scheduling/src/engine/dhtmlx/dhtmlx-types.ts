import type { ScheduleTask } from '../../model/types'

// DHTMLX gantt 实例的最小子集(只声明适配器用到的成员)。
export interface DhxGantt {
  config: Record<string, unknown>
  templates: Record<string, unknown>
  ext?: { zoom?: { setLevel?: (l: string) => void } }
  plugins?: (plugins: Record<string, boolean>) => void
  attachEvent: (name: string, handler: (...args: unknown[]) => unknown) => string
  detachEvent?: (id: string) => void
  init: (container: HTMLElement) => void
  parse: (data: { data: unknown[]; links: unknown[] }) => void
  clearAll: () => void
  getTask: (id: string | number) => DhxTask | undefined
  isTaskExists?: (id: string | number) => boolean
  selectTask?: (id: string | number) => void
  getTaskByIndex?: (index: number) => DhxTask | undefined
  updateTask?: (id: string | number) => void
  getScrollState?: () => { x: number; y: number }
  dateFromPos?: (x: number) => Date
  render: () => void
  setSizes?: () => void
  destructor?: () => void
  showDate?: (date: Date) => void
  /** 把某条任务滚进可视区(纵向到它那行、横向到它的时间)。搜索定位用。 */
  showTask?: (id: string | number) => void
  addMarker?: (marker: Record<string, unknown>) => string
  deleteMarker?: (id: string) => void
  addLink?: (link: Record<string, unknown>) => string
  deleteLink?: (id: string | number) => void
  isLinkExists?: (id: string | number) => boolean
  addTaskLayer?: (renderer: (task: DhxTask) => HTMLElement | boolean) => string
  getTaskPosition?: (
    task: DhxTask,
    start?: Date,
    end?: Date,
  ) => { left: number; top: number; width: number; height: number }
  date?: { add: (d: Date, n: number, unit: string) => Date }
}
export interface DhxTask {
  id: string
  text?: string
  start_date?: Date
  end_date?: Date
  planned_start?: Date
  planned_end?: Date
  parent?: string | number
  $resource?: string
  nerv?: ScheduleTask
}
