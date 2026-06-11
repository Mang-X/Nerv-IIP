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
export type TaskPriority = 'high' | 'medium' | 'low'

/** 业务执行状态(网格状态列显示)。 */
export interface TaskStatus {
  label: string
  tone: 'success' | 'info' | 'warning' | 'danger' | 'neutral'
}

/** 资源排产板的可切换分组维度(设备 / 班组 / 产线 / 工作中心 …)。 */
export interface SchedulingDimension {
  /** 维度键,对应 ScheduleTask.dimensions 的键。 */
  key: string
  /** 维度显示名(如「设备」「班组」「产线」)。 */
  label: string
}

/** 某工序在某维度上的归属值。 */
export interface DimensionValue {
  id: string
  label: string
}

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
  /** 多维分组归属(资源排产板按所选维度铺泳道)。键对应 ScheduleModel.groupDimensions。 */
  dimensions?: Record<string, DimensionValue>
  startUtc: string
  endUtc: string
  /** 计划基线(与实际 start/end 对比;甘特画"计划 vs 实际"双层条)。 */
  plannedStartUtc?: string
  plannedEndUtc?: string
  /** 0..1。 */
  progress?: number
  /** 网格列:负责人 / 优先级 / 状态。来源于 MES/工程数据(当前 APS 契约未提供 → 后端缺口)。 */
  owner?: string
  priority?: TaskPriority
  status?: TaskStatus
  /** 里程碑(独立节点,渲染为菱形,无时长)。 */
  isMilestone?: boolean
  /** 阶段里程碑:贴在本工序条末尾的菱形 + 标签(如"冲焊完成"),不独占一行。 */
  milestoneLabel?: string
  /** 分类着色键(按车间/工序);映射到分类色板,缺省用品牌色。 */
  colorKey?: string
  /** 排产板工单卡片信息(MES/工程数据;当前 APS 契约未提供 → 后端缺口)。 */
  product?: string
  quantity?: number
  dueUtc?: string
  /** 齐套率 0..1。 */
  kitting?: number
  /** 换型时间(分钟)。 */
  changeoverMin?: number
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
  /** 排产板泳道头指标:利用率 0..1+(>1 过载)、OEE 0..1、换型次数、待料风险数。 */
  utilization?: number
  oee?: number
  changeoverCount?: number
  materialRisk?: number
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
  /** 资源排产板可用的分组维度(为空时默认按工作中心)。 */
  groupDimensions?: SchedulingDimension[]
  horizon: { startUtc: string; endUtc: string }
  meta: { planId: string; status: PlanStatus; algorithmVersion: string }
}
