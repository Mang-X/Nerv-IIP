import type { ConflictSeverity, SchedulingStatus } from './gantt'

export interface ScheduleResource {
  id: string
  name: string
  kind: 'work-center' | 'line' | 'equipment' | 'team'
  workCenterCode: string
  capacityPerShift: number
  calendarLabel: string
}

export interface ScheduleOperation {
  id: string
  resourceId: string
  workOrderCode: string
  operationCode: string
  name: string
  skuCode: string
  start: string
  end: string
  progress: number
  status: SchedulingStatus
  isLocked?: boolean
  loadPercent: number
  conflictIds?: string[]
}

export interface ScheduleCapacityBand {
  id: string
  resourceId: string
  start: string
  end: string
  loadPercent: number
  capacityPercent: number
  isOverloaded: boolean
}

export interface ScheduleConflict {
  id: string
  targetId: string
  targetKind: 'operation' | 'resource'
  severity: ConflictSeverity
  title: string
  description: string
  resolutionHint: string
}

export interface ScheduleFixture {
  id: string
  name: string
  rangeStart: string
  rangeEnd: string
  resources: ScheduleResource[]
  operations: ScheduleOperation[]
  capacityBands: ScheduleCapacityBand[]
  conflicts: ScheduleConflict[]
}

export interface ScheduleRow extends ScheduleResource {
  operationIds: string[]
}

export type ScheduleSelection =
  | { kind: 'resource'; id: string }
  | { kind: 'operation'; id: string }
  | { kind: 'conflict'; id: string }

export interface ScheduleChartProps extends ScheduleFixture {
  selected?: ScheduleSelection
}

export function groupScheduleRows(
  resources: ScheduleResource[],
  operations: ScheduleOperation[],
): ScheduleRow[] {
  return resources.map((resource) => ({
    ...resource,
    operationIds: operations
      .filter((operation) => operation.resourceId === resource.id)
      .sort((a, b) => a.start.localeCompare(b.start))
      .map((operation) => operation.id),
  }))
}
