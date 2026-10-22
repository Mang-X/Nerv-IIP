import { shallowMount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { reactive, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import ReportsPage from './reports.vue'

const state = vi.hoisted(() => ({
  oeeShouldLoad: undefined as undefined | (() => boolean),
  wipShouldLoad: undefined as undefined | (() => boolean),
  filters: undefined as unknown as Record<string, string | number>,
}))

vi.mock('@/composables/useMesProductionStatistics', async () => {
  const { computed, reactive, shallowRef } = await import('vue')
  state.filters = reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    dimension: 'day',
    windowStartUtc: '2026-08-24T00:00:00.000Z',
    windowEndUtc: '2026-08-31T00:00:00.000Z',
    businessDate: '',
    shiftCode: '',
    workCenterId: '',
    skuId: '',
    skip: 0,
    take: 20,
  })
  return {
    useMesProductionStatistics: () => ({
      filters: state.filters,
      items: shallowRef([]),
      total: shallowRef(0),
      error: shallowRef(),
      pending: shallowRef(false),
      state: computed(() => 'ready'),
      refresh: vi.fn(),
      loadAll: vi.fn(async () => []),
    }),
  }
})

vi.mock('@/composables/useBusinessTelemetry', () => ({
  useBusinessTelemetryOeeAggregates: (_filters: unknown, shouldLoad: () => boolean) => {
    state.oeeShouldLoad = shouldLoad
    return {
      aggregateBuckets: shallowRef([]),
      aggregateError: shallowRef(),
      aggregatePending: shallowRef(false),
      filters: reactive({}),
      refreshAggregates: vi.fn(),
    }
  },
}))

vi.mock('@/composables/useBusinessMes', () => ({
  useMesWipSummary: (shouldLoad: () => boolean) => {
    state.wipShouldLoad = shouldLoad
    return {
      filters: reactive({ workCenterId: '', skip: 0, take: 5 }),
      refreshWip: vi.fn(),
      wipError: shallowRef(),
      wipPending: shallowRef(false),
      wipRows: shallowRef([]),
      wipState: shallowRef('ready'),
      wipTotal: shallowRef(0),
    }
  },
}))

function mountPage(permissionCodes: string[]) {
  const pinia = createPinia()
  useAuthStore(pinia).$patch({
    principal: {
      principalId: 'principal-shift-lead',
      principalType: 'User',
      loginName: 'shift.lead',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes,
    },
  })
  return shallowMount(ReportsPage, { global: { plugins: [pinia] } })
}

describe('MES production report page request boundaries', () => {
  beforeEach(() => {
    state.oeeShouldLoad = undefined
    state.wipShouldLoad = undefined
  })

  it('does not issue WIP or OEE reads for a reporting-only principal', () => {
    mountPage(['business.mes.reporting.read'])

    expect(state.wipShouldLoad?.()).toBe(false)
    expect(state.oeeShouldLoad?.()).toBe(false)
  })

  it('issues authorized context reads except OEE for the SKU dimension', () => {
    mountPage([
      'business.mes.reporting.read',
      'business.mes.operations.read',
      'business.iiot.telemetry.read',
    ])

    expect(state.wipShouldLoad?.()).toBe(true)
    expect(state.oeeShouldLoad?.()).toBe(true)

    state.filters.dimension = 'sku'
    expect(state.oeeShouldLoad?.()).toBe(false)
  })
})
