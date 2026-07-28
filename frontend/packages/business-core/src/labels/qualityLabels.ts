/**
 * 质量检验相关 code→中文 标签映射（纯 TS，框架无关，PC/PDA 共用）。
 *
 * 镜像 Business Quality 服务的 code 口径：检验任务状态（pending/in-progress/completed）
 * 与来源类型（receiving/operation/final），确保 PDA 待检执行页与 PC 待检工作台文案一致
 * （MAN-457 / #811 与 console C3-1 / #801 同源）。每个 label 函数大小写不敏感，未知/空值给中文兜底。
 */

function lookup(
  map: Record<string, string>,
  value: string | null | undefined,
  fallback: string,
): string {
  if (value == null) return fallback
  const normalized = value.trim().toLowerCase()
  if (normalized.length === 0) return fallback
  return map[normalized] ?? fallback
}

/**
 * 检验任务状态（镜像 Quality `InspectionTaskStatuses`：pending/in-progress/completed）。
 * PDA 待检列表默认只呈现 pending（未检）；已检任务 status=completed。
 */
export const inspectionTaskStatusLabels: Record<string, string> = {
  pending: '待检',
  'in-progress': '检验中',
  completed: '已完成',
  cancelled: '已取消',
}

export function inspectionTaskStatusLabel(value: string | null | undefined): string {
  return lookup(inspectionTaskStatusLabels, value, '未知状态')
}

/**
 * 检验任务来源类型（镜像 Quality `InspectionTask.SourceTypes`：receiving/operation/final）。
 * 用于待检列表的来源筛选 chips 与任务上下文展示。
 */
export const inspectionTaskSourceTypeLabels: Record<string, string> = {
  receiving: '来料检',
  operation: '过程检',
  final: '终检',
}

export function inspectionTaskSourceTypeLabel(value: string | null | undefined): string {
  return lookup(inspectionTaskSourceTypeLabels, value, '其他来源')
}

/** 来源类型有序表（筛选 chips 的候选，顺序即展示顺序）。 */
export const INSPECTION_TASK_SOURCE_TYPES: readonly string[] = ['receiving', 'operation', 'final']

/**
 * 质量单据（检验记录 / NCR）来源类型（镜像 Quality 检验记录 sourceType 口径：
 * operation/receiving/final/maintenance/customer-return）。与检验任务的三类来源不同，
 * 这里覆盖 NCR 分析、检验记录带出区等展示场景；未知码原样返回，不吞真值。
 */
export const qualitySourceTypeLabels: Record<string, string> = {
  operation: '工序',
  'in-process': '过程检验',
  receiving: '收货',
  final: '终检',
  maintenance: '维修',
  'customer-return': '客户退货',
}

export function qualitySourceTypeLabel(value: string | null | undefined): string {
  if (value == null) return ''
  const trimmed = value.trim()
  if (trimmed.length === 0) return ''
  return qualitySourceTypeLabels[trimmed.toLowerCase()] ?? trimmed
}

/**
 * 检验记录权威结论（镜像 Quality `InspectionRecordResults`：passed/rejected/conditional-release）。
 * 用于检验记录详情/结果页的结论展示。
 */
export const inspectionRecordResultLabels: Record<string, string> = {
  passed: '合格',
  rejected: '不合格',
  'conditional-release': '条件放行',
}

export function inspectionRecordResultLabel(value: string | null | undefined): string {
  return lookup(inspectionRecordResultLabels, value, '未知结论')
}

/**
 * 质量特性码 → 中文特性名。
 *
 * 特性没有全域目录读面（只能挂在检验方案下按 `inspectionPlanId` 取），所以 SPC 结果这类
 * **只带 `characteristicCode`、不带 name** 的读面在页面上会直接印出 `damping-force` 这样的
 * 英文码。这张表镜像 Quality 种子 `WorldHistoryQualitySpec.InspectionPlans` 的特性清单，
 * 让「特性」列在没有方案上下文时也能说人话。
 *
 * 注意：`UCL` / `LCL` / `Cpk` / `X-bar` / `Pareto` 是 SPC 通用术语，**保留英文不译**，不进本表。
 */
export const qualityCharacteristicLabels: Record<string, string> = {
  appearance: '外观检查',
  dimension: '关键尺寸',
  certificate: '材质证明',
  'damping-force': '阻尼力',
  stroke: '行程',
  leakage: '渗漏检查',
  labeling: '标识核对',
  packaging: '包装完整性',
}

/** 特性码→中文名；未收录时回吐原码（不编名字），空值回吐空串由调用方补占位。 */
export function qualityCharacteristicLabel(value: string | null | undefined): string {
  if (value == null) return ''
  const trimmed = value.trim()
  if (trimmed.length === 0) return ''
  return qualityCharacteristicLabels[trimmed.toLowerCase()] ?? trimmed
}
