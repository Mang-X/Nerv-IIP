import {
  getBusinessConsoleQualityCapaQueryOptions,
  listBusinessConsoleQualityCalibrationRecordsQueryOptions,
  listBusinessConsoleQualityCapasQueryOptions,
  listBusinessConsoleQualityMeasuringDevicesQueryOptions,
  listBusinessConsoleQualitySpcControlChartsQueryOptions,
  type BusinessConsoleQualityCalibrationRecordItem,
  type BusinessConsoleQualityCapaActionItem,
  type BusinessConsoleQualityCapaItem,
  type BusinessConsoleQualityMeasuringDeviceItem,
  type BusinessConsoleQualitySpcControlChartItem,
} from '@nerv-iip/api-client'
import type { StatusTone } from '@nerv-iip/ui'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  type BusinessContextFields,
} from './businessContextBinding'

/**
 * 计量校准 / CAPA / SPC 控制图三本台账的读面。
 *
 * 三块都直接挂在 BusinessGateway facade 的 QueryOptions 上（服务端分页 + 服务端统计），
 * 页面不再自己数状态；业务范围（组织 / 环境）为空时一律不发请求。
 */

const DEFAULT_TAKE = 50

export type QualityMeasuringDeviceItem = BusinessConsoleQualityMeasuringDeviceItem
export type QualityCalibrationRecordItem = BusinessConsoleQualityCalibrationRecordItem
export type QualityCapaItem = BusinessConsoleQualityCapaItem
export type QualityCapaActionItem = BusinessConsoleQualityCapaActionItem
export type QualitySpcControlChartItem = BusinessConsoleQualitySpcControlChartItem

export interface QualityMeasuringDeviceFilters extends BusinessContextFields {
  deviceType?: string
  status?: string
  calibrationState?: string
  keyword?: string
  skip: number
  take: number
}

export interface QualityCalibrationRecordFilters extends BusinessContextFields {
  measuringDeviceId?: string
  keyword?: string
  skip: number
  take: number
}

export interface QualityCapaFilters extends BusinessContextFields {
  status?: string
  ownerUserId?: string
  sourceNcrId?: string
  overdueOnly?: boolean
  keyword?: string
  skip: number
  take: number
}

export interface QualitySpcControlChartFilters extends BusinessContextFields {
  skuCode?: string
  characteristicCode?: string
  workCenterId?: string
  locked?: boolean
  keyword?: string
  skip: number
  take: number
}

/**
 * 计量 / CAPA 的状态词条。
 *
 * 这几个状态码是本域私有的（`in-use` / `calibration` / `retired`、
 * `current` / `warning` / `overdue` / `unavailable`、`containment` / `corrective` /
 * `preventive`），共享状态词表里没有，所以在这里显式给出中文与语义色，
 * 徽标一律用显式 label + tone，避免界面上印出后端英文码。
 */
const MEASURING_DEVICE_STATUS_LABELS: Record<string, string> = {
  'in-use': '在用',
  calibration: '送检中',
  disabled: '停用',
  retired: '报废',
}

const CALIBRATION_STATE_PRESENTATIONS: Record<string, { label: string; tone: StatusTone }> = {
  current: { label: '有效期内', tone: 'success' },
  warning: { label: '临近到期', tone: 'warning' },
  overdue: { label: '已过期', tone: 'danger' },
  unavailable: { label: '不参与校准', tone: 'neutral' },
}

const CAPA_STATUS_LABELS: Record<string, string> = {
  open: '进行中',
  'effectiveness-verified': '效果已验证',
  closed: '已关闭',
}

const CAPA_ACTION_TYPE_LABELS: Record<string, string> = {
  containment: '临时措施',
  corrective: '纠正措施',
  preventive: '预防措施',
}

function labelOf(map: Record<string, string>, value: string | null | undefined, fallback = '未知') {
  const raw = (value ?? '').trim()
  return map[raw.toLowerCase()] ?? (raw || fallback)
}

/** 计量器具在用状态的中文名。 */
export function measuringDeviceStatusLabel(value: string | null | undefined) {
  return labelOf(MEASURING_DEVICE_STATUS_LABELS, value)
}

/** 校准状态的中文名 + 语义色（过期红 / 临近到期黄 / 有效期内绿 / 不参与灰）。 */
export function calibrationStatePresentation(value: string | null | undefined): {
  label: string
  tone: StatusTone
} {
  const raw = (value ?? '').trim().toLowerCase()
  return CALIBRATION_STATE_PRESENTATIONS[raw] ?? { label: raw || '未知', tone: 'neutral' }
}

/** CAPA 状态的中文名。 */
export function capaStatusLabel(value: string | null | undefined) {
  return labelOf(CAPA_STATUS_LABELS, value)
}

/** CAPA 状态的语义色。 */
export function capaStatusTone(value: string | null | undefined): StatusTone {
  const raw = (value ?? '').trim().toLowerCase()
  if (raw === 'closed') return 'success'
  if (raw === 'effectiveness-verified') return 'info'
  if (raw === 'open') return 'warning'
  return 'neutral'
}

/** 8D 措施类型的中文名。 */
export function capaActionTypeLabel(value: string | null | undefined) {
  return labelOf(CAPA_ACTION_TYPE_LABELS, value)
}

/** 剩余天数：负数表示已过期若干天。 */
export function calibrationRemainingText(days: number | null | undefined) {
  if (typeof days !== 'number' || !Number.isFinite(days)) return '无'
  if (days < 0) return `已过期 ${Math.abs(days)} 天`
  if (days === 0) return '今天到期'
  return `${days} 天`
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function defaultFilters<T extends BusinessContextFields>(base: T, initial: Partial<T>) {
  return bindBusinessContext(reactive({ ...base, ...initial }) as T)
}

/** 计量器具台账：状态统计由服务端给出，切换校准状态筛选时四个统计数不变。 */
export function useQualityMeasuringDevices(
  initialFilters: Partial<QualityMeasuringDeviceFilters> = {},
) {
  const filters = defaultFilters<QualityMeasuringDeviceFilters>(
    { organizationId: '', environmentId: '', skip: 0, take: DEFAULT_TAKE },
    initialFilters,
  )

  const devicesQuery = useQuery(() => ({
    ...listBusinessConsoleQualityMeasuringDevicesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('deviceType', filters.deviceType),
        ...optionalQuery('status', filters.status),
        ...optionalQuery('calibrationState', filters.calibrationState),
        ...optionalQuery('keyword', filters.keyword),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(filters),
  }))

  const response = computed(() =>
    devicesQuery.data.value?.success ? (devicesQuery.data.value.data ?? undefined) : undefined,
  )

  return {
    filters,
    measuringDevices: computed<QualityMeasuringDeviceItem[]>(() => response.value?.items ?? []),
    measuringDevicesError: devicesQuery.error,
    measuringDevicesPending: devicesQuery.isLoading,
    measuringDevicesTotal: computed(() => response.value?.total ?? 0),
    measuringDeviceCurrentCount: computed(() => response.value?.currentCount ?? 0),
    measuringDeviceWarningCount: computed(() => response.value?.warningCount ?? 0),
    measuringDeviceOverdueCount: computed(() => response.value?.overdueCount ?? 0),
    measuringDeviceUnavailableCount: computed(() => response.value?.unavailableCount ?? 0),
    refreshMeasuringDevices: () => refetchWithBusinessContext(filters, devicesQuery),
  }
}

/** 校准记录流水。 */
export function useQualityCalibrationRecords(
  initialFilters: Partial<QualityCalibrationRecordFilters> = {},
) {
  const filters = defaultFilters<QualityCalibrationRecordFilters>(
    { organizationId: '', environmentId: '', skip: 0, take: DEFAULT_TAKE },
    initialFilters,
  )

  const recordsQuery = useQuery(() => ({
    ...listBusinessConsoleQualityCalibrationRecordsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('measuringDeviceId', filters.measuringDeviceId),
        ...optionalQuery('keyword', filters.keyword),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(filters),
  }))

  const response = computed(() =>
    recordsQuery.data.value?.success ? (recordsQuery.data.value.data ?? undefined) : undefined,
  )

  return {
    filters,
    calibrationRecords: computed<QualityCalibrationRecordItem[]>(() => response.value?.items ?? []),
    calibrationRecordsError: recordsQuery.error,
    calibrationRecordsPending: recordsQuery.isLoading,
    calibrationRecordsTotal: computed(() => response.value?.total ?? 0),
    refreshCalibrationRecords: () => refetchWithBusinessContext(filters, recordsQuery),
  }
}

/** CAPA 台账：进行中 / 待关单 / 已关闭 / 逾期 四个统计数由服务端给出。 */
export function useQualityCapas(initialFilters: Partial<QualityCapaFilters> = {}) {
  const filters = defaultFilters<QualityCapaFilters>(
    { organizationId: '', environmentId: '', skip: 0, take: DEFAULT_TAKE },
    initialFilters,
  )

  const capasQuery = useQuery(() => ({
    ...listBusinessConsoleQualityCapasQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('status', filters.status),
        ...optionalQuery('ownerUserId', filters.ownerUserId),
        ...optionalQuery('sourceNcrId', filters.sourceNcrId),
        ...(filters.overdueOnly ? { overdueOnly: true } : {}),
        ...optionalQuery('keyword', filters.keyword),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(filters),
  }))

  const response = computed(() =>
    capasQuery.data.value?.success ? (capasQuery.data.value.data ?? undefined) : undefined,
  )

  return {
    filters,
    capas: computed<QualityCapaItem[]>(() => response.value?.items ?? []),
    capasError: capasQuery.error,
    capasPending: capasQuery.isLoading,
    capasTotal: computed(() => response.value?.total ?? 0),
    capaOpenCount: computed(() => response.value?.openCount ?? 0),
    capaEffectivenessVerifiedCount: computed(() => response.value?.effectivenessVerifiedCount ?? 0),
    capaClosedCount: computed(() => response.value?.closedCount ?? 0),
    capaOverdueCount: computed(() => response.value?.overdueCount ?? 0),
    refreshCapas: () => refetchWithBusinessContext(filters, capasQuery),
  }
}

/** 单张 CAPA 详情：抽屉打开、且业务范围就绪时才取数。 */
export function useQualityCapaDetail(source: () => QualityCapaDetailSource) {
  const enabled = computed(() => {
    const value = source()
    return (
      value.organizationId.trim().length > 0 &&
      value.environmentId.trim().length > 0 &&
      value.correctiveActionId.trim().length > 0
    )
  })

  const detailQuery = useQuery(() => {
    const value = source()
    return {
      ...getBusinessConsoleQualityCapaQueryOptions({
        path: { correctiveActionId: value.correctiveActionId },
        query: {
          organizationId: value.organizationId,
          environmentId: value.environmentId,
        },
      }),
      enabled: enabled.value,
    }
  })

  return {
    capaDetail: computed<QualityCapaItem | undefined>(() =>
      detailQuery.data.value?.success ? (detailQuery.data.value.data ?? undefined) : undefined,
    ),
    capaDetailError: detailQuery.error,
    capaDetailPending: detailQuery.isLoading,
    refreshCapaDetail: () => (enabled.value ? detailQuery.refetch() : Promise.resolve(undefined)),
  }
}

export interface QualityCapaDetailSource extends BusinessContextFields {
  correctiveActionId: string
}

/** SPC 控制图台账：控制限与锁定状态的登记面，不参与控制图的实时计算。 */
export function useQualitySpcControlCharts(
  initialFilters: Partial<QualitySpcControlChartFilters> = {},
) {
  const filters = defaultFilters<QualitySpcControlChartFilters>(
    { organizationId: '', environmentId: '', skip: 0, take: DEFAULT_TAKE },
    initialFilters,
  )

  const chartsQuery = useQuery(() => ({
    ...listBusinessConsoleQualitySpcControlChartsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('characteristicCode', filters.characteristicCode),
        ...optionalQuery('workCenterId', filters.workCenterId),
        ...(filters.locked === undefined ? {} : { locked: filters.locked }),
        ...optionalQuery('keyword', filters.keyword),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(filters),
  }))

  const response = computed(() =>
    chartsQuery.data.value?.success ? (chartsQuery.data.value.data ?? undefined) : undefined,
  )

  return {
    filters,
    spcControlCharts: computed<QualitySpcControlChartItem[]>(() => response.value?.items ?? []),
    spcControlChartsError: chartsQuery.error,
    spcControlChartsPending: chartsQuery.isLoading,
    spcControlChartsTotal: computed(() => response.value?.total ?? 0),
    spcControlChartsLockedCount: computed(() => response.value?.lockedCount ?? 0),
    refreshSpcControlCharts: () => refetchWithBusinessContext(filters, chartsQuery),
  }
}
