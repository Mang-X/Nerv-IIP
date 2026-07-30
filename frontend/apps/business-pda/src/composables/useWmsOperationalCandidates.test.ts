import { nextTick, reactive, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  listBusinessConsoleWmsCountOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions,
} from '@nerv-iip/api-client'
import { useWmsOperationalCandidates } from './useWmsOperationalCandidates'

const queryState = vi.hoisted(() => ({
  response: undefined as unknown,
  options: undefined as undefined | { enabled?: boolean },
}))

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
    queryState.options = factory()
    return {
      data: shallowRef(queryState.response),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))

describe('PDA WMS operational candidates', () => {
  beforeEach(() => {
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
    const scopeKey = shallowRef<string | undefined>('self:worker-1')
    const filters = reactive({ status: 'Open', locationCode: 'A-01', skuCode: 'SKU-1' })

    const result = useWmsOperationalCandidates(kind, {
      organizationId: shallowRef('org-1'),
      environmentId: shallowRef('env-1'),
      scopeKey,
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

  it('does not request candidates for an unresolved or untrusted scope key', () => {
    useWmsOperationalCandidates('receipt', {
      organizationId: shallowRef('org-1'),
      environmentId: shallowRef('env-1'),
      scopeKey: shallowRef('unknown:scope'),
      filters: reactive({ status: 'Open' }),
    })

    expect(queryState.options?.enabled).toBe(false)
  })

  it('clears picker values when scope or status changes', async () => {
    const scopeKey = shallowRef<string | undefined>('self:worker-1')
    const filters = reactive({
      status: 'Open',
      locationCode: 'A-01' as string | undefined,
      lotNo: 'LOT-A' as string | undefined,
    })
    useWmsOperationalCandidates('shipment', {
      organizationId: shallowRef('org-1'),
      environmentId: shallowRef('env-1'),
      scopeKey,
      filters,
    })

    filters.status = 'Completed'
    await nextTick()
    expect(filters.locationCode).toBeUndefined()
    expect(filters.lotNo).toBeUndefined()

    filters.locationCode = 'A-02'
    filters.lotNo = 'LOT-B'
    scopeKey.value = 'work-pool:POOL-2'
    await nextTick()
    expect(filters.locationCode).toBeUndefined()
    expect(filters.lotNo).toBeUndefined()
  })
})
