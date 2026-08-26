import type {
  BusinessConsoleBarcodeResolveCandidate,
  BusinessConsoleBarcodeResolveEnvelope,
  BusinessConsoleSearchEnvelope,
} from '@nerv-iip/api-client'
import { nextTick, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { usePdaBarcodeResolver } from './usePdaBarcodeResolver'

const scope = { organizationId: 'org-1', environmentId: 'env-1' }

function envelope(
  status: string,
  candidates: NonNullable<BusinessConsoleBarcodeResolveEnvelope['data']>['candidates'] = [],
): BusinessConsoleBarcodeResolveEnvelope {
  return { success: true, data: { status, candidates, total: candidates.length } }
}

describe('usePdaBarcodeResolver', () => {
  it('exposes pending, then returns the one supported strong-ID route', async () => {
    let settle!: (value: BusinessConsoleBarcodeResolveEnvelope) => void
    const resolveBarcode = vi.fn(
      () => new Promise<BusinessConsoleBarcodeResolveEnvelope>((resolve) => (settle = resolve)),
    )
    const resolver = usePdaBarcodeResolver({ ...scope, resolveBarcode })

    const resolving = resolver.resolve(' WO-CODE ')
    expect(resolver.status.value).toBe('pending')
    expect(resolveBarcode).toHaveBeenCalledWith({
      organizationId: 'org-1',
      environmentId: 'env-1',
      scannedValue: 'WO-CODE',
      pageIndex: 1,
      pageSize: 20,
    })

    settle(
      envelope('resolved', [{ objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } }]),
    )
    await expect(resolving).resolves.toEqual({
      path: '/mes/report',
      query: { workOrderId: 'WO-1' },
    })
    expect(resolver.status.value).toBe('resolved')
  })

  it('does not guess among ambiguous candidates and routes only after manual selection', async () => {
    const candidates: BusinessConsoleBarcodeResolveCandidate[] = [
      { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } },
      { objectType: 'mes-operation', strongIds: { workOrderId: 'WO-1', operationTaskId: 'OP-1' } },
    ]
    const resolver = usePdaBarcodeResolver({
      ...scope,
      resolveBarcode: vi.fn().mockResolvedValue(envelope('ambiguous', candidates)),
    })

    await expect(resolver.resolve('AMB')).resolves.toBeNull()
    expect(resolver.status.value).toBe('ambiguous')
    expect(resolver.candidates.value).toEqual(candidates)
    expect(resolver.selectCandidate(candidates[1]!)).toEqual({
      path: '/mes/operation',
      query: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
    })
  })

  it('keeps unknown search results as non-navigable server candidates', async () => {
    const searchCandidates = vi.fn().mockResolvedValue({
      success: true,
      data: {
        results: [
          {
            objectType: 'mes-work-order',
            title: '工单 WO-9',
            objectNumber: 'WO-9',
            route: '/pc/ignored',
          },
        ],
      },
    } satisfies BusinessConsoleSearchEnvelope)
    const resolver = usePdaBarcodeResolver({
      ...scope,
      resolveBarcode: vi.fn().mockResolvedValue(envelope('unknown')),
      searchCandidates,
    })

    await resolver.resolve('UNKNOWN-9')
    expect(resolver.status.value).toBe('unknown')
    await resolver.searchUnknownCandidates()
    expect(searchCandidates).toHaveBeenCalledWith('UNKNOWN-9')
    expect(resolver.searchResults.value[0]?.route).toBe('/pc/ignored')
    expect(resolver.searchStatus.value).toBe('resolved')
  })

  it.each(['unsupported', 'unknown', 'forbidden'] as const)(
    'represents the %s outcome explicitly',
    async (status) => {
      const resolver = usePdaBarcodeResolver({
        ...scope,
        resolveBarcode: vi.fn().mockResolvedValue(envelope(status)),
      })
      await resolver.resolve('CODE')
      expect(resolver.status.value).toBe(status)
    },
  )

  it('fails closed for forbidden requests and malformed resolved responses', async () => {
    const forbidden = usePdaBarcodeResolver({
      ...scope,
      resolveBarcode: vi.fn().mockRejectedValue({ response: { status: 403 } }),
    })
    await forbidden.resolve('NOPE')
    expect(forbidden.status.value).toBe('forbidden')

    const malformed = usePdaBarcodeResolver({
      ...scope,
      resolveBarcode: vi.fn().mockResolvedValue(envelope('resolved', [])),
    })
    await malformed.resolve('BROKEN')
    expect(malformed.status.value).toBe('unsupported')
  })

  it('ignores a late response from an older scan', async () => {
    let settleFirst!: (value: BusinessConsoleBarcodeResolveEnvelope) => void
    const resolveBarcode = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<BusinessConsoleBarcodeResolveEnvelope>((resolve) => (settleFirst = resolve)),
      )
      .mockResolvedValueOnce(envelope('unknown'))
    const resolver = usePdaBarcodeResolver({ ...scope, resolveBarcode })

    const first = resolver.resolve('FIRST')
    await resolver.resolve('SECOND')
    settleFirst(
      envelope('resolved', [{ objectType: 'mes-work-order', strongIds: { workOrderId: 'OLD' } }]),
    )

    await expect(first).resolves.toBeNull()
    expect(resolver.scannedValue.value).toBe('SECOND')
    expect(resolver.status.value).toBe('unknown')
  })

  it('ignores a late resolve rejection after a newer scan starts', async () => {
    let rejectFirst!: (reason?: unknown) => void
    const resolveBarcode = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<BusinessConsoleBarcodeResolveEnvelope>((_resolve, reject) => {
            rejectFirst = reject
          }),
      )
      .mockResolvedValueOnce(envelope('unknown'))
    const resolver = usePdaBarcodeResolver({ ...scope, resolveBarcode })

    const first = resolver.resolve('FIRST')
    await resolver.resolve('SECOND')
    rejectFirst(new Error('旧解析请求失败'))

    await expect(first).resolves.toBeNull()
    expect(resolver.scannedValue.value).toBe('SECOND')
    expect(resolver.status.value).toBe('unknown')
  })

  it('ignores a late resolve rejection after scope drift', async () => {
    const organizationId = shallowRef('org-1')
    let rejectFirst!: (reason?: unknown) => void
    const resolver = usePdaBarcodeResolver({
      organizationId,
      environmentId: 'env-1',
      resolveBarcode: vi.fn(
        () =>
          new Promise<BusinessConsoleBarcodeResolveEnvelope>((_resolve, reject) => {
            rejectFirst = reject
          }),
      ),
    })

    const first = resolver.resolve('FIRST')
    organizationId.value = 'org-2'
    await nextTick()
    rejectFirst(new Error('旧 scope 解析请求失败'))

    await expect(first).resolves.toBeNull()
    expect(resolver.scannedValue.value).toBe('')
    expect(resolver.status.value).toBe('idle')
  })

  it('discards an unknown search response after a newer scan starts', async () => {
    let settleSearch!: (value: BusinessConsoleSearchEnvelope) => void
    const resolver = usePdaBarcodeResolver({
      ...scope,
      resolveBarcode: vi.fn().mockResolvedValue(envelope('unknown')),
      searchCandidates: vi.fn(
        () => new Promise<BusinessConsoleSearchEnvelope>((resolve) => (settleSearch = resolve)),
      ),
    })

    await resolver.resolve('FIRST')
    const firstSearch = resolver.searchUnknownCandidates()
    expect(resolver.searchStatus.value).toBe('pending')

    await resolver.resolve('SECOND')
    settleSearch({
      success: true,
      data: { results: [{ objectType: 'mes-work-order', title: '过期候选' }] },
    })
    await firstSearch

    expect(resolver.scannedValue.value).toBe('SECOND')
    expect(resolver.status.value).toBe('unknown')
    expect(resolver.searchStatus.value).toBe('idle')
    expect(resolver.searchResults.value).toEqual([])
  })

  it('ignores a late unknown-search rejection after a newer scan starts', async () => {
    let rejectSearch!: (reason?: unknown) => void
    const resolver = usePdaBarcodeResolver({
      ...scope,
      resolveBarcode: vi.fn().mockResolvedValue(envelope('unknown')),
      searchCandidates: vi.fn(
        () =>
          new Promise<BusinessConsoleSearchEnvelope>((_resolve, reject) => {
            rejectSearch = reject
          }),
      ),
    })

    await resolver.resolve('FIRST')
    const firstSearch = resolver.searchUnknownCandidates()
    await resolver.resolve('SECOND')
    rejectSearch(new Error('旧候选搜索失败'))
    await firstSearch

    expect(resolver.scannedValue.value).toBe('SECOND')
    expect(resolver.status.value).toBe('unknown')
    expect(resolver.searchStatus.value).toBe('idle')
    expect(resolver.searchResults.value).toEqual([])
  })

  it('ignores a late unknown-search rejection after scope drift', async () => {
    const organizationId = shallowRef('org-1')
    let rejectSearch!: (reason?: unknown) => void
    const resolver = usePdaBarcodeResolver({
      organizationId,
      environmentId: 'env-1',
      resolveBarcode: vi.fn().mockResolvedValue(envelope('unknown')),
      searchCandidates: vi.fn(
        () =>
          new Promise<BusinessConsoleSearchEnvelope>((_resolve, reject) => {
            rejectSearch = reject
          }),
      ),
    })

    await resolver.resolve('FIRST')
    const firstSearch = resolver.searchUnknownCandidates()
    organizationId.value = 'org-2'
    await nextTick()
    rejectSearch(new Error('旧 scope 候选搜索失败'))
    await firstSearch

    expect(resolver.scannedValue.value).toBe('')
    expect(resolver.status.value).toBe('idle')
    expect(resolver.searchStatus.value).toBe('idle')
    expect(resolver.searchResults.value).toEqual([])
  })

  it('invalidates pending work on scope drift and freezes the latest scope per request', async () => {
    const organizationId = shallowRef('org-1')
    const environmentId = shallowRef('env-1')
    let settleFirst!: (value: BusinessConsoleBarcodeResolveEnvelope) => void
    const resolveBarcode = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<BusinessConsoleBarcodeResolveEnvelope>((resolve) => (settleFirst = resolve)),
      )
      .mockResolvedValueOnce(envelope('unknown'))
    const resolver = usePdaBarcodeResolver({
      organizationId,
      environmentId,
      resolveBarcode,
    })

    const first = resolver.resolve('FIRST')
    organizationId.value = 'org-2'
    await nextTick()
    settleFirst(
      envelope('resolved', [{ objectType: 'mes-work-order', strongIds: { workOrderId: 'OLD' } }]),
    )

    await expect(first).resolves.toBeNull()
    expect(resolver.status.value).toBe('idle')
    expect(resolver.scannedValue.value).toBe('')

    await resolver.resolve('SECOND')
    expect(resolveBarcode).toHaveBeenLastCalledWith({
      organizationId: 'org-2',
      environmentId: 'env-1',
      scannedValue: 'SECOND',
      pageIndex: 1,
      pageSize: 20,
    })
  })
})
