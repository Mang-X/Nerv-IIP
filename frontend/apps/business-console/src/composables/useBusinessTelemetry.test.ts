import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
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
  formatOeeRate,
  useBusinessTelemetryAlarmRules,
  useBusinessTelemetryHistory,
  useBusinessTelemetryOee,
  useBusinessTelemetryTags,
} from './useBusinessTelemetry'

const coladaState = vi.hoisted(() => ({
  mutationCalls: [] as unknown[],
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  refetchById: new Map<string, ReturnType<typeof vi.fn>>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
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
  })

  it('uses current business context and pagination for tag and alarm-rule lists', () => {
    const businessContext = useBusinessContextStore()
    businessContext.patchContext({ organizationId: 'org-telemetry', environmentId: 'env-shopfloor' })

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

    expect(coladaState.refetchById.get('listBusinessConsoleTelemetryAlarmRules')).not.toHaveBeenCalled()
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
    expect(coladaState.queryOptionsById.get('queryBusinessConsoleTelemetryDeviceHistory')?.enabled).toBe(false)
    expect(coladaState.queryOptionsById.get('queryBusinessConsoleTelemetryOee')?.enabled).toBe(false)
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
        performanceRateEstimated: true,
        qualityRateEstimated: true,
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

  it('formats OEE rates and states the P0 limitation in business language', () => {
    expect(formatOeeRate(0.876)).toBe('87.6%')
    expect(formatOeeRate(undefined)).toBe('无数据')
    expect(describeTelemetryOeeLimitations()).toContain('当前 OEE 只按设备运行状态计算可用率')
    expect(describeTelemetryOeeLimitations()).toContain('性能与质量不作为真实测量值')
  })
})
