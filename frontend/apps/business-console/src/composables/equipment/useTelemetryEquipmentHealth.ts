import {
  getBusinessConsoleEquipmentDeviceHealthQueryOptions,
  queryBusinessConsoleTelemetryRuntimeHours,
  queryBusinessConsoleTelemetryRuntimeHoursQueryOptions,
  type BusinessConsoleEquipmentHealthEnvelope,
  type BusinessConsoleEquipmentHealthResponse,
  type BusinessConsoleTelemetryRuntimeHoursEnvelope,
  type BusinessConsoleTelemetryRuntimeHoursResponse,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, onScopeDispose, ref, watch, type Ref } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from '../shared/businessContextBinding'
import { EQUIPMENT_HEALTH_POLL_INTERVAL_MS, toContextQuery, unwrapData } from './telemetryShared'

/**
 * 设备健康评分读面。只有业务上下文与设备范围同时有效时才启用，并由 Pinia Colada
 * 官方 auto-refetch 插件每 5 秒刷新；刷新期间保留 query cache 中的上一份健康事实。
 */
export function useBusinessEquipmentHealth(deviceAssetId: Ref<string>) {
  const businessContext = useBusinessContextStore()
  const normalizedDeviceAssetId = computed(() => deviceAssetId.value.trim())
  const enabled = computed(
    () => hasBusinessContext(businessContext) && normalizedDeviceAssetId.value.length > 0,
  )
  const healthQuery = useQuery(() => ({
    ...getBusinessConsoleEquipmentDeviceHealthQueryOptions({
      path: { deviceAssetId: normalizedDeviceAssetId.value },
      query: toContextQuery(businessContext),
    }),
    enabled: enabled.value,
    autoRefetch: () => EQUIPMENT_HEALTH_POLL_INTERVAL_MS,
  }))

  return {
    health: computed<BusinessConsoleEquipmentHealthResponse | undefined>(() =>
      unwrapData<BusinessConsoleEquipmentHealthResponse>(
        healthQuery.data.value as BusinessConsoleEquipmentHealthEnvelope | undefined,
      ),
    ),
    healthError: healthQuery.error,
    healthPending: healthQuery.isLoading,
    refreshHealth: () => (enabled.value ? healthQuery.refetch() : Promise.resolve()),
  }
}

/**
 * 设备累计运行小时（IIoT #688/#884 facade）。返回值是 [windowStartUtc, windowEndUtc] 窗口内累计，不是
 * 终身表底，调用方须按窗口口径展示。窗口起点对齐运行小时型保养计划的起算日时，`totalRuntimeHours`
 * 即等于计划推算所用的累计口径，可与计划 `nextDueRuntimeHours` 相减得到「距下次保养还需 X 小时」。
 * org/env + 设备 + 窗口齐备才发请求，缺任一即静默空态。
 */
export function useBusinessTelemetryRuntimeHours(
  deviceAssetId: Ref<string>,
  windowStartUtc: Ref<string>,
  windowEndUtc: Ref<string>,
) {
  const businessContext = useBusinessContextStore()
  const enabled = computed(
    () =>
      hasBusinessContext(businessContext) &&
      deviceAssetId.value.trim().length > 0 &&
      windowStartUtc.value.trim().length > 0 &&
      windowEndUtc.value.trim().length > 0,
  )
  const runtimeHoursQuery = useQuery(() => ({
    ...queryBusinessConsoleTelemetryRuntimeHoursQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: deviceAssetId.value.trim(),
        windowStartUtc: windowStartUtc.value,
        windowEndUtc: windowEndUtc.value,
      },
    }),
    enabled: enabled.value,
  }))
  const runtimeHours = computed<BusinessConsoleTelemetryRuntimeHoursResponse | undefined>(() =>
    unwrapData<BusinessConsoleTelemetryRuntimeHoursResponse>(
      runtimeHoursQuery.data.value as BusinessConsoleTelemetryRuntimeHoursEnvelope | undefined,
    ),
  )

  return {
    runtimeHours,
    totalRuntimeHours: computed(() => runtimeHours.value?.totalRuntimeHours ?? null),
    hasRuntimeSamples: computed(() => runtimeHours.value?.hasRuntimeSamples ?? false),
    runtimeHoursError: runtimeHoursQuery.error,
    runtimeHoursPending: runtimeHoursQuery.isLoading,
    runtimeHoursEnabled: enabled,
    refreshRuntimeHours: () => (enabled.value ? runtimeHoursQuery.refetch() : Promise.resolve()),
  }
}

/** Minimal shape needed to derive remaining runtime hours from a maintenance plan list row. */
export interface RuntimeRemainingPlan {
  planId?: string
  deviceAssetId?: string
  startsOn?: string
  runtimeHourInterval?: number | null
  nextDueRuntimeHours?: number | null
}

/**
 * Per-plan remaining-runtime-hours outcome. `loading` (a read is in flight for this plan, including a
 * refresh that supersedes a prior settled value), `error` (telemetry read failed) and `no-samples`
 * (facade responded, but the device has no real runtime samples yet) are all kept distinct so the UI
 * never shows a stale value during a refresh nor mislabels a transient failure as "no samples".
 */
export type RuntimeRemainingEntry =
  | { status: 'ok'; hours: number }
  | { status: 'no-samples' }
  | { status: 'error' }
  | { status: 'loading' }
  // A runtime-hour plan whose runtime cursor / device / window is missing can never yield a remaining; it
  // settles here (terminal, never loading) so such a plan is never left forever on the loading state.
  | { status: 'invalid' }

// Bound the client-side telemetry fan-out: a full page of runtime plans issues at most this many
// concurrent runtime-hours reads, so the derivation never turns into a 100-way burst against the Gateway.
const RUNTIME_REMAINING_CONCURRENCY = 6

async function mapWithConcurrency<T, R>(
  items: readonly T[],
  limit: number,
  fn: (item: T) => Promise<R>,
  shouldStop?: () => boolean,
): Promise<R[]> {
  const results = new Array<R>(items.length)
  let cursor = 0
  async function worker() {
    // Stop pulling new work once superseded, so overlapping rounds never keep `limit` workers each
    // running — the stale round drains instead of adding to the global concurrency.
    while (cursor < items.length && !shouldStop?.()) {
      const index = cursor++
      results[index] = await fn(items[index])
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, () => worker()))
  return results
}

/**
 * Derives per-plan remaining runtime hours on the client from the runtime-hours read facade — one call
 * per runtime-hour plan over that plan's own [startsOn, now] window, so each plan uses its own accrual
 * basis (windows are never shared). The maintenance plan list query stays a fast DB projection; this
 * keeps the telemetry fan-out on the client where it is bounded (see RUNTIME_REMAINING_CONCURRENCY) and
 * non-blocking. remaining = nextDueRuntimeHours − accumulated runtime. The result recomputes whenever a
 * plan's next runtime threshold advances (e.g. after generating due work orders), not only when the set
 * of plan ids changes, so a stale remaining is never shown after the threshold moves.
 *
 * This is an imperative fan-out (a variable N of per-plan, per-window reads under one bounded-concurrency
 * pool + generation/AbortController lifecycle) that the declarative `useQuery` model cannot express, so it
 * calls the generated read function directly — the same on-demand raw-sdk pattern already used by other
 * composables (e.g. useBusinessMes material-issue/finished-goods reads, useCodeRules preview). Declarative
 * list/detail reads elsewhere still go through Pinia Colada per
 * docs/architecture/frontend/workspace-structure.md ("路由、页面与状态边界").
 */
export function useMaintenancePlanRuntimeRemaining(plans: Ref<RuntimeRemainingPlan[]>) {
  const businessContext = useBusinessContextStore()
  const remainingByPlanId = ref<Record<string, RuntimeRemainingEntry>>({})
  const pending = ref(false)
  // Guards against overlapping rounds (watch fires, manual refresh, paging / org-env switch): only the
  // latest generation may commit results or clear pending, and the previous round's in-flight reads are
  // aborted so overlapping rounds never exceed the concurrency cap.
  let generation = 0
  let inFlight: AbortController | undefined

  async function recompute() {
    const myGeneration = ++generation
    inFlight?.abort()
    const controller = new AbortController()
    inFlight = controller
    const isCurrent = () => myGeneration === generation

    if (!hasBusinessContext(businessContext)) {
      // A new empty-context round supersedes any in-flight round: it must clear both the results and the
      // pending flag itself (the superseded round can no longer clear pending), else the UI hangs on loading.
      if (isCurrent()) {
        remainingByPlanId.value = {}
        pending.value = false
      }
      return
    }
    // Every runtime-hour plan (has runtimeHourInterval) gets an entry. Plans that can actually be read
    // (device + window + runtime cursor present) are fetched; any missing those settle as 'invalid' up-front
    // so a runtime plan with an incomplete/inconsistent cursor is never left permanently on 'loading'.
    const runtimeCandidates = plans.value.filter((p) => !!p.planId && p.runtimeHourInterval != null)
    if (runtimeCandidates.length === 0) {
      if (isCurrent()) {
        remainingByPlanId.value = {}
        pending.value = false
      }
      return
    }
    const isFetchable = (p: RuntimeRemainingPlan) =>
      !!p.deviceAssetId && !!p.startsOn && p.nextDueRuntimeHours != null
    const fetchable = runtimeCandidates.filter(isFetchable)

    // Loading up-front for fetchable plans (so a refresh never shows a prior settled value while a fresh read
    // is in flight); terminal 'invalid' for the rest.
    if (isCurrent()) {
      remainingByPlanId.value = Object.fromEntries(
        runtimeCandidates.map(
          (p) =>
            [p.planId!, isFetchable(p) ? { status: 'loading' } : { status: 'invalid' }] as const,
        ),
      )
    }
    if (fetchable.length === 0) {
      if (isCurrent()) pending.value = false
      return
    }

    pending.value = true
    const nowUtc = new Date().toISOString()
    const organizationId = businessContext.organizationId
    const environmentId = businessContext.environmentId
    try {
      const entries = await mapWithConcurrency(
        fetchable,
        RUNTIME_REMAINING_CONCURRENCY,
        async (p): Promise<readonly [string, RuntimeRemainingEntry]> => {
          try {
            const result = await queryBusinessConsoleTelemetryRuntimeHours({
              query: {
                organizationId,
                environmentId,
                deviceAssetId: p.deviceAssetId!,
                windowStartUtc: `${p.startsOn}T00:00:00.000Z`,
                windowEndUtc: nowUtc,
              },
              signal: controller.signal,
            })
            if (result.error) return [p.planId!, { status: 'error' }] as const
            const data = result.data?.data
            if (!data?.hasRuntimeSamples) return [p.planId!, { status: 'no-samples' }] as const
            const remaining = p.nextDueRuntimeHours! - (data.totalRuntimeHours ?? 0)
            return [p.planId!, { status: 'ok', hours: remaining < 0 ? 0 : remaining }] as const
          } catch {
            return [p.planId!, { status: 'error' }] as const
          }
        },
        () => !isCurrent(),
      )
      // A superseded round must never overwrite the newer round's results or clear its pending flag. Overlay
      // the fetched results onto the up-front map so the terminal 'invalid' entries are preserved.
      if (isCurrent()) {
        remainingByPlanId.value = { ...remainingByPlanId.value, ...Object.fromEntries(entries) }
      }
    } finally {
      if (isCurrent()) pending.value = false
    }
  }

  watch(
    [
      // Include the fields that move the remaining value (next runtime threshold + accrual window start),
      // so advancing a plan's threshold via generate-due triggers a fresh read instead of a stale display.
      () => plans.value.map((p) => `${p.planId}:${p.nextDueRuntimeHours}:${p.startsOn}`).join(','),
      () => businessContext.organizationId,
      () => businessContext.environmentId,
    ],
    () => void recompute(),
    { immediate: true },
  )

  // Complete the AbortController lifecycle: when the owning scope is disposed (page unmount), supersede any
  // in-flight round (generation bump → workers stop pulling and never commit map/pending to a dead scope)
  // and abort in-flight reads so they don't keep hitting the Gateway → IIoT after the page is gone.
  onScopeDispose(() => {
    generation += 1
    inFlight?.abort()
    inFlight = undefined
  })

  return { remainingByPlanId, remainingPending: pending, refreshRemaining: recompute }
}
