export type SchedulingStatus = 'planned' | 'ready' | 'running' | 'blocked' | 'done'
export type ConflictSeverity = 'info' | 'warning' | 'critical'

export interface GanttTask {
  id: string
  parentId?: string
  name: string
  code: string
  start: string
  end: string
  progress: number
  status: SchedulingStatus
  assignee?: string
  baselineStart?: string
  baselineEnd?: string
  isMilestone?: boolean
  isLocked?: boolean
  children?: GanttTask[]
  conflictIds?: string[]
}

export interface GanttDependency {
  id: string
  sourceTaskId: string
  targetTaskId: string
  type: 'finish-start' | 'start-start' | 'finish-finish' | 'start-finish'
}

export interface GanttConflict {
  id: string
  taskId: string
  severity: ConflictSeverity
  title: string
  description: string
  resolutionHint: string
}

export interface GanttRow extends GanttTask {
  depth: number
  hasChildren: boolean
}

export interface GanttFixture {
  id: string
  name: string
  rangeStart: string
  rangeEnd: string
  tasks: GanttTask[]
  dependencies: GanttDependency[]
  conflicts: GanttConflict[]
}

export type GanttSelection =
  | { kind: 'task'; id: string }
  | { kind: 'dependency'; id: string }
  | { kind: 'conflict'; id: string }

export interface GanttChartProps extends GanttFixture {
  expandedTaskIds?: string[]
  selected?: GanttSelection
}

export function flattenGanttTasks(
  tasks: GanttTask[],
  expandedTaskIds: Set<string>,
  depth = 0,
): GanttRow[] {
  return tasks.flatMap((task) => {
    const children = task.children ?? []
    const row: GanttRow = { ...task, depth, hasChildren: children.length > 0 }

    if (children.length === 0 || !expandedTaskIds.has(task.id)) {
      return [row]
    }

    return [row, ...flattenGanttTasks(children, expandedTaskIds, depth + 1)]
  })
}
