import { setupMaintenanceDetailTests } from './useMaintenanceSelfWorkOrdersTestFixture'
import {
  api,
  seedPrincipal,
  requestFor,
  queryMetadata,
  publishCached,
  authoritativeDetail,
  workOrderGuid,
} from './useMaintenanceSelfWorkOrdersTestFixture'

import { describe, expect, it } from 'vitest'
import { computed, nextTick, shallowRef } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useMaintenanceSelfWorkOrderDetail } from '.'

describe('useMaintenanceSelfWorkOrderDetail', () => {
  setupMaintenanceDetailTests()

  it('resolves readable users and teams through exact scoped MasterData contracts', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
        assignedTeamId: 'TEAM-A',
      },
    }
    await nextTick()
    api.workers.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          pageIndex: 1,
          pageSize: 1,
          totalCount: 1,
          items: [
            {
              userId: 'principal-1',
              employeeNo: 'EMP-001',
              displayName: '张维修',
              employmentStatus: 'active',
              active: true,
              teams: [],
              skills: [],
              snapshotVersion: 'worker-v1',
            },
          ],
        },
      },
    })
    api.resourceDetail.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          resourceType: 'team',
          code: 'TEAM-A',
          displayName: '甲班',
          active: true,
          snapshotVersion: 'team-v1',
          organizationId: 'org-001',
          environmentId: 'env-dev',
        },
      },
    })

    const options = api.queryFactories.get('maintenance-identities')?.()
    expect(options?.enabled).toBe(true)
    const identities = await (options?.query as () => Promise<unknown>)()
    api.data.get('maintenance-identities')!.value = identities
    await nextTick()

    expect(api.workers).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        userId: 'principal-1',
        pageIndex: 1,
        pageSize: 1,
      }),
      throwOnError: true,
    })
    expect(api.resourceDetail).toHaveBeenCalledWith({
      path: { resourceType: 'team', code: 'TEAM-A' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      throwOnError: true,
    })
    expect(result.identities.value).toEqual({
      users: { 'principal-1': '张维修' },
      teams: { 'TEAM-A': '甲班' },
    })
  })

  it('fails identity enrichment closed when an exact worker response returns another user', async () => {
    seedPrincipal()
    useMaintenanceSelfWorkOrderDetail(computed(() => '019f0000-0000-7000-8000-000000000101'))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
    }
    await nextTick()
    api.workers.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          pageIndex: 1,
          pageSize: 1,
          totalCount: 1,
          items: [
            {
              userId: 'principal-other',
              employeeNo: 'EMP-002',
              displayName: '错误人员',
              employmentStatus: 'active',
              active: true,
              teams: [],
              skills: [],
              snapshotVersion: 'worker-v1',
            },
          ],
        },
      },
    })

    const options = api.queryFactories.get('maintenance-identities')?.()
    await expect((options?.query as () => Promise<unknown>)()).rejects.toThrow('身份资料暂不可用')
  })

  it('bounds identity enrichment requests for an oversized lifecycle', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
        lifecycle: Array.from({ length: 21 }, (_, index) => ({
          action: 'accept',
          fromStatus: 'open',
          toStatus: 'accepted',
          actorPrincipalId: `principal-${index + 2}`,
          technicianUserId: null,
          teamId: null,
          reason: '历史动作',
          resultingVersion: index + 1,
          occurredAtUtc: '2026-08-02T01:02:03.000Z',
        })),
      },
    }
    await nextTick()

    expect(api.queryFactories.get('maintenance-identities')?.().enabled).toBe(false)
    expect(result.identitiesUnavailable.value).toBe(true)
    expect(api.workers).not.toHaveBeenCalled()
  })

  it('clears retained identity names synchronously when the work-order identity changes', async () => {
    seedPrincipal()
    const routeId = shallowRef('019f0000-0000-7000-8000-000000000102')
    const result = useMaintenanceSelfWorkOrderDetail(routeId)
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000102', 'DEV-A'),
    }
    await nextTick()
    api.data.get('maintenance-identities')!.value = {
      users: { 'principal-1': '旧人员' },
      teams: {},
    }
    await nextTick()
    expect(result.identities.value?.users['principal-1']).toBe('旧人员')

    routeId.value = '019f0000-0000-7000-8000-000000000103'
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000103', 'DEV-B'),
    }
    await nextTick()

    expect(result.identities.value).toBeUndefined()
  })

  it('rejects cached and delayed detail generations when route returns A to B to A', async () => {
    seedPrincipal()
    const workOrderA = '019f0000-0000-7000-8000-000000000102'
    const workOrderB = '019f0000-0000-7000-8000-000000000103'
    const routeId = shallowRef(workOrderA)
    const result = useMaintenanceSelfWorkOrderDetail(routeId)
    const aMetadata = queryMetadata('maintenance-detail')!
    const aEnvelope = {
      success: true,
      data: {
        ...authoritativeDetail(workOrderA, 'DEV-A'),
        lifecycle: [
          {
            action: 'accept',
            fromStatus: 'open',
            toStatus: 'accepted',
            actorPrincipalId: 'principal-1',
            technicianUserId: 'principal-1',
            teamId: null,
            reason: '旧 A 生命周期',
            resultingVersion: 7,
            occurredAtUtc: '2026-08-02T01:02:03.000Z',
          },
        ],
      },
    }
    api.data.get('maintenance-detail')!.value = aEnvelope
    await nextTick()
    expect(result.authoritativeWorkOrder.value?.lifecycle[0]?.reason).toBe('旧 A 生命周期')

    routeId.value = workOrderB
    await nextTick()
    publishCached(
      'maintenance-detail',
      aMetadata.generation as number,
      aMetadata.identity as string,
      aEnvelope,
    )
    await nextTick()
    expect(result.authoritativeWorkOrder.value).toBeUndefined()
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderB, 'DEV-B'),
    }
    await nextTick()
    expect(result.authoritativeWorkOrder.value?.workOrderId).toBe(workOrderB)

    routeId.value = workOrderA
    await nextTick()
    publishCached(
      'maintenance-detail',
      aMetadata.generation as number,
      aMetadata.identity as string,
      aEnvelope,
    )
    await nextTick()
    expect(result.authoritativeWorkOrder.value).toBeUndefined()
    expect(result.identities.value).toBeUndefined()
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)

    api.errors.get('maintenance-detail')!.value = { status: 403 }
    await nextTick()
    expect(result.authoritativeHasFailedResponse.value).toBe(true)
    expect(result.authoritativeWorkOrder.value).toBeUndefined()
  })

  it('treats the strong-ID device detail as part of aggregate detail state', async () => {
    seedPrincipal()
    const routeId = shallowRef('019f0000-0000-7000-8000-000000000104')
    const result = useMaintenanceSelfWorkOrderDetail(routeId)

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: { workOrderId: '019f0000-0000-7000-8000-000000000105', deviceAssetId: 'device-old' },
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(requestFor('device-detail')).toEqual({
      path: { resourceType: 'device-asset', code: '' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000104', 'DEV-CNC-01'),
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.pending.value).toBe(true)
    expect(requestFor('device-detail')).toEqual({
      path: { resourceType: 'device-asset', code: 'DEV-CNC-01' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-OLD',
        displayName: '迟到的旧设备',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)

    api.data.get('device-detail')!.value = undefined
    api.errors.get('device-detail')!.value = { status: 403 }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.error.value).toEqual({ status: 403 })

    api.errors.get('device-detail')!.value = undefined
    api.data.get('device-detail')!.value = { success: false, data: null }
    await nextTick()
    expect(result.hasFailedResponse.value).toBe(true)

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'work-center',
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.hasFailedResponse.value).toBe(true)

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-other',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
        displayName: '一号数控机床',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        workshopCode: 'WS-1',
      },
    }
    await nextTick()
    expect(result.device.value).toMatchObject({
      code: 'DEV-CNC-01',
      displayName: '一号数控机床',
      workshopCode: 'WS-1',
    })
    expect(result.workOrder.value?.workOrderId).toBe('019f0000-0000-7000-8000-000000000104')
    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(result.hasFailedResponse.value).toBe(false)

    api.errors.get('maintenance-detail')!.value = { status: 403 }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.device.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    api.errors.get('maintenance-detail')!.value = undefined

    routeId.value = '019f0000-0000-7000-8000-000000000106'
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000106', 'DEV-LATEST'),
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.pending.value).toBe(true)

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
        displayName: '迟到的一号数控机床',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
  })

  it('retries the work order first and then the newly resolved device detail', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
    }
    await nextTick()

    const calls: string[] = []
    api.refetches.get('maintenance-detail')!.mockImplementation(async () => {
      calls.push('work-order')
    })
    api.refetches.get('device-detail')!.mockImplementation(async () => {
      calls.push('device')
    })

    await result.refresh()

    expect(calls).toEqual(['work-order', 'device'])
    expect(api.refetches.get('maintenance-detail')).toHaveBeenCalledTimes(1)
    expect(api.refetches.get('device-detail')).toHaveBeenCalledTimes(1)
    expect(api.refetches.get('maintenance-identities')).toHaveBeenCalledTimes(1)

    calls.length = 0
    api.refetches.get('maintenance-detail')!.mockImplementation(async () => {
      calls.push('work-order')
      api.errors.get('maintenance-detail')!.value = { status: 403 }
    })

    await result.refresh()

    expect(calls).toEqual(['work-order'])
    expect(api.refetches.get('maintenance-detail')).toHaveBeenCalledTimes(2)
    expect(api.refetches.get('device-detail')).toHaveBeenCalledTimes(1)
    expect(api.refetches.get('maintenance-identities')).toHaveBeenCalledTimes(1)
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
    'does not manually refetch detail enrichments after a delayed work-order refresh crosses %s',
    async (_, driftScope) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(computed(() => workOrderGuid(101)))
      api.data.get('maintenance-detail')!.value = {
        success: true,
        data: authoritativeDetail(workOrderGuid(101), 'DEV-CNC-01'),
      }
      await nextTick()
      expect(result.authoritativeWorkOrder.value?.workOrderId).toBe(workOrderGuid(101))

      let resolveDetailRefresh!: () => void
      api.refetches
        .get('maintenance-detail')!
        .mockImplementationOnce(
          () => new Promise<void>((resolve) => (resolveDetailRefresh = resolve)),
        )
      const refreshPromise = result.refresh()
      await nextTick()

      driftScope()
      await nextTick()

      expect(result.authoritativeWorkOrder.value).toBeUndefined()
      expect(api.refetches.get('device-detail')).not.toHaveBeenCalled()
      expect(api.refetches.get('maintenance-identities')).not.toHaveBeenCalled()

      resolveDetailRefresh()
      await refreshPromise
      await nextTick()

      expect(api.refetches.get('maintenance-detail')).toHaveBeenCalledTimes(1)
      expect(api.refetches.get('device-detail')).not.toHaveBeenCalled()
      expect(api.refetches.get('maintenance-identities')).not.toHaveBeenCalled()
      expect(result.workOrder.value).toBeUndefined()
    },
  )

  it('does not manually refetch the new detail enrichments when an old refresh finishes after a route switch', async () => {
    seedPrincipal()
    const routeId = shallowRef(workOrderGuid(101))
    const result = useMaintenanceSelfWorkOrderDetail(routeId)
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderGuid(101), 'DEV-OLD'),
    }
    await nextTick()

    let resolveOldDetailRefresh!: () => void
    api.refetches
      .get('maintenance-detail')!
      .mockImplementationOnce(
        () => new Promise<void>((resolve) => (resolveOldDetailRefresh = resolve)),
      )
    const refreshPromise = result.refresh()
    await nextTick()

    routeId.value = workOrderGuid(102)
    await nextTick()
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderGuid(102), 'DEV-NEW'),
    }
    await nextTick()

    resolveOldDetailRefresh()
    await refreshPromise
    await nextTick()

    expect(result.authoritativeWorkOrder.value?.workOrderId).toBe(workOrderGuid(102))
    expect(api.refetches.get('maintenance-detail')).toHaveBeenCalledTimes(1)
    expect(api.refetches.get('device-detail')).not.toHaveBeenCalled()
    expect(api.refetches.get('maintenance-identities')).not.toHaveBeenCalled()
  })

  it('retries identity enrichment after authoritative detail revalidation', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
    }
    api.errors.get('maintenance-identities')!.value = { status: 502 }
    await nextTick()
    expect(result.identitiesUnavailable.value).toBe(true)

    const calls: string[] = []
    api.refetches.get('maintenance-detail')!.mockImplementationOnce(async () => {
      calls.push('work-order')
    })
    api.refetches.get('maintenance-identities')!.mockImplementationOnce(async () => {
      calls.push('identities')
      api.errors.get('maintenance-identities')!.value = undefined
      api.data.get('maintenance-identities')!.value = {
        users: { 'principal-1': '张维修' },
        teams: {},
      }
    })

    await result.refresh()
    await nextTick()

    expect(calls[0]).toBe('work-order')
    expect(calls).toContain('identities')
    expect(result.identitiesUnavailable.value).toBe(false)
    expect(result.identities.value?.users['principal-1']).toBe('张维修')
  })

  it('hides retained aggregate detail during work-order and device refreshes', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
    }
    await nextTick()
    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
        displayName: '旧设备',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.workOrder.value?.workOrderId).toBe('019f0000-0000-7000-8000-000000000101')
    expect(result.device.value?.displayName).toBe('旧设备')

    api.loading.get('maintenance-detail')!.value = true
    await nextTick()
    expect(result.pending.value).toBe(true)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.workOrder.value).toBeUndefined()
    expect(result.device.value).toBeUndefined()

    api.loading.get('maintenance-detail')!.value = false
    api.errors.get('maintenance-detail')!.value = { status: 403 }
    await nextTick()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.workOrder.value).toBeUndefined()

    api.errors.get('maintenance-detail')!.value = undefined
    api.loading.get('device-detail')!.value = true
    await nextTick()
    expect(result.pending.value).toBe(true)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.workOrder.value).toBeUndefined()
    expect(result.device.value).toBeUndefined()

    api.loading.get('device-detail')!.value = false
    api.data.get('device-detail')!.value = { success: false, data: null }
    await nextTick()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.workOrder.value).toBeUndefined()
  })
})
