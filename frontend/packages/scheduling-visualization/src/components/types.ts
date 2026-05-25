import type { GanttSelection } from '../model/gantt'
import type { ScheduleSelection } from '../model/schedule'

export type SchedulingWorkspaceMode = 'gantt' | 'schedule'
export type SchedulingLinkMode = 'none' | 'all' | 'selection'

export type SchedulingWorkspaceSelection =
  | { source: 'gantt'; selection: GanttSelection }
  | { source: 'schedule'; selection: ScheduleSelection }

export interface SchedulingDetailField {
  label: string
  value: string
}

export interface SchedulingDetailView {
  eyebrow: string
  title: string
  status?: string
  description?: string
  fields: SchedulingDetailField[]
  conflictTitle?: string
  conflictDescription?: string
  conflictResolutionHint?: string
}
