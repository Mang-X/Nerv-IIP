import {
  acknowledgeBusinessConsoleEquipmentAlarm,
  confirmBusinessConsoleOperation,
  getBusinessConsoleEquipmentAvailabilityQueryOptions,
  getBusinessConsoleEquipmentDeviceQueryOptions,
  getBusinessConsoleEquipmentOverviewQueryOptions,
  listBusinessConsoleEquipmentAlarmsQueryOptions,
  listBusinessConsoleEquipmentAlarms,
  shelveBusinessConsoleEquipmentAlarm,
  unshelveBusinessConsoleEquipmentAlarm,
  type BusinessConsoleEquipmentAlarmListEnvelope,
  type BusinessConsoleEquipmentDeviceDetailEnvelope,
  type BusinessConsoleEquipmentDeviceDetailResponse,
  type BusinessConsoleEquipmentOverviewEnvelope,
  type BusinessConsoleEquipmentOverviewResponse,
  type BusinessConsoleTelemetryAlarmEventItem,
  type EquipmentRuntimeAlarmSummary,
  type EquipmentRuntimeAvailabilityEnvelope,
  type EquipmentRuntimeAvailabilityWindow,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from './useListFreshness'
import { useBusinessMasterDataResources } from './useBusinessMasterData'
import { hasBusinessContext, refetchWithBusinessContext } from './businessContextBinding'
import { executeLifecycleAction } from './lifecycleAction'

const DEFAULT_DEVICE_ASSET_IDS = ''

function requirePendingPayloadSnapshot<T extends object>(snapshot: unknown, operation: string): T {
  if (!snapshot || typeof snapshot !== 'object') {
    throw new Error(`${operation}缺少冻结的待处理载荷，请保留当前页面并人工核实。`)
  }
  return snapshot as T
}

// 看板默认范围 = 全部设备。后端 overview/availability 要求 deviceAssetIds 非空（最多 50 个），
// 故未手动指定范围时，自动取设备资源列表（device-asset）的全部编号带入。
export const MAX_DEVICE_ASSET_IDS = 50

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
}

export interface BusinessEquipmentOverviewFilters {
  deviceAssetIds: string
}

export interface BusinessEquipmentAvailabilityFilters extends BusinessEquipmentOverviewFilters {
  windowStartUtc: string
  windowEndUtc: string
  workCenterIds?: string
}

export interface BusinessEquipmentDeviceFilters {
  deviceAssetId: string
}

const equipmentReasonDisplays: Record<string, EquipmentReasonDisplay> = {
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

  return (
    equipmentReasonDisplays[normalizedCode] ?? {
      code: normalizedCode,
      label: normalizedCode,
      nextStep: '查看设备详情并处理来源业务单据',
    }
  )
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

function defaultOverviewFilters(): BusinessEquipmentOverviewFilters {
  return reactive({
    deviceAssetIds: DEFAULT_DEVICE_ASSET_IDS,
  })
}

function defaultAvailabilityFilters(): BusinessEquipmentAvailabilityFilters {
  const now = new Date()
  const start = new Date(now)
  start.setHours(8, 0, 0, 0)
  const end = new Date(start)
  end.setHours(16, 0, 0, 0)

  return reactive({
    deviceAssetIds: DEFAULT_DEVICE_ASSET_IDS,
    windowStartUtc: start.toISOString(),
    windowEndUtc: end.toISOString(),
  })
}

function defaultDeviceFilters(deviceAssetId = ''): BusinessEquipmentDeviceFilters {
  return reactive({
    deviceAssetId,
  })
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function normalizeDeviceAssetIds(deviceAssetIds: string) {
  return deviceAssetIds
    .split(',')
    .map((deviceAssetId) => deviceAssetId.trim())
    .filter((deviceAssetId) => deviceAssetId.length > 0)
    .join(',')
}

function toContextQuery(businessContext: ReturnType<typeof useBusinessContextStore>) {
  return {
    organizationId: businessContext.organizationId,
    environmentId: businessContext.environmentId,
  }
}

function toOverviewQuery(
  businessContext: ReturnType<typeof useBusinessContextStore>,
  filters: BusinessEquipmentOverviewFilters,
) {
  return {
    ...toContextQuery(businessContext),
    deviceAssetIds: normalizeDeviceAssetIds(filters.deviceAssetIds),
  }
}

function toAvailabilityQuery(
  businessContext: ReturnType<typeof useBusinessContextStore>,
  filters: BusinessEquipmentAvailabilityFilters,
) {
  return {
    ...toOverviewQuery(businessContext, filters),
    windowStartUtc: filters.windowStartUtc,
    windowEndUtc: filters.windowEndUtc,
    ...optionalQuery('workCenterIds', filters.workCenterIds),
  }
}

function unwrapData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

function listItems<
  TItem,
  TEnvelope extends { success?: boolean; data?: { items?: TItem[] } | null },
>(envelope: TEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

/**
 * 看板默认范围解析：用户手动输入了设备号则按输入过滤；未输入时回退到「全部设备」——
 * 取设备资源列表（device-asset）的全部编号（后端最多 50 个，超出截断）。返回逗号分隔串。
 *
 * 台账读面的 error 必须透出：台账 4xx/5xx 时「在册几台」本身就是未知，
 * 页面不能把它渲染成「0 台设备」——那是把故障伪装成现场没有设备。
 */
function useEffectiveDeviceAssetIds(filters: BusinessEquipmentOverviewFilters) {
  const {
    resources: deviceResources,
    resourcesError: deviceRosterError,
    resourcesPending: deviceResourcesPending,
    resourcesTotal: deviceRosterTotal,
    refreshResources: refreshDeviceRoster,
  } = useBusinessMasterDataResources('device-asset')

  const rosterDeviceCodes = computed(() =>
    deviceResources.value
      .map((device) => device.code?.trim())
      .filter((code): code is string => Boolean(code)),
  )

  const allDeviceAssetIds = computed(() =>
    rosterDeviceCodes.value.slice(0, MAX_DEVICE_ASSET_IDS).join(','),
  )

  const effectiveDeviceAssetIds = computed(() => {
    const manual = normalizeDeviceAssetIds(filters.deviceAssetIds)
    return manual.length > 0 ? manual : allDeviceAssetIds.value
  })

  return {
    deviceResourcesPending,
    deviceRosterError,
    // 台账在册总数（后端 total，含未加载到本页的记录）；台账取不到时回 0，调用方须先看 error。
    deviceRosterTotal: computed(() =>
      Math.max(deviceRosterTotal.value, rosterDeviceCodes.value.length),
    ),
    effectiveDeviceAssetIds,
    // 本次实际带入查询的设备台数——与在册总数不等时说明发生了截断，页面必须如实说明。
    effectiveDeviceAssetIdCount: computed(
      () =>
        normalizeDeviceAssetIds(effectiveDeviceAssetIds.value).split(',').filter(Boolean).length,
    ),
    refreshDeviceRoster,
  }
}

export function useBusinessEquipmentOverview() {
  const businessContext = useBusinessContextStore()
  const filters = defaultOverviewFilters()
  const {
    deviceResourcesPending,
    deviceRosterError,
    deviceRosterTotal,
    effectiveDeviceAssetIds,
    effectiveDeviceAssetIdCount,
    refreshDeviceRoster,
  } = useEffectiveDeviceAssetIds(filters)
  const contextReady = computed(() => hasBusinessContext(businessContext))
  const overviewEnabled = computed(
    () => contextReady.value && effectiveDeviceAssetIds.value.length > 0,
  )
  const overviewQuery = useQuery(() => ({
    ...getBusinessConsoleEquipmentOverviewQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetIds: effectiveDeviceAssetIds.value,
      },
    }),
    enabled: overviewEnabled.value,
  }))

  const overview = computed(() =>
    unwrapData<BusinessConsoleEquipmentOverviewResponse, BusinessConsoleEquipmentOverviewEnvelope>(
      overviewQuery.data.value,
    ),
  )

  const overviewPending = computed(
    () => deviceResourcesPending.value || overviewQuery.isLoading.value,
  )
  // 四态判定顺序：上下文未就绪 → 在查（含重试）→ 任一读面失败 → 已取到。
  // 台账失败与 overview 失败都算 error：两者任一取不到，「在册几台/几台在跑」就都是未知。
  const overviewState = computed<BusinessEquipmentQueryState>(() => {
    if (!contextReady.value) return 'idle'
    if (overviewPending.value) return 'loading'
    if (deviceRosterError.value || overviewQuery.error.value) return 'error'
    return 'ready'
  })

  return {
    activeBlocks: computed(() => overview.value?.activeBlocks ?? []),
    contextReady,
    deviceRosterError,
    deviceRosterTotal,
    devices: computed(() => overview.value?.devices ?? []),
    effectiveDeviceAssetIdCount,
    filters,
    overview,
    overviewError: overviewQuery.error,
    overviewPending,
    overviewState,
    refreshOverview: () => (overviewEnabled.value ? overviewQuery.refetch() : Promise.resolve()),
    refreshDeviceRoster,
  }
}

export function useBusinessEquipmentAvailability() {
  const businessContext = useBusinessContextStore()
  const filters = defaultAvailabilityFilters()
  const availabilityEnabled = computed(
    () =>
      hasBusinessContext(businessContext) &&
      normalizeDeviceAssetIds(filters.deviceAssetIds).length > 0,
  )
  const availabilityQuery = useQuery(() => ({
    ...getBusinessConsoleEquipmentAvailabilityQueryOptions({
      query: toAvailabilityQuery(businessContext, filters),
    }),
    enabled: availabilityEnabled.value,
  }))

  return {
    availability: computed(() =>
      unwrapData<
        NonNullable<EquipmentRuntimeAvailabilityEnvelope['data']>,
        EquipmentRuntimeAvailabilityEnvelope
      >(availabilityQuery.data.value),
    ),
    availabilityError: availabilityQuery.error,
    availabilityPending: availabilityQuery.isLoading,
    availabilityWindows: computed<EquipmentRuntimeAvailabilityWindow[]>(() =>
      listItems<EquipmentRuntimeAvailabilityWindow, EquipmentRuntimeAvailabilityEnvelope>(
        availabilityQuery.data.value,
      ),
    ),
    filters,
    refreshAvailability: () =>
      availabilityEnabled.value ? availabilityQuery.refetch() : Promise.resolve(),
  }
}

export function useBusinessEquipmentDevice(deviceAssetId?: string) {
  const businessContext = useBusinessContextStore()
  const filters = defaultDeviceFilters(deviceAssetId)
  const deviceEnabled = computed(
    () => hasBusinessContext(businessContext) && filters.deviceAssetId.trim().length > 0,
  )
  const deviceQuery = useQuery(() => ({
    ...getBusinessConsoleEquipmentDeviceQueryOptions({
      path: { deviceAssetId: filters.deviceAssetId },
      query: toContextQuery(businessContext),
    }),
    enabled: deviceEnabled.value,
  }))

  const device = computed<BusinessConsoleEquipmentDeviceDetailResponse | undefined>(() =>
    unwrapData<
      BusinessConsoleEquipmentDeviceDetailResponse,
      BusinessConsoleEquipmentDeviceDetailEnvelope
    >(deviceQuery.data.value),
  )

  return {
    activeAlarms: computed<EquipmentRuntimeAlarmSummary[]>(
      () => device.value?.currentState?.activeAlarms ?? [],
    ),
    availabilityWindows: computed<EquipmentRuntimeAvailabilityWindow[]>(
      () => device.value?.availability?.items ?? [],
    ),
    device,
    deviceError: deviceQuery.error,
    devicePending: deviceQuery.isLoading,
    filters,
    refreshDevice: () => (deviceEnabled.value ? deviceQuery.refetch() : Promise.resolve()),
  }
}

export function useBusinessEquipmentAlarms() {
  const auth = useAuthStore()
  const businessContext = useBusinessContextStore()
  const alarmsQuery = useQuery(() => ({
    ...listBusinessConsoleEquipmentAlarmsQueryOptions({
      query: toContextQuery(businessContext),
    }),
    enabled: hasBusinessContext(businessContext),
  }))
  async function readAlarm(
    alarmEventId: string,
    action: 'acknowledge' | 'shelve' | 'unshelve',
    evaluatedAtUtc?: string,
    idempotentReplay = false,
  ) {
    const response = await listBusinessConsoleEquipmentAlarms({
      query: {
        ...toContextQuery(businessContext),
        alarmEventId,
        skip: 0,
        take: 1,
      },
      throwOnError: false,
    })
    if (response.error !== undefined) throw response.error
    if (!response.data?.success) throw response.data ?? new Error('读取报警最新状态失败')
    const item = response.data.data?.items?.find(
      (candidate) => candidate.alarmEventId === alarmEventId,
    )
    return item
      ? {
          domain: 'iiot-alarm' as const,
          action,
          facts: {
            status: item.status,
            acknowledgedAtUtc: item.acknowledgedAtUtc,
            shelvedAtUtc: item.shelvedAtUtc,
            shelvedUntilUtc: item.shelvedUntilUtc,
            evaluatedAtUtc,
            idempotentReplay,
          },
        }
      : undefined
  }

  const alarmsScopeReady = computed(() => hasBusinessContext(businessContext))
  const alarmsResponse = useScopeBoundListResponse(
    () => alarmsQuery.data.value,
    () => `${businessContext.organizationId.trim()}:${businessContext.environmentId.trim()}`,
    alarmsScopeReady,
  )
  const alarmsLastUpdatedAt = useListFreshness(alarmsResponse, alarmsScopeReady)
  const {
    hasSuccessfulResponse: alarmsHasSuccessfulResponse,
    hasFailedResponse: alarmsHasFailedResponse,
  } = useListResponseState(alarmsResponse, alarmsScopeReady, () => alarmsQuery.isLoading.value)

  async function acknowledgeAlarm(alarmEventId: string, acknowledgedBy: string) {
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      operationType: 'iiot.alarm.acknowledge',
      payloadFingerprint: JSON.stringify({ alarmEventId, acknowledgedBy }),
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(
      scope,
      () => globalThis.crypto?.randomUUID?.() ?? `alarm-acknowledge-${Date.now()}-${Math.random()}`,
      { acknowledgedAtUtc: new Date().toISOString() },
    )
    const stablePayload = requirePendingPayloadSnapshot<{ acknowledgedAtUtc: string }>(
      pending.payloadSnapshot,
      '报警确认',
    )
    const result = await completePendingBusinessIntent(scope, async () => {
      const envelope = await executeLifecycleAction({
        readLatest: () => readAlarm(alarmEventId, 'acknowledge', undefined, Boolean(restored)),
        command: () =>
          acknowledgeBusinessConsoleEquipmentAlarm({
            path: { alarmEventId },
            body: {
              ...toContextQuery(businessContext),
              acknowledgedAtUtc: stablePayload.acknowledgedAtUtc,
              acknowledgedBy,
              idempotencyKey: pending.idempotencyKey,
            },
            throwOnError: false,
          }),
      })
      if (!envelope) throw new Error('报警确认未返回业务信封')
      await confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'iiot.alarm.acknowledge',
        expectedIdempotencyKey: pending.idempotencyKey,
        expectedResourceId: alarmEventId,
      })
      return envelope
    })
    await refetchWithBusinessContext(businessContext, alarmsQuery)
    return result
  }

  async function shelveAlarm(
    alarmEventId: string,
    shelvedBy: string,
    durationMinutes = 30,
    reason?: string,
    options?: {
      attempt?: 'initial' | 'retry'
      shelvedAtUtc?: string
      idempotencyKey?: string
    },
  ) {
    // Freeze the shelve instant so a retried batch reuses the same window; the backend
    // derives the shelve window from shelvedAtUtc + idempotencyKey (first-write-wins),
    // so a stable key makes re-submitting the same batch a no-op instead of extending it.
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      operationType: 'iiot.alarm.shelve',
      payloadFingerprint: JSON.stringify({
        alarmEventId,
        shelvedBy,
        durationMinutes,
        reason: reason?.trim() ?? '',
      }),
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(
      scope,
      () =>
        options?.idempotencyKey ??
        globalThis.crypto?.randomUUID?.() ??
        `alarm-shelve-${Date.now()}-${Math.random()}`,
      { shelvedAtUtc: options?.shelvedAtUtc ?? new Date().toISOString() },
    )
    const stablePayload = requirePendingPayloadSnapshot<{ shelvedAtUtc: string }>(
      pending.payloadSnapshot,
      '报警搁置',
    )
    const result = await completePendingBusinessIntent(scope, async () => {
      const envelope = await executeLifecycleAction({
        readLatest: () =>
          readAlarm(
            alarmEventId,
            'shelve',
            restored ? stablePayload.shelvedAtUtc : undefined,
            Boolean(restored),
          ),
        command: () =>
          shelveBusinessConsoleEquipmentAlarm({
            path: { alarmEventId },
            body: {
              ...toContextQuery(businessContext),
              durationMinutes,
              idempotencyKey: pending.idempotencyKey,
              reason,
              shelvedAtUtc: stablePayload.shelvedAtUtc,
              shelvedBy,
            },
            throwOnError: false,
          }),
      })
      if (!envelope) throw new Error('报警搁置未返回业务信封')
      await confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'iiot.alarm.shelve',
        expectedIdempotencyKey: pending.idempotencyKey,
        expectedResourceId: alarmEventId,
      })
      return envelope
    })
    await refetchWithBusinessContext(businessContext, alarmsQuery)
    return result
  }

  async function unshelveAlarm(alarmEventId: string) {
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      operationType: 'iiot.alarm.unshelve',
      payloadFingerprint: JSON.stringify({ alarmEventId }),
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(
      scope,
      () => globalThis.crypto?.randomUUID?.() ?? `alarm-unshelve-${Date.now()}-${Math.random()}`,
      { unshelvedAtUtc: new Date().toISOString() },
    )
    const stablePayload = requirePendingPayloadSnapshot<{ unshelvedAtUtc: string }>(
      pending.payloadSnapshot,
      '报警取消搁置',
    )
    const result = await completePendingBusinessIntent(scope, async () => {
      const envelope = await executeLifecycleAction({
        readLatest: () => readAlarm(alarmEventId, 'unshelve', undefined, Boolean(restored)),
        command: () =>
          unshelveBusinessConsoleEquipmentAlarm({
            path: { alarmEventId },
            body: {
              ...toContextQuery(businessContext),
              idempotencyKey: pending.idempotencyKey,
              unshelvedAtUtc: stablePayload.unshelvedAtUtc,
            },
            throwOnError: false,
          }),
      })
      if (!envelope) throw new Error('报警取消搁置未返回业务信封')
      await confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'iiot.alarm.unshelve',
        expectedIdempotencyKey: pending.idempotencyKey,
        expectedResourceId: alarmEventId,
      })
      return envelope
    })
    await refetchWithBusinessContext(businessContext, alarmsQuery)
    return result
  }

  return {
    acknowledgeAlarm,
    alarms: computed<BusinessConsoleTelemetryAlarmEventItem[]>(() =>
      listItems<BusinessConsoleTelemetryAlarmEventItem, BusinessConsoleEquipmentAlarmListEnvelope>(
        alarmsResponse.value,
      ),
    ),
    alarmsError: alarmsQuery.error,
    alarmsPending: alarmsQuery.isLoading,
    alarmsTotal: computed(() =>
      alarmsResponse.value?.success ? (alarmsResponse.value.data?.total ?? 0) : 0,
    ),
    alarmsOrganizationId: computed(() => businessContext.organizationId),
    alarmsEnvironmentId: computed(() => businessContext.environmentId),
    alarmsLastUpdatedAt,
    alarmsHasSuccessfulResponse,
    alarmsHasFailedResponse,
    refreshAlarms: () => refetchWithBusinessContext(businessContext, alarmsQuery),
    shelveAlarm,
    unshelveAlarm,
  }
}
