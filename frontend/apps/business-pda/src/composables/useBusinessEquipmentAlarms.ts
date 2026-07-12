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
 * **未确认优先（跨分页）**：除全量列表读外，另发一支 `status=raised` 服务端过滤查询，
 * 把「全部未确认报警」（≤take）并入列表头部——即便未确认报警落在全量读的第 2 页之后，
 * 也保证被展示且排在最前，不依赖客户端只对首页排序。
 *
 * **幂等（断网重试不重复）**：`acknowledge`/`shelve` 接受调用方铸造的**稳定时间戳** `atUtc`，
 * 同一次用户操作跨重试复用同一 `atUtc`。领域侧 `AlarmEvent.Acknowledge` 对已确认直接返回
 * （first-write-wins，天然幂等）；`Shelve` 的搁置窗口 = `[atUtc, atUtc+duration]`
 * （后端 `ShelveAlarmCommandHandler` 按请求 `ShelvedAtUtc` 派生 `ShelvedUntilUtc`），
 * 复用同一 `atUtc` 使窗口固定、**重试不会延长**。页面对「已发出但结果未知」的失败不盲目重试
 * （交给 verify），只对确定性失败复用同键重试。
 */
export function useBusinessEquipmentAlarms(initialFilters: Partial<EquipmentAlarmFilters> = {}) {
  const { organizationId, environmentId, actor, scopeReady } = authScope()
  const queryCache = useQueryCache()
  const filters = reactive<EquipmentAlarmFilters>({
    skip: 0,
    take: DEFAULT_TAKE,
    ...initialFilters,
  })

  // 全量读（各状态，供已处理行的灰显上下文/历史）。
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

  // status=raised 过滤读：保证全部未确认报警进入列表头部（跨分页未确认优先）。
  const raisedQuery = useQuery(() => ({
    ...listBusinessConsoleEquipmentAlarmsQueryOptions({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        status: RAISED_STATUS,
        skip: 0,
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

  /** 搁置。`atUtc` 跨重试复用 → 窗口 `[atUtc, atUtc+duration]` 固定，重试不延长。 */
  async function shelve(
    alarmEventId: string,
    durationMinutes: number,
    atUtc: string,
    reason?: string,
  ) {
    return shelveMutation.mutateAsync({
      path: { alarmEventId },
      body: {
        ...contextBody(),
        durationMinutes,
        shelvedAtUtc: atUtc,
        shelvedBy: actor.value,
        ...(reason && reason.trim() ? { reason: reason.trim() } : {}),
      },
    })
  }

  const alarms = computed<BusinessConsoleTelemetryAlarmEventItem[]>(() => {
    const raised = listItems<BusinessConsoleTelemetryAlarmEventItem>(
      raisedQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined,
    )
    const all = listItems<BusinessConsoleTelemetryAlarmEventItem>(
      listQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined,
    )
    // 并入全部未确认（raised 支）+ 全量读中的其余状态，按 alarmEventId 去重（raised 支优先）。
    const seen = new Set(raised.map((a) => a.alarmEventId).filter(Boolean))
    const merged = [...raised, ...all.filter((a) => !a.alarmEventId || !seen.has(a.alarmEventId))]
    // 未确认 > 已搁置 > 已确认 > 已清除；同档按发生时间倒序（新报警在前）。稳定副本，不改原数组。
    return merged.sort((a, b) => {
      const weightDiff = alarmLifecycleSortWeight(a.status) - alarmLifecycleSortWeight(b.status)
      if (weightDiff !== 0) return weightDiff
      return (b.raisedAtUtc ?? '').localeCompare(a.raisedAtUtc ?? '')
    })
  })

  return {
    filters,
    alarms,
    // 全量未确认数（服务端过滤 total，跨分页准确）。
    unacknowledgedCount: computed(() =>
      listTotal(raisedQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined),
    ),
    total: computed(() =>
      listTotal(listQuery.data.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined),
    ),
    pending: computed(() => listQuery.isLoading.value || raisedQuery.isLoading.value),
    error: computed(() => listQuery.error.value ?? raisedQuery.error.value),
    actionPending: computed(
      () => acknowledgeMutation.isLoading.value || shelveMutation.isLoading.value,
    ),
    acknowledge,
    shelve,
    refresh: () =>
      scopeReady.value
        ? Promise.all([listQuery.refetch(), raisedQuery.refetch()])
        : Promise.resolve(),
  }
}
