import {
  type EquipmentRuntimeAvailabilityWindow,
  type EquipmentRuntimeSourceType,
} from '@nerv-iip/api-client'
import type { NvMetricTone } from '@nerv-iip/ui'

export type EquipmentTone = 'success' | 'danger' | 'muted'

/**
 * 读面四态：区分「上下文未就绪 / 加载中 / 取不到 / 已取到」。
 * 页面必须按这四态分别呈现——把失败和未查询都渲染成「0 + 暂无数据」会把故障伪装成现场正常。
 */
export type BusinessEquipmentQueryState = 'idle' | 'loading' | 'error' | 'ready'

export interface EquipmentReasonDisplay {
  code: string
  label: string
  nextStep: string
  /** 阻塞徽标色。 */
  tone: NvMetricTone
}

/** 字典条目：tone 缺省为危险色，只有「尚未接入」这类还没配置、不算异常的原因才标中性色。 */
type EquipmentReasonEntry = Omit<EquipmentReasonDisplay, 'tone'> & { tone?: NvMetricTone }

const equipmentReasonDisplays: Record<string, EquipmentReasonEntry> = {
  'equipment.activeAlarm': {
    code: 'equipment.activeAlarm',
    label: '设备报警未解除',
    nextStep: '处理并解除设备报警后重新检查',
  },
  'equipment.stateUnavailable': {
    code: 'equipment.stateUnavailable',
    label: '设备状态不可运行',
    nextStep: '确认设备恢复运行后重新检查',
  },
  'equipment.downtime': {
    code: 'equipment.downtime',
    label: '设备停机中',
    nextStep: '关闭停机事件或改派可用设备',
  },
  'equipment.maintenanceWindow': {
    code: 'equipment.maintenanceWindow',
    label: '维修保养占用',
    nextStep: '调整维修窗口、等待释放或选择替代设备',
  },
  'equipment.inspectionRequired': {
    code: 'equipment.inspectionRequired',
    label: '点检未通过',
    nextStep: '完成点检并确认结果后重新检查',
  },
  'equipment.sourceStale': {
    code: 'equipment.sourceStale',
    label: '采集数据过期',
    nextStep: '检查采集连接并刷新设备状态',
  },
  'equipment.sourceNotConnected': {
    code: 'equipment.sourceNotConnected',
    label: '尚未接入采集',
    nextStep: '为设备配置采集连接后即可看到运行状态',
    tone: 'neutral',
  },
  'equipment.tagMappingMissing': {
    code: 'equipment.tagMappingMissing',
    label: '采集点未配置',
    nextStep: '补齐设备采集点映射',
  },
  'equipment.noEligibleSubstitute': {
    code: 'equipment.noEligibleSubstitute',
    label: '无可替代设备',
    nextStep: '调整排程或维护设备能力配置',
  },
}

export function describeEquipmentReason(code: string): EquipmentReasonDisplay {
  const normalizedCode = code.trim()

  // 词表外的原因码不把英文码送上屏，显示「—」。
  const entry = equipmentReasonDisplays[normalizedCode] ?? {
    code: normalizedCode,
    label: '—',
    nextStep: '查看设备详情并处理来源业务单据',
  }
  return { ...entry, tone: entry.tone ?? 'danger' }
}

const SOURCE_NOT_CONNECTED_REASON = 'equipment.sourceNotConnected'

export type EquipmentSourceStatus = 'fresh' | 'stale' | 'notConnected'

/**
 * 设备采集接入状态。是否接入以 IIoT 的可用性窗口为准：它只在设备既没有状态快照、也没有任何样本时
 * 发「尚未接入采集」（只上报样本不带状态的连接器照样算已接入）；已接入的按当前是否新鲜判断。
 * 页面不要再从 currentState 自行推断。
 */
export function equipmentSourceStatus(
  isSourceFresh: boolean | null | undefined,
  windows: ReadonlyArray<Pick<EquipmentRuntimeAvailabilityWindow, 'deviceAssetId' | 'reasonCode'>>,
  deviceAssetId?: string | null,
): EquipmentSourceStatus {
  if (isSourceFresh) return 'fresh'
  const notConnected = windows.some(
    (w) =>
      w.reasonCode === SOURCE_NOT_CONNECTED_REASON &&
      (deviceAssetId == null || w.deviceAssetId === deviceAssetId),
  )
  return notConnected ? 'notConnected' : 'stale'
}

const AVAILABILITY_STATUS_DISPLAY: Record<string, { label: string; tone: NvMetricTone }> = {
  available: { label: '可用', tone: 'success' },
  unavailable: { label: '不可用', tone: 'danger' },
}

/**
 * 可用性窗口状态的说法与颜色。只有明确「可用」才是绿色；未知（尚未接入、采集过期等）不能说成可用，
 * 用中性色照实说。词表漏了的码值同样按未知处理，不把后端码值上屏。
 */
export function describeAvailabilityStatus(status: string | null | undefined) {
  return (
    AVAILABILITY_STATUS_DISPLAY[status?.trim().toLowerCase() ?? ''] ?? {
      label: '状态未知',
      tone: 'neutral' as NvMetricTone,
    }
  )
}

/** 列表 / 抽屉里「实时数据」一格的说法与颜色：从没接入采集不是数据中断，用中性色照实说。 */
export const EQUIPMENT_FRESHNESS_DISPLAY: Record<
  EquipmentSourceStatus,
  { label: string; tone: NvMetricTone }
> = {
  fresh: { label: '实时', tone: 'success' },
  stale: { label: '暂无实时数据', tone: 'warning' },
  notConnected: { label: '尚未接入采集', tone: 'neutral' },
}

/**
 * 可用性窗口的「关联业务」。设备状态 / 采集类窗口没有来源单据，关联的就是设备本身，显示设备编码；
 * IIoT 在这类窗口里回填的是调用方传入的设备引用（可能是公开 ID），不能上屏。其余窗口显示来源单据的人读标识。
 */
export function describeAvailabilityWindowReference(
  window: { sourceType?: EquipmentRuntimeSourceType | null; sourceReferenceLabel?: string | null },
  deviceCode: string,
) {
  if (window.sourceType === 'device-state' || window.sourceType === 'stale-source') {
    return deviceCode || '—'
  }
  return window.sourceReferenceLabel?.trim() || '—'
}

export function equipmentStatusTone(status: string | null | undefined): EquipmentTone {
  const value = status?.trim().toLowerCase()
  if (value === 'running' || value === 'ready' || value === 'idle') {
    return 'success'
  }
  if (value === 'faulted' || value === 'stopped' || value === 'offline' || value === 'down') {
    return 'danger'
  }
  return 'muted'
}
