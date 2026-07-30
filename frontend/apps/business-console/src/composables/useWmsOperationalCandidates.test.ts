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

describe('PC WMS operational candidates', () => {
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
        truncated: true,
        locations: [{ locationCode: 'A-01', skuCodes: ['SKU-1'] }],
        lots: [
          { lotNo: 'LOT-A', skuCode: 'SKU-1', locationCodes: ['A-01'] },
          { lotNo: 'LOT-B', skuCode: 'SKU-2', locationCodes: ['B-01'] },
        ],
      },
    }
  })

  it.each([
    ['receipt', listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions],
    ['shipment', listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions],
    ['count', listBusinessConsoleWmsCountOperationalCandidatesQueryOptions],
  ] as const)('maps %s candidates to its stable query options', (kind, factory) => {
    const filters = reactive({
      organizationId: 'org-1',
      environmentId: 'env-1',
      scopeKind: 'self',
      scopeId: 'worker-1',
      status: 'Open',
      skuCode: 'SKU-1',
      locationCode: 'A-01',
    })

    const result = useWmsOperationalCandidates(kind, filters)

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
    expect(result.locationOptions.value).toEqual([
      expect.objectContaining({ value: 'A-01', label: 'A-01' }),
    ])
    expect(result.lotOptions.value).toEqual(
      kind === 'count' ? [] : [expect.objectContaining({ value: 'LOT-A', label: 'LOT-A' })],
    )
    expect(result.sourceLabel.value).toContain('当前范围仓储作业记录候选')
    expect(result.sourceKind.value).toBe('warehouse-operational-records')
    expect(result.truncated.value).toBe(true)
  })

  it('does not enable a candidate query until trusted scope is resolved', () => {
    const filters = reactive({
      organizationId: 'org-1',
      environmentId: 'env-1',
      scopeKind: undefined as string | undefined,
      scopeId: undefined as string | undefined,
    })

    useWmsOperationalCandidates('receipt', filters)

    expect(queryState.options?.enabled).toBe(false)
  })

  it('clears selected location and lot when trusted scope or status changes', async () => {
    const filters = reactive({
      organizationId: 'org-1',
      environmentId: 'env-1',
      scopeKind: 'self',
      scopeId: 'worker-1',
      status: 'Open',
      locationCode: 'A-01' as string | undefined,
      lotNo: 'LOT-A' as string | undefined,
    })
    useWmsOperationalCandidates('shipment', filters)

    filters.status = 'Completed'
    await nextTick()

    expect(filters.locationCode).toBeUndefined()
    expect(filters.lotNo).toBeUndefined()

    filters.locationCode = 'B-01'
    filters.lotNo = 'LOT-B'
    filters.scopeId = 'pool-2'
    await nextTick()

    expect(filters.locationCode).toBeUndefined()
    expect(filters.lotNo).toBeUndefined()
  })

  it('never exposes lot candidates for count flows', () => {
    const filters = reactive({
      organizationId: 'org-1',
      environmentId: 'env-1',
      scopeKind: 'self',
      scopeId: 'worker-1',
      locationCode: 'A-01',
    })

    const result = useWmsOperationalCandidates('count', filters)

    expect(result.lotOptions.value).toEqual([])
  })
})
