import { mount } from '@vue/test-utils'
import { NvScanBar } from '@nerv-iip/ui-mobile'
import { defineComponent, h, nextTick, reactive, shallowRef } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  listBusinessConsoleWmsCountOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions,
} from '@nerv-iip/api-client'
import WmsOperationalCandidatePicker from '@/components/wms/WmsOperationalCandidatePicker.vue'
import { useWmsOperationalCandidates } from './useWmsOperationalCandidates'

const queryState = vi.hoisted(() => ({
  response: undefined as unknown,
  options: undefined as undefined | { enabled?: boolean },
  factory: undefined as undefined | (() => Record<string, unknown>),
  data: undefined as undefined | { value: unknown },
}))

afterEach(() => {
  vi.useRealTimers()
})

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions: vi.fn((options) => ({
    key: ['receipt-candidates', options],
  })),
  listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions: vi.fn((options) => ({
    key: ['shipment-candidates', options],
  })),
  listBusinessConsoleWmsCountOperationalCandidatesQueryOptions: vi.fn((options) => ({
    key: ['count-candidates', options],
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((factory) => {
    queryState.factory = factory
    queryState.options = factory()
    const data = shallowRef(queryState.response)
    queryState.data = data
    return {
      data,
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))

describe('PDA WMS operational candidates', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.clearAllMocks()
    queryState.response = {
      success: true,
      data: {
        sourceKind: 'warehouse-operational-records',
        scopeKind: 'self',
        scopeId: 'worker-1',
        asOfUtc: '2026-07-30T01:00:00Z',
        freshnessUtc: '2026-07-30T00:59:00Z',
        truncated: false,
        locations: [{ locationCode: 'A-01', skuCodes: ['SKU-1'] }],
        lots: [{ lotNo: 'LOT-A', skuCode: 'SKU-1', locationCodes: ['A-01'] }],
      },
    }
  })

  it.each([
    ['receipt', listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions],
    ['shipment', listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions],
    ['count', listBusinessConsoleWmsCountOperationalCandidatesQueryOptions],
  ] as const)('maps %s to trusted selected scope', (kind, factory) => {
    const filters = reactive({ status: 'Open', locationCode: 'A-01', skuCode: 'SKU-1' })

    const result = useWmsOperationalCandidates(kind, {
      organizationId: shallowRef('org-1'),
      environmentId: shallowRef('env-1'),
      scopeKind: shallowRef('self'),
      scopeId: shallowRef('worker-1'),
      scopeReady: shallowRef(true),
      filters,
    })

    expect(factory).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-1',
        environmentId: 'env-1',
        scopeKind: 'self',
        scopeId: 'worker-1',
        skuCode: 'SKU-1',
        locationCode: 'A-01',
        take: 100,
      },
    })
    expect(queryState.options?.enabled).toBe(true)
    expect(result.locationOptions.value).toHaveLength(1)
    expect(result.lotOptions.value).toHaveLength(kind === 'count' ? 0 : 1)
  })

  it('does not request candidates until the authorized scope is ready', () => {
    useWmsOperationalCandidates('receipt', {
      organizationId: shallowRef('org-1'),
      environmentId: shallowRef('env-1'),
      scopeKind: shallowRef('self'),
      scopeId: shallowRef('worker-1'),
      scopeReady: shallowRef(false),
      filters: reactive({ status: 'Open' }),
    })

    expect(queryState.options?.enabled).toBe(false)
  })

  it('clears picker values when scope or status changes', async () => {
    const scopeKind = shallowRef<string | undefined>('self')
    const scopeId = shallowRef<string | undefined>('worker-1')
    const filters = reactive({
      status: 'Open',
      locationCode: 'A-01' as string | undefined,
      lotNo: 'LOT-A' as string | undefined,
    })
    useWmsOperationalCandidates('shipment', {
      organizationId: shallowRef('org-1'),
      environmentId: shallowRef('env-1'),
      scopeKind,
      scopeId,
      scopeReady: shallowRef(true),
      filters,
    })

    filters.status = 'Completed'
    await nextTick()
    expect(filters.locationCode).toBeUndefined()
    expect(filters.lotNo).toBeUndefined()

    filters.locationCode = 'A-02'
    filters.lotNo = 'LOT-B'
    scopeKind.value = 'work-pool'
    scopeId.value = 'POOL-2'
    await nextTick()
    expect(filters.locationCode).toBeUndefined()
    expect(filters.lotNo).toBeUndefined()
  })

  it('debounces remote search and rejects a late response from the previous scope', async () => {
    const scopeKind = shallowRef<string | undefined>('self')
    const scopeId = shallowRef<string | undefined>('worker-1')
    const result = useWmsOperationalCandidates('shipment', {
      organizationId: shallowRef('org-1'),
      environmentId: shallowRef('env-1'),
      scopeKind,
      scopeId,
      scopeReady: shallowRef(true),
      filters: reactive({ status: 'Open' }),
    })

    result.searchKeyword.value = ' remote-bin '
    await vi.advanceTimersByTimeAsync(300)
    queryState.factory?.()
    expect(
      listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions,
    ).toHaveBeenLastCalledWith({
      query: expect.objectContaining({ keyword: 'remote-bin', take: 100 }),
    })

    scopeId.value = 'worker-2'
    await nextTick()
    expect(result.locationOptions.value).toEqual([])
    expect(result.lotOptions.value).toEqual([])
  })

  it('keeps an explicit unknown scan through an empty response until the user clears it', async () => {
    const filters = reactive({
      status: 'Open',
      locationCode: undefined as string | undefined,
      lotNo: undefined as string | undefined,
    })
    const Harness = defineComponent({
      setup() {
        const candidates = useWmsOperationalCandidates('shipment', {
          organizationId: shallowRef('org-1'),
          environmentId: shallowRef('env-1'),
          scopeKind: shallowRef('self'),
          scopeId: shallowRef('worker-1'),
          scopeReady: shallowRef(true),
          filters,
        })
        return () =>
          h(WmsOperationalCandidatePicker, {
            locationCode: filters.locationCode,
            lotNo: filters.lotNo,
            locationOptions: candidates.locationOptions.value,
            lotOptions: candidates.lotOptions.value,
            ready: candidates.ready.value,
            sourceLabel: candidates.sourceLabel.value,
            'onUpdate:locationCode': (value) => {
              filters.locationCode = value
            },
            'onUpdate:lotNo': (value) => {
              filters.lotNo = value
            },
            onScanOverrideChange: candidates.setScanOverride,
          })
      },
    })
    const wrapper = mount(Harness)

    wrapper.findComponent(NvScanBar).vm.$emit('scan', 'UNKNOWN-BIN')
    await nextTick()
    expect(filters.locationCode).toBe('UNKNOWN-BIN')
    expect(wrapper.text()).toContain('清除扫码筛选')

    queryState.data!.value = {
      success: true,
      data: {
        sourceKind: 'warehouse-operational-records',
        scopeKind: 'self',
        scopeId: 'worker-1',
        asOfUtc: '2026-07-30T01:01:00Z',
        freshnessUtc: '2026-07-30T01:00:00Z',
        truncated: false,
        locations: [],
        lots: [],
      },
    }
    await nextTick()

    expect(filters.locationCode).toBe('UNKNOWN-BIN')
    expect(wrapper.text()).toContain('UNKNOWN-BIN')
    expect(wrapper.text()).toContain('清除扫码筛选')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('清除扫码筛选'))!
      .trigger('click')

    expect(filters.locationCode).toBeUndefined()
    expect(wrapper.text()).not.toContain('未验证为主数据')
  })
})
