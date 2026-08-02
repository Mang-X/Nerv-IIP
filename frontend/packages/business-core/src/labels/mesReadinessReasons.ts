export type MesReadinessReasonCategory = '前序工序' | '物料齐套' | '质量' | '设备' | '其他门禁'

export interface MesReadinessReasonDisplay {
  code: string
  category: MesReadinessReasonCategory
  /** 短标签，适用于徽标或移动端原因标题。 */
  label: string
  /** 服务端返回的具体业务事实，例如缺哪个物料、缺多少。 */
  detail: string
  nextStep: string
}

type KnownMesReadinessReasonDisplay = Omit<MesReadinessReasonDisplay, 'detail'>

const materialNextStep = '在工单详情「用料齐套」发起领料；物料到线边后确认收料'
const equipmentNextStep = '处理报警/停机或改派可用设备'

export const MES_READINESS_REASON_DISPLAYS: Readonly<
  Record<string, KnownMesReadinessReasonDisplay>
> = {
  MATERIAL_SHORTAGE: {
    code: 'MATERIAL_SHORTAGE',
    category: '物料齐套',
    label: '物料缺料',
    nextStep: materialNextStep,
  },
  MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: {
    code: 'MATERIAL_REQUIREMENT_SNAPSHOT_MISSING',
    category: '物料齐套',
    label: '齐套快照缺失',
    nextStep: '确认工单已绑定生产版本，重新下达以生成齐套需求快照',
  },
  PREVIOUS_OPERATION_INCOMPLETE: {
    code: 'PREVIOUS_OPERATION_INCOMPLETE',
    category: '前序工序',
    label: '前序工序未完工',
    nextStep: '先完成前道工序再开工本工序',
  },
  QUALITY_PLAN_MISSING: {
    code: 'QUALITY_PLAN_MISSING',
    category: '质量',
    label: '检验方案缺失',
    nextStep: '维护并启用 SKU 与工序检验方案后重新检查',
  },
  QUALITY_HOLD_ACTIVE: {
    code: 'QUALITY_HOLD_ACTIVE',
    category: '质量',
    label: '质量冻结中',
    nextStep: '处理质量冻结、NCR 或放行状态后再执行',
  },
  EQUIPMENT_UNAVAILABLE: {
    code: 'EQUIPMENT_UNAVAILABLE',
    category: '设备',
    label: '设备不可用',
    nextStep: equipmentNextStep,
  },
  EQUIPMENT_MAINTENANCE_CONFLICT: {
    code: 'EQUIPMENT_MAINTENANCE_CONFLICT',
    category: '设备',
    label: '维修占用冲突',
    nextStep: '调整维修窗口、等待释放或选择替代设备',
  },
  'equipment.activeAlarm': {
    code: 'equipment.activeAlarm',
    category: '设备',
    label: '设备报警未解除',
    nextStep: equipmentNextStep,
  },
  'equipment.stateUnavailable': {
    code: 'equipment.stateUnavailable',
    category: '设备',
    label: '设备状态不可用',
    nextStep: equipmentNextStep,
  },
  'equipment.downtime': {
    code: 'equipment.downtime',
    category: '设备',
    label: '设备停机',
    nextStep: equipmentNextStep,
  },
  'equipment.maintenanceWindow': {
    code: 'equipment.maintenanceWindow',
    category: '设备',
    label: '维修窗口冲突',
    nextStep: '调整维修窗口、等待释放或选择替代设备',
  },
  'equipment.inspectionRequired': {
    code: 'equipment.inspectionRequired',
    category: '设备',
    label: '设备点检未完成',
    nextStep: '完成设备点检后重新检查',
  },
  'equipment.sourceStale': {
    code: 'equipment.sourceStale',
    category: '设备',
    label: '设备状态已过期',
    nextStep: '恢复设备状态采集并等待最新状态',
  },
  'equipment.tagMappingMissing': {
    code: 'equipment.tagMappingMissing',
    category: '设备',
    label: '设备标签映射缺失',
    nextStep: '维护设备标签映射后重新检查',
  },
  'equipment.noEligibleSubstitute': {
    code: 'equipment.noEligibleSubstitute',
    category: '设备',
    label: '无可用替代设备',
    nextStep: '恢复原设备或维护可替代设备后重新检查',
  },
  'equipment.sourceUnavailable': {
    code: 'equipment.sourceUnavailable',
    category: '设备',
    label: '设备来源不可用',
    nextStep: '稍后重试或联系管理员检查设备来源服务',
  },
  SOURCE_SERVICE_UNAVAILABLE: {
    code: 'SOURCE_SERVICE_UNAVAILABLE',
    category: '其他门禁',
    label: '来源服务不可用',
    nextStep: '稍后重试或联系管理员检查来源服务',
  },
}

function isReasonCode(value: string) {
  return /^[A-Z0-9_]+$/.test(value) || /^equipment\.[A-Za-z][A-Za-z0-9]*$/.test(value)
}

/** 将 `CODE: 中文事实` 解析为跨 PC/PDA 一致的可读门禁原因。 */
export function describeMesReadinessReason(reason: string): MesReadinessReasonDisplay {
  const trimmedReason = reason.trim()
  const separator = trimmedReason.indexOf(':')
  const head = separator > 0 ? trimmedReason.slice(0, separator) : ''
  const hasCodePrefix = head.length > 0 && isReasonCode(head)
  const code = hasCodePrefix ? head : trimmedReason
  const detail = hasCodePrefix ? trimmedReason.slice(separator + 1).trim() : ''
  const known = MES_READINESS_REASON_DISPLAYS[code]
  if (known) return { ...known, detail }
  return {
    code,
    category: '其他门禁',
    label: detail || trimmedReason,
    detail: '',
    nextStep: '查看阻塞详情并按来源业务页面处理',
  }
}

/** 同码原因合并为一条，并保留所有不同的服务端业务事实。 */
export function describeMesReadinessReasons(
  reasons?: readonly string[] | null,
): MesReadinessReasonDisplay[] {
  const merged = new Map<string, MesReadinessReasonDisplay>()
  const details = new Map<string, string[]>()
  for (const raw of reasons ?? []) {
    const display = describeMesReadinessReason(raw)
    const bucket = details.get(display.code)
    if (!bucket) {
      merged.set(display.code, display)
      details.set(display.code, display.detail ? [display.detail] : [])
      continue
    }
    if (display.detail && !bucket.includes(display.detail)) bucket.push(display.detail)
  }
  return [...merged.values()].map((display) => ({
    ...display,
    detail: (details.get(display.code) ?? []).join('、'),
  }))
}
