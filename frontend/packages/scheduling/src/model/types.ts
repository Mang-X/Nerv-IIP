// 引擎无关的排程数据模型。所有字段为引擎可消费的归一化形态,不含任何引擎私有结构。
// 这是「换引擎」接缝的数据契约:DHTMLX 适配器与自研适配器都只消费 ScheduleModel。

export type PlanStatus = 'preview' | 'generated' | 'released'
export type TimeScale = 'hour' | 'day' | 'week' | 'month' | 'auto'
export type ConflictSeverity = 'info' | 'warning' | 'error'
export type ConflictReason =
  | 'dueDate'
  | 'capacity'
  | 'calendar'
  | 'material'
  | 'quality'
  | 'equipment'
  | 'noEligibleResource'
  | 'outsideHorizon'
  | 'invalidLockedAssignment'
  | 'predecessorUnscheduled'
export type ChangeType = 'added' | 'moved' | 'delayed' | 'preserved' | 'blocked'

export interface ScheduleTask {
  /** assignmentId 优先,缺则 `${orderId}:${operationId}`。 */
  id: string
  orderId: string
  operationId: string
  operationSequence: number
  /** 工单分组父节点 id(order 视图用)。 */
  parentId?: string
  type: 'order' | 'operation'
  /** 业务化显示名(工序名/工单名),不暴露工程语言。 */
  text: string
  resourceId?: string
  workCenterId?: string
  startUtc: string
  endUtc: string
  /** 0..1。 */
  progress?: number
  locked: boolean
  hasConflict: boolean
  conflictReason?: ConflictReason | null
}

export interface ScheduleLink {
  id: string
  /** ScheduleTask.id。 */
  source: string
  /** ScheduleTask.id。 */
  target: string
  /** MVP 仅 FS,由 operationSequence 派生。 */
  type: 'finish_to_start'
}

export interface ScheduleResource {
  /** resourceId / workCenterId。 */
  id: string
  /** 业务化资源名。 */
  text: string
  capacityMinutesPerDay?: number
}

export interface ResourceLoadBucket {
  resourceId: string
  windowStartUtc: string
  windowEndUtc: string
  assignedMinutes: number
  availableMinutes: number
  /** 0..1+(>1 过载)。 */
  utilization: number
}

export interface ScheduleConflict {
  id: string
  reason: ConflictReason
  severity: ConflictSeverity
  orderId?: string | null
  operationId?: string | null
  resourceId?: string | null
  message: string
  /** 关联的 ScheduleTask.id(便于选中/滚动定位)。 */
  taskId?: string
}

export interface UnscheduledItem {
  orderId: string
  operationId: string
  reason: ConflictReason
  message: string
}

export interface ScheduleChange {
  orderId: string
  operationId: string
  changeType: ChangeType
  message: string
  taskId?: string
}

export interface ScheduleModel {
  tasks: ScheduleTask[]
  links: ScheduleLink[]
  resources: ScheduleResource[]
  loads: ResourceLoadBucket[]
  conflicts: ScheduleConflict[]
  unscheduled: UnscheduledItem[]
  /** 重预览 diff。 */
  changes: ScheduleChange[]
  horizon: { startUtc: string; endUtc: string }
  meta: { planId: string; status: PlanStatus; algorithmVersion: string }
}
