import {
  api,
  seedPrincipal,
  requestFor,
  queryMetadata,
  publishCached,
  authoritativeDetail,
  workOrderGuid,
  authoritativeListItem,
  withPagination,
  invalidListEnvelopes,
  setupMaintenanceSelfWorkOrdersTests,
} from './useMaintenanceSelfWorkOrdersTestFixture'
import { expect, it } from 'vitest'
import { computed, nextTick } from 'vue'
import { useAuthStore } from '@/stores/auth'
import {
  useMaintenanceSelfWorkOrderDetail,
  useMaintenanceSelfWorkOrders,
} from './useMaintenanceSelfWorkOrders'
import { describe } from 'vitest'

describe('useMaintenanceSelfWorkOrders', () => {
  setupMaintenanceSelfWorkOrdersTests()

  it('suppresses the personal queue when principal scope or read permission is unavailable', () => {
    seedPrincipal({ principalId: '', permissionCodes: [] })

    const result = useMaintenanceSelfWorkOrders()
    const options = api.queryFactories.get('maintenance-list')?.()

    expect(result.scopeReady.value).toBe(false)
    expect(options?.enabled).toBe(false)
    expect(result.items.value).toEqual([])
  })

  it('fails closed when maintenance read exists without device location read permission', () => {
    seedPrincipal({ permissionCodes: ['business.maintenance.work-orders.read'] })

    const list = useMaintenanceSelfWorkOrders()
    const detail = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )

    expect(list.scopeReady.value).toBe(false)
    expect(api.queryFactories.get('maintenance-list')?.().enabled).toBe(false)
    expect(detail.enabled.value).toBe(false)
    expect(api.queryFactories.get('maintenance-detail')?.().enabled).toBe(false)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it('uses a non-repeating component-instance identity for every list and detail query family', async () => {
    seedPrincipal()
    useMaintenanceSelfWorkOrders()
    const firstList = queryMetadata('maintenance-list')
    const firstPrincipal = queryMetadata('maintenance-list-principal')
    useMaintenanceSelfWorkOrderDetail(computed(() => workOrderGuid(101)))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderGuid(101), 'DEV-CNC-01'),
    }
    await nextTick()
    const firstDetail = queryMetadata('maintenance-detail')
    const firstDevice = queryMetadata('device-detail')
    const firstIdentities = queryMetadata('maintenance-identities')

    useMaintenanceSelfWorkOrders()
    const secondList = queryMetadata('maintenance-list')
    const secondPrincipal = queryMetadata('maintenance-list-principal')
    useMaintenanceSelfWorkOrderDetail(computed(() => workOrderGuid(101)))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderGuid(101), 'DEV-CNC-01'),
    }
    await nextTick()
    const secondDetail = queryMetadata('maintenance-detail')
    const secondDevice = queryMetadata('device-detail')
    const secondIdentities = queryMetadata('maintenance-identities')

    for (const [first, second] of [
      [firstList, secondList],
      [firstPrincipal, secondPrincipal],
      [firstDetail, secondDetail],
      [firstDevice, secondDevice],
      [firstIdentities, secondIdentities],
    ]) {
      expect(first?.identity).toBeTypeOf('string')
      expect(second?.identity).toBeTypeOf('string')
      expect(second?.identity).not.toBe(first?.identity)
    }
  })

  it('binds status, device, keyword, and first-page pagination to the server self query', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()

    result.filters.status = 'accepted'
    result.filters.deviceAssetIds = ['019f0000-0000-7000-8000-000000000001', 'DEV-CNC-01']
    result.filters.keyword = '主轴'
    await nextTick()

    expect(requestFor('maintenance-list')).toEqual({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'principal-1',
        status: 'accepted',
        deviceAssetReferences: ['019f0000-0000-7000-8000-000000000001', 'DEV-CNC-01'],
        keyword: '主轴',
        skip: 0,
        take: 20,
      },
    })
  })

  it('preserves GUID-shaped device references as opaque Ordinal codes', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    const publicId = '019f0000-0000-7000-8000-000000000001'

    result.filters.deviceAssetIds = [publicId.toUpperCase(), publicId]
    await nextTick()
    expect(
      (requestFor('maintenance-list') as { query: { deviceAssetReferences?: string[] } }).query
        .deviceAssetReferences,
    ).toEqual([publicId.toUpperCase(), publicId])

    result.filters.deviceAssetIds = ['DEV-A', 'dev-a', 'DEV-A']
    await nextTick()
    expect(
      (requestFor('maintenance-list') as { query: { deviceAssetReferences?: string[] } }).query
        .deviceAssetReferences,
    ).toEqual(['DEV-A', 'dev-a'])
  })

  it('keeps scope and filter query identities collision-free for delimiter-bearing values', async () => {
    seedPrincipal({ organizationId: 'org:env', environmentId: 'x' })
    const result = useMaintenanceSelfWorkOrders()
    const firstScopeKey = result.scopeKey.value
    const firstIdentity = queryMetadata('maintenance-list')?.identity

    seedPrincipal({ organizationId: 'org', environmentId: 'env:x' })
    await nextTick()
    expect(result.scopeKey.value).not.toBe(firstScopeKey)
    expect(queryMetadata('maintenance-list')?.identity).not.toBe(firstIdentity)

    result.filters.deviceAssetIds = ['DEV-A,DEV-B']
    result.filters.keyword = `bearing\u001fteam`
    await nextTick()
    const delimiterIdentity = queryMetadata('maintenance-list')?.identity

    result.filters.deviceAssetIds = ['DEV-A', 'DEV-B']
    result.filters.keyword = 'bearing'
    await nextTick()
    expect(queryMetadata('maintenance-list')?.identity).not.toBe(delimiterIdentity)
  })

  it('drops an unknown restored status instead of sending it to the service', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()

    ;(result.filters as { status: string }).status = 'future-server-state'
    await nextTick()

    expect(requestFor('maintenance-list')).toEqual({
      query: expect.not.objectContaining({ status: expect.anything() }),
    })
  })

  it('clears visible rows and reports failure for a rejected HTTP 200 envelope', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()

    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: [authoritativeListItem(1)],
        total: 1,
        skip: 0,
        take: 20,
      },
    }
    await nextTick()
    expect(result.items.value).toHaveLength(1)

    api.data.get('maintenance-list')!.value = {
      success: false,
      data: {
        items: [authoritativeListItem(2)],
        total: 1,
        skip: 0,
        take: 20,
      },
    }
    await nextTick()

    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.items.value).toEqual([])
    expect(result.total.value).toBe(0)
  })

  it('canonicalizes the strong work-order ID but preserves the untyped device reference', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    const workOrderId = workOrderGuid(1)
    const deviceAssetId = '019f0000-0000-7000-8000-000000000001'

    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: [
          authoritativeListItem(1, {
            workOrderId: workOrderId.toUpperCase(),
            deviceAssetId: deviceAssetId.toUpperCase(),
          }),
        ],
        total: 1,
        skip: 0,
        take: 20,
      },
    }
    await nextTick()

    expect(result.items.value[0]).toMatchObject({
      workOrderId,
      deviceAssetId: deviceAssetId.toUpperCase(),
    })
  })

  it('requires a fresh list generation when scope returns A to B to A', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    const aMetadata = queryMetadata('maintenance-list')!
    const aEnvelope = {
      success: true,
      data: { items: [authoritativeListItem(1)], total: 1, skip: 0, take: 20 },
    }
    api.data.get('maintenance-list')!.value = aEnvelope
    await nextTick()
    expect(result.items.value[0]?.workOrderId).toBe(workOrderGuid(1))

    seedPrincipal({ principalId: 'principal-2' })
    await nextTick()
    expect(result.items.value).toEqual([])
    expect(queryMetadata('maintenance-list')?.generation).not.toBe(aMetadata.generation)

    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: [authoritativeListItem(2, { assignedTechnicianUserId: 'principal-2' })],
        total: 1,
        skip: 0,
        take: 20,
      },
    }
    await nextTick()
    expect(result.items.value[0]?.workOrderId).toBe(workOrderGuid(2))

    seedPrincipal()
    await nextTick()
    publishCached(
      'maintenance-list',
      aMetadata.generation as number,
      aMetadata.identity as string,
      aEnvelope,
    )
    await nextTick()

    expect(result.items.value).toEqual([])
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.pending.value).toBe(true)

    api.errors.get('maintenance-list')!.value = { status: 403 }
    await nextTick()
    expect(result.items.value).toEqual([])
    expect(result.hasFailedResponse.value).toBe(true)
  })

  it.each(invalidListEnvelopes)(
    'rejects a malformed first-page envelope: %s',
    async (_, envelope) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()

      api.data.get('maintenance-list')!.value = withPagination(envelope, 0, 20)
      await nextTick()

      expect(result.hasSuccessfulResponse.value).toBe(false)
      expect(result.hasFailedResponse.value).toBe(true)
      expect(result.items.value).toEqual([])
      expect(result.total.value).toBe(0)
    },
  )

  it.each([
    ['missing skip', { take: 20 }],
    ['missing take', { skip: 0 }],
    ['unsafe skip', { skip: Number.MAX_SAFE_INTEGER + 1, take: 20 }],
    ['fractional take', { skip: 0, take: 20.5 }],
    ['misaligned skip', { skip: 20, take: 20 }],
    ['misaligned take', { skip: 0, take: 10 }],
  ])('rejects invalid first-page response pagination: %s', async (_, pagination) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: { items: [], total: 0, ...pagination },
    }
    await nextTick()

    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.items.value).toEqual([])
  })

  it.each([null, undefined, 'principal-other'])(
    'rejects a first-page row not authoritatively assigned to the current principal: %s',
    async (assignedTechnicianUserId) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()
      api.data.get('maintenance-list')!.value = {
        success: true,
        data: {
          items: [authoritativeListItem(1, { assignedTechnicianUserId })],
          total: 1,
          skip: 0,
          take: 20,
        },
      }
      await nextTick()

      expect(result.hasFailedResponse.value).toBe(true)
      expect(result.items.value).toEqual([])
    },
  )

  it.each(invalidListEnvelopes)(
    'rejects a malformed next-page envelope: %s',
    async (_, envelope) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()
      api.data.get('maintenance-list')!.value = {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => authoritativeListItem(index)),
          total: 21,
          skip: 0,
          take: 20,
        },
      }
      api.list.mockResolvedValueOnce({ data: withPagination(envelope, 20, 20) })
      await nextTick()

      await result.loadMore()

      expect(result.loadMoreError.value).toBeInstanceOf(Error)
      expect(result.items.value).toHaveLength(20)
    },
  )

  it.each([
    ['misaligned skip', { skip: 0, take: 20 }],
    ['misaligned take', { skip: 20, take: 10 }],
  ])('rejects invalid next-page response pagination: %s', async (_, pagination) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: Array.from({ length: 20 }, (_, index) => authoritativeListItem(index)),
        total: 21,
        skip: 0,
        take: 20,
      },
    }
    api.list.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          items: [authoritativeListItem(20)],
          total: 21,
          ...pagination,
        },
      },
    })
    await nextTick()

    await result.loadMore()

    expect(result.loadMoreError.value).toBeInstanceOf(Error)
    expect(result.items.value).toHaveLength(20)
    expect(
      result.items.value.find((item) => item.workOrderId === workOrderGuid(20)),
    ).toBeUndefined()
  })

  it.each([null, undefined, 'principal-other'])(
    'rejects a next-page row not authoritatively assigned to the current principal: %s',
    async (assignedTechnicianUserId) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()
      api.data.get('maintenance-list')!.value = {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => authoritativeListItem(index)),
          total: 21,
          skip: 0,
          take: 20,
        },
      }
      api.list.mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [authoritativeListItem(20, { assignedTechnicianUserId })],
            total: 21,
            skip: 20,
            take: 20,
          },
        },
      })
      await nextTick()

      await result.loadMore()

      expect(result.loadMoreError.value).toBeInstanceOf(Error)
      expect(result.items.value).toHaveLength(20)
    },
  )

  it('hides retained rows while the current list identity is refreshing and keeps them hidden on failure', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: [authoritativeListItem(1)],
        total: 1,
        skip: 0,
        take: 20,
      },
    }
    await nextTick()
    expect(result.items.value).toHaveLength(1)

    let resolveRefetch!: () => void
    api.refetches
      .get('maintenance-list')!
      .mockImplementationOnce(() => new Promise<void>((resolve) => (resolveRefetch = resolve)))
    const refreshPromise = result.refresh()
    await nextTick()

    expect(result.pending.value).toBe(true)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.items.value).toEqual([])
    expect(result.total.value).toBe(0)

    api.data.get('maintenance-list')!.value = { success: false, data: null }
    resolveRefetch()
    await refreshPromise
    await nextTick()

    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.items.value).toEqual([])
  })

  it('retries the exact principal identity lookup together with a list refresh', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: { items: [authoritativeListItem(1)], total: 1, skip: 0, take: 20 },
    }
    api.errors.get('maintenance-list-principal')!.value = { status: 502 }
    await nextTick()
    expect(result.principalDisplayName.value).toBeUndefined()

    api.refetches.get('maintenance-list-principal')!.mockImplementationOnce(async () => {
      api.errors.get('maintenance-list-principal')!.value = undefined
      api.data.get('maintenance-list-principal')!.value = '张维修'
    })
    await result.refresh()
    await nextTick()

    expect(api.refetches.get('maintenance-list')).toHaveBeenCalledTimes(1)
    expect(api.refetches.get('maintenance-list-principal')).toHaveBeenCalledTimes(1)
    expect(result.principalDisplayName.value).toBe('张维修')
  })

  it.each([
    [
      'logout',
      () => {
        useAuthStore().principal = undefined
      },
    ],
    ['principal switch', () => seedPrincipal({ principalId: 'principal-2' })],
    ['organization switch', () => seedPrincipal({ organizationId: 'org-002' })],
    ['environment switch', () => seedPrincipal({ environmentId: 'env-prod' })],
    [
      'permission loss',
      () => seedPrincipal({ permissionCodes: ['business.maintenance.work-orders.read'] }),
    ],
  ])(
    'does not manually refetch a worker after a delayed list refresh crosses %s',
    async (_, driftScope) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()
      api.data.get('maintenance-list')!.value = {
        success: true,
        data: { items: [authoritativeListItem(1)], total: 1, skip: 0, take: 20 },
      }
      await nextTick()
      expect(result.items.value).toHaveLength(1)

      let resolveListRefresh!: () => void
      api.refetches
        .get('maintenance-list')!
        .mockImplementationOnce(
          () => new Promise<void>((resolve) => (resolveListRefresh = resolve)),
        )
      const refreshPromise = result.refresh()
      await nextTick()

      driftScope()
      await nextTick()

      expect(result.items.value).toEqual([])
      expect(result.hasSuccessfulResponse.value).toBe(false)
      expect(api.refetches.get('maintenance-list-principal')).not.toHaveBeenCalled()

      resolveListRefresh()
      await refreshPromise
      await nextTick()

      expect(api.refetches.get('maintenance-list')).toHaveBeenCalledTimes(1)
      expect(api.refetches.get('maintenance-list-principal')).not.toHaveBeenCalled()
      expect(result.items.value).toEqual([])
    },
  )

  it('loads the next page with the same self scope and server filters', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    result.filters.status = 'inProgress'
    result.filters.deviceAssetIds = ['device-2', 'DEV-2', 'device-2']
    result.filters.keyword = '轴承'
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: Array.from({ length: 20 }, (_, index) => authoritativeListItem(index)),
        total: 21,
        skip: 0,
        take: 20,
      },
    }
    api.list.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          items: [authoritativeListItem(20)],
          total: 21,
          skip: 20,
          take: 20,
        },
      },
    })
    await nextTick()

    await result.loadMore()

    expect(api.list).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'principal-1',
        status: 'inProgress',
        deviceAssetReferences: ['device-2', 'DEV-2'],
        keyword: '轴承',
        skip: 20,
        take: 20,
      },
      throwOnError: true,
    })
    expect(result.items.value.at(-1)?.workOrderId).toBe(workOrderGuid(20))
  })
})
