export type SchedulingSceneElementKind =
  | 'grid-line'
  | 'row-label'
  | 'bar'
  | 'progress'
  | 'milestone'
  | 'dependency'
  | 'baseline'
  | 'today'
  | 'capacity'
  | 'conflict'
  | 'calendar-highlight'
  | 'selection'

export interface SchedulingScenePoint {
  x: number
  y: number
}

export interface SchedulingSceneElement {
  id: string
  kind: SchedulingSceneElementKind
  x: number
  y: number
  width?: number
  height?: number
  text?: string
  fill?: string
  stroke?: string
  severity?: 'info' | 'warning' | 'critical'
  points?: SchedulingScenePoint[]
  metadata?: Record<string, string | number | boolean>
}

export interface SchedulingScene {
  width: number
  height: number
  rowHeight: number
  elements: SchedulingSceneElement[]
}
