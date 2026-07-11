import {
  acknowledgeBusinessConsoleEquipmentAlarmMutationOptions,
  listBusinessConsoleEquipmentAlarmsQueryOptions,
  shelveBusinessConsoleEquipmentAlarmMutationOptions,
  type BusinessConsoleEquipmentAlarmListEnvelope,
  type BusinessConsoleTelemetryAlarmEventItem,
} from '@nerv-iip/api-client'
import { alarmLifecycleSortWeight } from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 100

/** 搁置时长档位（分钟）——交互稿固定三档：30 分钟 / 2 小时 / 8 小时。 */
export const ALARM_SHELVE_DURATIONS_MINUTES = [30, 120, 480] as const
export type AlarmShelveDurationMinutes = (typeof ALARM_SHELVE_DURATIONS_MINUTES)[number]

export interface EquipmentAlarmFilters {
  skip: number
  take: number
  deviceAssetId?: string
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  if (!envelope?.success) {
    return []
  }
  return envelope.data?.items ?? []
}

function listTotal(envelope: { success?: boolean; data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) {
    return 0
  }
  return envelope.data?.total ?? 0
}

/** 谓词匹配某 operationId 的查询键（`key[0]._id`）——跨 composable 实例失效同一列表读。 */
function isAlarmsQuery(entry: UseQueryEntry) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
  return keyParts.some(
    (part) =>
      typeof part === 'object' &&
      part !== null &&
      '_id' in part &&
      part._id === 'listBusinessConsoleEquipmentAlarms',
  )
}

function ignoreBackgroundError(_error: unknown) {}

/**
 * 未确认 = 状态 `raised`（既未确认、也未搁置、也未清除）。工作台角标与列表待办口径统一走它，
 * 避免"已搁置"被算进待处理。
 */
function isUnacknowledged(item: BusinessConsoleTelemetryAlarmEventItem) {
  return (item.status ?? '').trim().toLowerCase() === 'raised'
}

/**
 * 设备报警（读 + 确认/搁置）数据封装：镜像 business-console `useBusinessEquipment` 的报警映射与
 * ack/shelve 写口径，但 org/env 取登录主体 `useAuthStore().principal`（PDA 无 business-context store）。
 * scope 为空（未登录 / 主体缺 org/env）时不发请求（`enabled:false`）。
 *
 * 幂等：`acknowledge`/`shelve` 在 IndustrialTelemetry 领域侧是"首次写入即终态"的幂等操作
 * （`AlarmEvent.Acknowledge`/`Shelve` 对已确认/已搁置直接返回、不抛错），所以断网后用户重试同一
 * 报警不会重复应用——沿用既有请求层（MAN-460 `createTimeoutFetch`），不引入额外幂等键。
 */
export function useBusinessEquipmentAlarms(initialFilters: Partial<EquipmentAlarmFilters> = {}) {
  const auth = useAuthStore()
  const queryCache = useQueryCache()
  const filters = reactive<EquipmentAlarmFilters>({
    skip: 0,
    take: DEFAULT_TAKE,
    ...initialFilters,
  })

  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const actor = computed(() => auth.principal?.loginName ?? '')
  const scopeReady = computed(() => Boolean(organizationId.value && environmentId.value))

  const alarmsQuery = useQuery(() => ({
    ...listBusinessConsoleEquipmentAlarmsQueryOptions({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        skip: filters.skip,
        take: filters.take,
        ...optionalQuery('deviceAssetId', filters.deviceAssetId),
      },
    }),
    enabled: scopeReady.value,
  }))

  /** 写后失效列表读——按 operationId 谓词匹配，连带刷新工作台角标的独立实例查询。 */
  const invalidate = () =>
    void queryCache.invalidateQueries({ predicate: isAlarmsQuery }).catch(ignoreBackgroundError)

  const acknowledgeMutation = useMutation({
    ...acknowledgeBusinessConsoleEquipmentAlarmMutationOptions(),
    onSuccess: invalidate,
  })
  const shelveMutation = useMutation({
    ...shelveBusinessConsoleEquipmentAlarmMutationOptions(),
    onSuccess: invalidate,
  })

  const contextBody = () => ({
    organizationId: organizationId.value,
    environmentId: environmentId.value,
  })

  async function acknowledge(alarmEventId: string) {
    return acknowledgeMutation.mutateAsync({
      path: { alarmEventId },
      body: {
        ...contextBody(),
        acknowledgedAtUtc: new Date().toISOString(),
        acknowledgedBy: actor.value,
      },
    })
  }

  async function shelve(alarmEventId: string, durationMinutes: number, reason?: string) {
    return shelveMutation.mutateAsync({
      path: { alarmEventId },
      body: {
        ...contextBody(),
        durationMinutes,
        shelvedAtUtc: new Date().toISOString(),
        shelvedBy: actor.value,
        ...(reason && reason.trim() ? { reason: reason.trim() } : {}),
      },
    })
  }

  const alarms = computed<BusinessConsoleTelemetryAlarmEventItem[]>(() => {
    const items = listItems<BusinessConsoleTelemetryAlarmEventItem>(
      alarmsQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined,
    )
    // 未确认 > 已搁置 > 已确认 > 已清除；同档按发生时间倒序（新报警在前）。稳定副本，不改原数组。
    return [...items].sort((a, b) => {
      const weightDiff = alarmLifecycleSortWeight(a.status) - alarmLifecycleSortWeight(b.status)
      if (weightDiff !== 0) return weightDiff
      return (b.raisedAtUtc ?? '').localeCompare(a.raisedAtUtc ?? '')
    })
  })

  return {
    filters,
    alarms,
    total: computed(() =>
      listTotal(alarmsQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined),
    ),
    unacknowledgedCount: computed(() => alarms.value.filter(isUnacknowledged).length),
    pending: alarmsQuery.isLoading,
    error: alarmsQuery.error,
    actionPending: computed(
      () => acknowledgeMutation.isLoading.value || shelveMutation.isLoading.value,
    ),
    acknowledge,
    shelve,
    refresh: () => (scopeReady.value ? alarmsQuery.refetch() : Promise.resolve()),
  }
}
