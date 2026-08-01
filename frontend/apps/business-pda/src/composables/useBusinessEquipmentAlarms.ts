import {
  acknowledgeBusinessConsoleEquipmentAlarmMutationOptions,
  listBusinessConsoleEquipmentAlarms,
  confirmBusinessConsoleOperation,
  listBusinessConsoleEquipmentAlarmsQueryOptions,
  shelveBusinessConsoleEquipmentAlarmMutationOptions,
  type BusinessConsoleEquipmentAlarmListEnvelope,
  type BusinessConsoleTelemetryAlarmEventItem,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  alarmLifecycleSortWeight,
  clearPendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from '@/composables/useListFreshness'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'
import { assertLifecycleActionExecutable } from '@/composables/lifecycleActionRecovery'
import { useTaskListPagination, type TaskListPage } from '@/composables/useTaskListPagination'

const DEFAULT_TAKE = 20

/** 搁置时长档位（分钟）——交互稿固定三档：30 分钟 / 2 小时 / 8 小时。 */
export const ALARM_SHELVE_DURATIONS_MINUTES = [30, 120, 480] as const
export type AlarmShelveDurationMinutes = (typeof ALARM_SHELVE_DURATIONS_MINUTES)[number]

/** 「未确认」状态码——工作台角标与列表待办口径统一走它（既未确认、未搁置、未清除）。 */
const RAISED_STATUS = 'raised'

export interface EquipmentAlarmFilters {
  skip: number
  take: number
  deviceAssetId?: string
  status?: string
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
  const principalId = computed(
    () => auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
  )
  const scopeReady = computed(() => Boolean(organizationId.value && environmentId.value))
  const scopeKey = computed(() => `${organizationId.value.trim()}:${environmentId.value.trim()}`)
  return { organizationId, environmentId, actor, principalId, scopeReady, scopeKey }
}

/**
 * 工作台报警角标数据源（未确认数）。**服务端 `status=raised` 过滤查询，取 `total`**——
 * 全量口径，不受列表首页 take 上限影响（>100 时也不会把角标算成 0）。`take:1` 只为省流量，
 * `total` 仍是符合条件的全部条数。
 */
export function useUnacknowledgedAlarmCount(enabled: MaybeRefOrGetter<boolean> = true) {
  const { organizationId, environmentId, scopeReady, scopeKey } = authScope()
  const queryEnabled = computed(() => scopeReady.value && toValue(enabled))
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
    // 调用方可再按权限门（如首页仅报警读权限主体才查询）。
    enabled: queryEnabled.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => raisedQuery.data.value,
    scopeKey,
    queryEnabled,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    queryEnabled,
    raisedQuery.isLoading,
  )

  return {
    unacknowledgedCount: computed(() =>
      listTotal(currentResponse.value as BusinessConsoleEquipmentAlarmListEnvelope | undefined),
    ),
    pending: raisedQuery.isLoading,
    hasSuccessfulResponse,
    hasFailedResponse,
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
 * **幂等（断网/延迟重投不重复）**：`acknowledge` 与 `shelve` 都携带意图级**持久幂等键**
 * `idempotencyKey`。结果未知时保留同一键和时间戳，后端以报警生命周期资源锁与持久回执收敛
 * 重复投递；同键更换报警、动作或载荷会 fail closed。页面对「已发出但结果未知」的失败先回读，
 * 不创建新意图盲重放。
 */
export function useBusinessEquipmentAlarms(initialFilters: Partial<EquipmentAlarmFilters> = {}) {
  const { organizationId, environmentId, actor, principalId, scopeReady, scopeKey } = authScope()
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
        ...optionalQuery('status', filters.status),
      },
    }),
    enabled: scopeReady.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => listQuery.data.value,
    scopeKey,
    scopeReady,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scopeReady)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    listQuery.isLoading,
  )

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

  async function readExactAlarm(alarmEventId: string) {
    const { data } = await listBusinessConsoleEquipmentAlarms({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        alarmEventId,
        skip: 0,
        take: 2,
      },
      throwOnError: true,
    })
    const matches = listItems<BusinessConsoleTelemetryAlarmEventItem>(
      data as BusinessConsoleEquipmentAlarmListEnvelope | undefined,
    ).filter((alarm) => alarm.alarmEventId === alarmEventId)
    return matches.length === 1 ? matches[0] : undefined
  }

  /** 确认。意图键与 `atUtc` 都跨结果未知的重试复用，直到回执确认后才清除。 */
  async function acknowledge(alarmEventId: string, atUtc: string) {
    const intentScope = {
      principalId: principalId.value,
      organizationId: organizationId.value,
      environmentId: environmentId.value,
      operationType: 'iiot.alarm.acknowledge',
      payloadFingerprint: JSON.stringify({ alarmEventId }),
    }
    const isReplay = Boolean(peekPendingBusinessIntent(intentScope))
    const pending = acquirePendingBusinessIntent(
      intentScope,
      () => globalThis.crypto?.randomUUID?.() ?? `alarm-acknowledge-${Date.now()}-${Math.random()}`,
      { acknowledgedAtUtc: atUtc },
    )
    try {
      const authoritative = await readExactAlarm(alarmEventId)
      assertLifecycleActionExecutable({
        domain: 'iiot-alarm',
        action: 'acknowledge',
        facts: {
          status: authoritative?.status,
          acknowledgedAtUtc: authoritative?.acknowledgedAtUtc,
          idempotentReplay: isReplay,
        },
      })
    } catch (error) {
      if (!isReplay) clearPendingBusinessIntent(intentScope)
      throw error
    }
    const acknowledgedAtUtcSnapshot = (
      pending.payloadSnapshot as { acknowledgedAtUtc?: unknown } | undefined
    )?.acknowledgedAtUtc
    const acknowledgedAtUtc =
      typeof acknowledgedAtUtcSnapshot === 'string' && acknowledgedAtUtcSnapshot.trim()
        ? acknowledgedAtUtcSnapshot
        : atUtc
    return completePendingBusinessIntent(intentScope, async () =>
      confirmBusinessConsoleOperation(
        await acknowledgeMutation.mutateAsync({
          path: { alarmEventId },
          body: {
            ...contextBody(),
            acknowledgedAtUtc,
            acknowledgedBy: actor.value,
            idempotencyKey: pending.idempotencyKey,
          },
        }),
        {
          expectedOperationType: 'iiot.alarm.acknowledge',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: alarmEventId,
        },
      ),
    )
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
    const intentScope = {
      principalId: principalId.value,
      organizationId: organizationId.value,
      environmentId: environmentId.value,
      operationType: 'iiot.alarm.shelve',
      payloadFingerprint: JSON.stringify({
        alarmEventId,
        durationMinutes,
        reason: reason?.trim() ?? '',
      }),
    }
    const isReplay = Boolean(peekPendingBusinessIntent(intentScope))
    const pending = acquirePendingBusinessIntent(intentScope, () => idempotencyKey, {
      shelvedAtUtc: atUtc,
    })
    try {
      const authoritative = await readExactAlarm(alarmEventId)
      assertLifecycleActionExecutable({
        domain: 'iiot-alarm',
        action: 'shelve',
        facts: {
          status: authoritative?.status,
          acknowledgedAtUtc: authoritative?.acknowledgedAtUtc,
          shelvedAtUtc: authoritative?.shelvedAtUtc,
          shelvedUntilUtc: authoritative?.shelvedUntilUtc,
          evaluatedAtUtc: atUtc,
          idempotentReplay: isReplay,
        },
      })
    } catch (error) {
      if (!isReplay) clearPendingBusinessIntent(intentScope)
      throw error
    }
    const shelvedAtUtcSnapshot = (pending.payloadSnapshot as { shelvedAtUtc?: unknown } | undefined)
      ?.shelvedAtUtc
    const shelvedAtUtc =
      typeof shelvedAtUtcSnapshot === 'string' && shelvedAtUtcSnapshot.trim()
        ? shelvedAtUtcSnapshot
        : atUtc
    return completePendingBusinessIntent(intentScope, async () =>
      confirmBusinessConsoleOperation(
        await shelveMutation.mutateAsync({
          path: { alarmEventId },
          body: {
            ...contextBody(),
            durationMinutes,
            shelvedAtUtc,
            shelvedBy: actor.value,
            idempotencyKey: pending.idempotencyKey,
            ...(reason && reason.trim() ? { reason: reason.trim() } : {}),
          },
        }),
        {
          expectedOperationType: 'iiot.alarm.shelve',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: alarmEventId,
        },
      ),
    )
  }

  const firstPage = computed<TaskListPage<BusinessConsoleTelemetryAlarmEventItem> | undefined>(
    () => {
      const envelope = currentResponse.value as
        | BusinessConsoleEquipmentAlarmListEnvelope
        | undefined
      if (envelope?.success !== true) return undefined
      return {
        items: envelope.data?.items ?? [],
        total: envelope.data?.total ?? 0,
      }
    },
  )
  const listIdentity = computed(
    () => `${scopeKey.value}:${filters.deviceAssetId ?? ''}:${filters.status ?? ''}`,
  )
  const pager = useTaskListPagination({
    identity: listIdentity,
    firstPage,
    pageSize: DEFAULT_TAKE,
    itemKey: (item) => item.alarmEventId ?? '',
    fetchPage: async ({ skip, take }) => {
      const { data } = await listBusinessConsoleEquipmentAlarms({
        query: {
          organizationId: organizationId.value,
          environmentId: environmentId.value,
          skip,
          take,
          ...optionalQuery('deviceAssetId', filters.deviceAssetId),
          ...optionalQuery('status', filters.status),
        },
        throwOnError: true,
      })
      const envelope = data as BusinessConsoleEquipmentAlarmListEnvelope | undefined
      if (envelope?.success !== true) throw new Error('报警下一页加载失败，请重试。')
      return { items: envelope.data?.items ?? [], total: envelope.data?.total ?? 0 }
    },
    refreshFirstPage: listQuery.refetch,
  })

  const alarms = computed<BusinessConsoleTelemetryAlarmEventItem[]>(() => {
    // 服务端已按生命周期排好；前端同口径再排一次兜底（稳定副本，不改原数组）。
    return [...pager.items.value].sort((a, b) => {
      const weightDiff = alarmLifecycleSortWeight(a.status) - alarmLifecycleSortWeight(b.status)
      if (weightDiff !== 0) return weightDiff
      return (b.raisedAtUtc ?? '').localeCompare(a.raisedAtUtc ?? '')
    })
  })

  return {
    filters,
    alarms,
    total: pager.total,
    loaded: pager.loaded,
    hasMore: pager.hasMore,
    loadingMore: pager.loadingMore,
    loadMoreError: pager.loadMoreError,
    organizationId,
    environmentId,
    scopeReady,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    pending: listQuery.isLoading,
    error: listQuery.error,
    actionPending: computed(
      () => acknowledgeMutation.isLoading.value || shelveMutation.isLoading.value,
    ),
    acknowledge,
    shelve,
    loadMore: pager.loadMore,
    refresh: () => (scopeReady.value ? pager.refresh() : Promise.resolve()),
  }
}
