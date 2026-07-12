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

/** 「未确认」状态码——工作台角标与列表待办口径统一走它（既未确认、未搁置、未清除）。 */
const RAISED_STATUS = 'raised'

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

/** 谓词匹配 alarms 列表读的所有查询键（含全量与 status=raised 两支）——跨 composable 实例失效。 */
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

function authScope() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const actor = computed(() => auth.principal?.loginName ?? '')
  const scopeReady = computed(() => Boolean(organizationId.value && environmentId.value))
  return { organizationId, environmentId, actor, scopeReady }
}

/**
 * 工作台报警角标数据源（未确认数）。**服务端 `status=raised` 过滤查询，取 `total`**——
 * 全量口径，不受列表首页 take 上限影响（>100 时也不会把角标算成 0）。`take:1` 只为省流量，
 * `total` 仍是符合条件的全部条数。
 */
export function useUnacknowledgedAlarmCount() {
  const { organizationId, environmentId, scopeReady } = authScope()
  const raisedQuery = useQuery(() => ({
    ...listBusinessConsoleEquipmentAlarmsQueryOptions({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        status: RAISED_STATUS,
        skip: 0,
        take: 1,
      },
    }),
    enabled: scopeReady.value,
  }))

  return {
    unacknowledgedCount: computed(() =>
      listTotal(raisedQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined),
    ),
    pending: raisedQuery.isLoading,
  }
}

/**
 * 设备报警（读 + 确认/搁置）数据封装：org/env 取登录主体 `useAuthStore().principal`
 * （PDA 无 business-context store）。scope 为空时不发请求（`enabled:false`）。
 *
 * **未确认优先（跨分页，服务端落实）**：列表读由服务端 `ListAlarmEventsQuery` 按生命周期
 * 排序（未确认 > 已搁置 > 已确认 > 已清除，同档发生时间倒序）**在分页前**排好，因此首页永远是
 * 全部未确认在前，已处理项不会插到未确认之前；前端再做一次同口径排序仅为兜底。角标另用
 * {@link useUnacknowledgedAlarmCount} 的 `status=raised` total（全量准确）。
 *
 * **幂等（断网/延迟重投不重复）**：`shelve` 携带调用方铸造的**持久幂等键** `idempotencyKey`，
 * 后端 `AlarmEvent.ShelveIdempotencyKey` 记录最后一次已应用的搁置操作键，**同键的延迟重复投递
 * 一律 no-op**（即便原窗口已过期/解除回到 raised），这是稳定时间戳单独做不到的。`acknowledge`
 * 领域侧 `AcknowledgedAtUtc is not null` 即 first-write-wins，天然幂等，无需额外键。页面对
 * 「已发出但结果未知」的失败不盲目重试（交给 verify）。
 */
export function useBusinessEquipmentAlarms(initialFilters: Partial<EquipmentAlarmFilters> = {}) {
  const { organizationId, environmentId, actor, scopeReady } = authScope()
  const queryCache = useQueryCache()
  const filters = reactive<EquipmentAlarmFilters>({
    skip: 0,
    take: DEFAULT_TAKE,
    ...initialFilters,
  })

  const listQuery = useQuery(() => ({
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

  /** 确认。`atUtc` 由调用方铸造并跨重试复用；领域 first-write-wins 保证重复确认为 no-op。 */
  async function acknowledge(alarmEventId: string, atUtc: string) {
    return acknowledgeMutation.mutateAsync({
      path: { alarmEventId },
      body: {
        ...contextBody(),
        acknowledgedAtUtc: atUtc,
        acknowledgedBy: actor.value,
      },
    })
  }

  /**
   * 搁置。`atUtc` 固定窗口 `[atUtc, atUtc+时长]`；`idempotencyKey` 为持久判重键，
   * 跨重试/延迟重投复用同一键 → 后端一律 no-op、不重复应用、不延长窗口。
   */
  async function shelve(
    alarmEventId: string,
    durationMinutes: number,
    atUtc: string,
    idempotencyKey: string,
    reason?: string,
  ) {
    return shelveMutation.mutateAsync({
      path: { alarmEventId },
      body: {
        ...contextBody(),
        durationMinutes,
        shelvedAtUtc: atUtc,
        shelvedBy: actor.value,
        idempotencyKey,
        ...(reason && reason.trim() ? { reason: reason.trim() } : {}),
      },
    })
  }

  const alarms = computed<BusinessConsoleTelemetryAlarmEventItem[]>(() => {
    const items = listItems<BusinessConsoleTelemetryAlarmEventItem>(
      listQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined,
    )
    // 服务端已按生命周期排好；前端同口径再排一次兜底（稳定副本，不改原数组）。
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
      listTotal(listQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined),
    ),
    pending: listQuery.isLoading,
    error: listQuery.error,
    actionPending: computed(
      () => acknowledgeMutation.isLoading.value || shelveMutation.isLoading.value,
    ),
    acknowledge,
    shelve,
    refresh: () => (scopeReady.value ? listQuery.refetch() : Promise.resolve()),
  }
}
