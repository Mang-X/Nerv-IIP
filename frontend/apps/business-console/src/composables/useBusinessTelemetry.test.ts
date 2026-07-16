import { flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, ref, shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions,
  listBusinessConsoleTelemetryAlarmRulesQueryOptions,
  listBusinessConsoleTelemetryTagsQueryOptions,
  queryBusinessConsoleTelemetryDeviceHistoryQueryOptions,
  queryBusinessConsoleTelemetryOeeQueryOptions,
  queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  describeTelemetryOeeLimitations,
  describeTelemetryOeeDegradation,
  formatOeeQuantity,
  formatOeeRate,
  useBusinessTelemetryAlarmRules,
  useBusinessTelemetryHistory,
  useBusinessTelemetryOee,
  useBusinessTelemetryTags,
  useMaintenancePlanRuntimeRemaining,
  type RuntimeRemainingPlan,
} from './useBusinessTelemetry'

const coladaState = vi.hoisted(() => ({
  mutationCalls: [] as unknown[],
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  refetchById: new Map<string, ReturnType<typeof vi.fn>>(),
}))

// Controllable runtime-hours read used by useMaintenancePlanRuntimeRemaining. Tests swap `impl` to
// hand out deferred, abort-aware promises so overlapping rounds / out-of-order completion can be driven.
type RuntimeHoursOptions = { query: Record<string, unknown>; signal?: AbortSignal }
const rtState = vi.hoisted(() => ({
  concurrent: 0,
  maxConcurrent: 0,
  impl: (_opts: RuntimeHoursOptions): Promise<unknown> =>
    Promise.resolve({
      data: { success: true, data: { totalRuntimeHours: 0, hasRuntimeSamples: false } },
    }),
}))

vi.mock('@nerv-iip/api-client', () => ({
  queryBusinessConsoleTelemetryRuntimeHours: (opts: RuntimeHoursOptions) => rtState.impl(opts),
  queryBusinessConsoleTelemetryRuntimeHoursQueryOptions: vi.fn(() => ({
    key: [{ _id: 'queryBusinessConsoleTelemetryRuntimeHours' }],
    query: vi.fn(),
  })),
  createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions: vi.fn(() => ({
    key: [{ _id: 'createOrUpdateBusinessConsoleTelemetryAlarmRule' }],
    mutation: vi.fn(),
  })),
  listBusinessConsoleTelemetryAlarmRulesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleTelemetryAlarmRules' }],
    query: vi.fn(),
  })),
  listBusinessConsoleTelemetryTagsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleTelemetryTags' }],
    query: vi.fn(),
  })),
  queryBusinessConsoleTelemetryDeviceHistoryQueryOptions: vi.fn(() => ({
    key: [{ _id: 'queryBusinessConsoleTelemetryDeviceHistory' }],
    query: vi.fn(),
  })),
  queryBusinessConsoleTelemetryOeeQueryOptions: vi.fn(() => ({
    key: [{ _id: 'queryBusinessConsoleTelemetryOee' }],
    query: vi.fn(),
  })),
  queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'queryBusinessConsoleTelemetryRuntimeAvailability' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      coladaState.mutationCalls.push(vars)
      await options.onSuccess?.()
      return { success: true, data: { alarmRuleId: 'rule-created' } }
    }),
  })),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    const refetch = vi.fn()
    coladaState.queryOptionsById.set(id, options)
    coladaState.refetchById.set(id, refetch)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch,
    }
  }),
}))

describe('business telemetry composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.clearAllMocks()
    coladaState.mutationCalls = []
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
    coladaState.refetchById.clear()
    rtState.concurrent = 0
    rtState.maxConcurrent = 0
    rtState.impl = () =>
      Promise.resolve({
        data: { success: true, data: { totalRuntimeHours: 0, hasRuntimeSamples: false } },
      })
  })

  it('uses current business context and pagination for tag and alarm-rule lists', () => {
    const businessContext = useBusinessContextStore()
    businessContext.patchContext({
      organizationId: 'org-telemetry',
      environmentId: 'env-shopfloor',
    })

    useBusinessTelemetryTags({ deviceAssetId: ' DEV-SMT-01 ' })
    useBusinessTelemetryAlarmRules({ deviceAssetId: ' DEV-SMT-01 ', isEnabled: 'enabled' })

    expect(listBusinessConsoleTelemetryTagsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-telemetry',
        environmentId: 'env-shopfloor',
        deviceAssetId: 'DEV-SMT-01',
        skip: 0,
        take: 100,
      },
    })
    expect(listBusinessConsoleTelemetryAlarmRulesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-telemetry',
        environmentId: 'env-shopfloor',
        deviceAssetId: 'DEV-SMT-01',
        isEnabled: true,
        skip: 0,
        take: 100,
      },
    })
  })

  it('unwraps paged telemetry lists and exposes totals from successful envelopes', () => {
    coladaState.queryDataById.set('listBusinessConsoleTelemetryTags', {
      success: true,
      data: {
        items: [{ telemetryTagId: 'tag-1', deviceAssetId: 'DEV-CNC-01', tagKey: 'temperature' }],
        total: 12,
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleTelemetryAlarmRules', {
      success: true,
      data: {
        items: [{ alarmRuleId: 'rule-1', ruleCode: 'TEMP_HIGH', isEnabled: true }],
        total: 3,
      },
    })

    const tags = useBusinessTelemetryTags()
    const rules = useBusinessTelemetryAlarmRules()

    expect(tags.tags.value).toHaveLength(1)
    expect(tags.tagsTotal.value).toBe(12)
    expect(rules.alarmRules.value).toHaveLength(1)
    expect(rules.alarmRulesTotal.value).toBe(3)
  })

  it('creates or updates alarm rules with context and refetches the list', async () => {
    const rules = useBusinessTelemetryAlarmRules()

    await rules.saveAlarmRule({
      deviceAssetId: 'DEV-CNC-01',
      ruleCode: 'TEMP_HIGH',
      alarmCode: 'ALM-TEMP-HIGH',
      severity: 'critical',
      tagKey: 'temperature',
      comparisonOperator: '>',
      thresholdValue: 85,
      unitCode: 'CEL',
      isEnabled: true,
    })

    expect(createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutationCalls).toEqual([
      {
        body: {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          deviceAssetId: 'DEV-CNC-01',
          ruleCode: 'TEMP_HIGH',
          alarmCode: 'ALM-TEMP-HIGH',
          severity: 'critical',
          tagKey: 'temperature',
          comparisonOperator: '>',
          thresholdValue: 85,
          unitCode: 'CEL',
          isEnabled: true,
        },
      },
    ])
    expect(coladaState.refetchById.get('listBusinessConsoleTelemetryAlarmRules')).toHaveBeenCalled()
  })

  it('does not refetch alarm rules after mutation when business context is empty', async () => {
    useBusinessContextStore().patchContext({ organizationId: '', environmentId: '' })
    const rules = useBusinessTelemetryAlarmRules()

    await rules.saveAlarmRule({
      deviceAssetId: 'DEV-CNC-01',
      ruleCode: 'TEMP_HIGH',
      alarmCode: 'ALM-TEMP-HIGH',
      severity: 'critical',
      tagKey: 'temperature',
      comparisonOperator: '>',
      thresholdValue: 85,
      unitCode: 'CEL',
      isEnabled: true,
    })

    expect(
      coladaState.refetchById.get('listBusinessConsoleTelemetryAlarmRules'),
    ).not.toHaveBeenCalled()
  })

  it('keeps history and OEE queries disabled until a device scope is provided', () => {
    const history = useBusinessTelemetryHistory()
    const oee = useBusinessTelemetryOee()

    expect(queryBusinessConsoleTelemetryDeviceHistoryQueryOptions).toHaveBeenCalledWith({
      path: { deviceAssetId: '' },
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
      }),
    })
    expect(queryBusinessConsoleTelemetryOeeQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        deviceAssetId: '',
      }),
    })
    expect(
      coladaState.queryOptionsById.get('queryBusinessConsoleTelemetryDeviceHistory')?.enabled,
    ).toBe(false)
    expect(coladaState.queryOptionsById.get('queryBusinessConsoleTelemetryOee')?.enabled).toBe(
      false,
    )
    expect(history.historyItems.value).toEqual([])
    expect(oee.oee.value).toBeUndefined()
  })

  it('queries OEE and runtime availability with the same real device and time window', () => {
    coladaState.queryDataById.set('queryBusinessConsoleTelemetryOee', {
      success: true,
      data: {
        deviceAssetId: 'DEV-PACK-01',
        stateSampleCount: 10,
        availabilityRate: 0.82,
        performanceRate: 0.8,
        qualityRate: 0.9,
        isDegraded: false,
      },
    })
    coladaState.queryDataById.set('queryBusinessConsoleTelemetryRuntimeAvailability', {
      success: true,
      data: {
        items: [{ deviceAssetId: 'DEV-PACK-01', reasonCode: 'equipment.activeAlarm' }],
      },
    })

    const oee = useBusinessTelemetryOee({ deviceAssetId: 'DEV-PACK-01' })

    expect(queryBusinessConsoleTelemetryOeeQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        deviceAssetId: 'DEV-PACK-01',
        windowStartUtc: oee.filters.windowStartUtc,
        windowEndUtc: oee.filters.windowEndUtc,
      }),
    })
    expect(queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        deviceAssetIds: 'DEV-PACK-01',
        windowStartUtc: oee.filters.windowStartUtc,
        windowEndUtc: oee.filters.windowEndUtc,
      }),
    })
    expect(oee.oee.value?.availabilityRate).toBe(0.82)
    expect(oee.availabilityWindows.value).toHaveLength(1)
  })

  it('formats OEE measures and explains degraded inputs in business language', () => {
    expect(formatOeeRate(0.876)).toBe('87.6%')
    expect(formatOeeRate(undefined)).toBe('无数据')
    expect(formatOeeQuantity(12.5, 'PCS')).toBe('12.5 PCS')
    expect(describeTelemetryOeeLimitations()).toContain('OEE = 可用率 × 性能率 × 质量率')
    expect(describeTelemetryOeeDegradation('production-facts-missing')).toBe('缺少 MES 报工事实')
  })

  // Hand out deferred runtime-hours reads so overlapping rounds and out-of-order completion can be driven.
  function deferredRuntimeHours(options: { honorAbort: boolean }) {
    const created: Array<{
      resolveWith: (totalRuntimeHours: number, hasRuntimeSamples?: boolean) => void
    }> = []
    rtState.impl = (opts) =>
      new Promise((resolve, reject) => {
        let settled = false
        const settle = () => {
          if (settled) return false
          settled = true
          rtState.concurrent -= 1
          return true
        }
        rtState.concurrent += 1
        rtState.maxConcurrent = Math.max(rtState.maxConcurrent, rtState.concurrent)
        if (options.honorAbort) {
          opts.signal?.addEventListener('abort', () => {
            if (settle()) reject(new DOMException('aborted', 'AbortError'))
          })
        }
        created.push({
          resolveWith: (totalRuntimeHours, hasRuntimeSamples = true) => {
            if (settle()) {
              resolve({ data: { success: true, data: { totalRuntimeHours, hasRuntimeSamples } } })
            }
          },
        })
      })
    return created
  }

  const runtimePlan = (overrides: Partial<RuntimeRemainingPlan>): RuntimeRemainingPlan => ({
    planId: 'p1',
    deviceAssetId: 'D1',
    startsOn: '2026-06-01',
    runtimeHourInterval: 1000,
    nextDueRuntimeHours: 1000,
    ...overrides,
  })

  it('does not let a superseded round overwrite the latest remaining or clear its pending', async () => {
    // Ignore abort here to isolate the generation guard: the stale round stays in flight and resolves late.
    const created = deferredRuntimeHours({ honorAbort: false })
    const plans = ref<RuntimeRemainingPlan[]>([runtimePlan({ nextDueRuntimeHours: 1000 })])
    const { remainingByPlanId, remainingPending } = useMaintenancePlanRuntimeRemaining(plans)
    await nextTick()
    await flushPromises()
    expect(created).toHaveLength(1)
    expect(remainingPending.value).toBe(true)

    // Advance the threshold -> a newer round supersedes the first.
    plans.value = [runtimePlan({ nextDueRuntimeHours: 2000 })]
    await nextTick()
    await flushPromises()
    expect(created).toHaveLength(2)

    // The newer round completes first: remaining = 2000 - 500 = 1500.
    created[1].resolveWith(500)
    await flushPromises()
    expect(remainingByPlanId.value.p1).toEqual({ status: 'ok', hours: 1500 })
    expect(remainingPending.value).toBe(false)

    // The stale round completes late with a different value: it must NOT overwrite nor re-toggle pending.
    created[0].resolveWith(900)
    await flushPromises()
    expect(remainingByPlanId.value.p1).toEqual({ status: 'ok', hours: 1500 })
    expect(remainingPending.value).toBe(false)
  })

  it('bounds telemetry concurrency to the cap even when a refresh starts mid-flight', async () => {
    deferredRuntimeHours({ honorAbort: true })
    const makePlans = (nextDue: number) =>
      Array.from({ length: 10 }, (_, i) =>
        runtimePlan({ planId: `p${i}`, deviceAssetId: `D${i}`, nextDueRuntimeHours: nextDue }),
      )
    const plans = ref<RuntimeRemainingPlan[]>(makePlans(1000))
    useMaintenancePlanRuntimeRemaining(plans)
    await nextTick()
    await flushPromises()
    // 10 plans, cap 6 -> never more than 6 concurrent reads.
    expect(rtState.maxConcurrent).toBe(6)

    // Start a refresh while round 1 is still in flight: aborting the stale round keeps the peak at the cap.
    plans.value = makePlans(2000)
    await nextTick()
    await flushPromises()
    expect(rtState.maxConcurrent).toBeLessThanOrEqual(6)
  })
})
