import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h, nextTick, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import {
  useMaintenanceSelfWorkOrderDetail,
  useMaintenanceSelfWorkOrders,
} from './useMaintenanceSelfWorkOrders'

const sdk = vi.hoisted(() => ({
  list: vi.fn(),
  detail: vi.fn(),
  workers: vi.fn(),
  resourceDetail: vi.fn(),
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  return {
    ...actual,
    listBusinessConsoleMaintenanceWorkOrders: sdk.list,
    getBusinessConsoleMaintenanceWorkOrder: sdk.detail,
    listBusinessConsoleWorkers: sdk.workers,
    getBusinessConsoleMasterDataResourceDetail: sdk.resourceDetail,
  }
})

const WORK_ORDER_ID = '019f0000-0000-7000-8000-000000000401'

function workerResponse(userId = 'principal-1') {
  return {
    data: {
      success: true,
      data: {
        pageIndex: 1,
        pageSize: 1,
        totalCount: 1,
        items: [
          {
            userId,
            employeeNo: 'E-001',
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
  }
}

function detailResponse(
  workOrderId: string,
  deviceAssetId = 'DEV-CNC-01',
  assignedTeamId = 'TEAM-A',
) {
  return {
    data: {
      success: true,
      data: {
        workOrderId,
        deviceAssetId,
        priority: 'high',
        status: 'accepted',
        openedAtUtc: '2026-08-02T01:00:00.000Z',
        version: 7,
        allowedActions: [],
        blockReasons: [],
        lifecycle: [],
        assignedTechnicianUserId: 'principal-1',
        assignedTeamId,
      },
    },
  }
}

function resourceResponse(resourceType: string, code: string) {
  return resourceType === 'device-asset'
    ? {
        data: {
          success: true,
          data: {
            resourceType,
            deviceAssetId: '019f0000-0000-7000-8000-000000000001',
            code,
            displayName: '一号数控机床',
            active: true,
            snapshotVersion: 'device-v1',
            organizationId: 'org-001',
            environmentId: 'env-dev',
          },
        },
      }
    : {
        data: {
          success: true,
          data: {
            resourceType,
            code,
            displayName: '维修一班',
            active: true,
            snapshotVersion: 'team-v1',
            organizationId: 'org-001',
            environmentId: 'env-dev',
          },
        },
      }
}

function resourceCalls(resourceType: string) {
  return sdk.resourceDetail.mock.calls.filter(
    ([request]) => request.path.resourceType === resourceType,
  ).length
}

function listResponse() {
  return {
    data: {
      success: true,
      data: {
        items: [
          {
            workOrderId: WORK_ORDER_ID,
            deviceAssetId: 'DEV-CNC-01',
            priority: 'high',
            status: 'accepted',
            openedAtUtc: '2026-08-02T01:00:00.000Z',
            version: 7,
            assignedTechnicianUserId: 'principal-1',
          },
        ],
        total: 1,
        skip: 0,
        take: 20,
      },
    },
  }
}

function emptyListResponse() {
  return {
    data: {
      success: true,
      data: {
        items: [],
        total: 0,
        skip: 0,
        take: 20,
      },
    },
  }
}

describe('maintenance real Pinia Colada component-scope identity', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    sdk.workers.mockImplementation((request) => workerResponse(request.query.userId))
    sdk.detail.mockImplementation((request) => detailResponse(request.path.workOrderId))
    sdk.resourceDetail.mockImplementation((request) =>
      resourceResponse(request.path.resourceType, request.path.code),
    )
  })

  it('never projects a successful component instance cache into a rebuilt instance that gets 403', async () => {
    sdk.list.mockResolvedValueOnce(listResponse()).mockRejectedValueOnce(new Error('403 Forbidden'))
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore(pinia)
    auth.principal = {
      principalId: 'principal-1',
      principalType: 'User',
      loginName: 'technician01',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.masterdata.resources.read',
      ],
    } as never

    const visible = ref(true)
    const instance = ref(1)
    const Harness = defineComponent({
      setup() {
        const queue = useMaintenanceSelfWorkOrders()
        return () =>
          h(
            'div',
            {
              'data-success': queue.hasSuccessfulResponse.value,
              'data-failed': queue.hasFailedResponse.value,
            },
            queue.items.value[0]?.workOrderId ?? 'no-row',
          )
      },
    })
    const Root = defineComponent({
      setup: () => () => (visible.value ? h(Harness, { key: instance.value }) : null),
    })
    const wrapper = mount(Root, {
      global: {
        plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 5_000 } }]],
      },
    })

    await flushPromises()
    expect(wrapper.text()).toBe(WORK_ORDER_ID)
    expect(wrapper.get('[data-success="true"]')).toBeDefined()

    visible.value = false
    await nextTick()
    instance.value += 1
    visible.value = true
    await nextTick()

    expect(wrapper.text()).toBe('no-row')
    expect(wrapper.text()).not.toContain(WORK_ORDER_ID)
    await flushPromises()
    expect(sdk.list).toHaveBeenCalledTimes(2)
    expect(wrapper.text()).toBe('no-row')
    expect(wrapper.get('[data-failed="true"]')).toBeDefined()
  })

  it.each(['logout', 'principal', 'organization', 'environment', 'permission'] as const)(
    'does not manually refetch a worker after a delayed list refresh crosses %s scope',
    async (drift) => {
      sdk.list.mockResolvedValue(listResponse())
      const pinia = createPinia()
      setActivePinia(pinia)
      const auth = useAuthStore(pinia)
      auth.principal = {
        principalId: 'principal-1',
        principalType: 'User',
        loginName: 'technician01',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionCodes: [
          'business.maintenance.work-orders.read',
          'business.masterdata.resources.read',
        ],
      } as never

      let queue!: ReturnType<typeof useMaintenanceSelfWorkOrders>
      const Harness = defineComponent({
        setup() {
          queue = useMaintenanceSelfWorkOrders()
          return () => h('div', queue.items.value[0]?.workOrderId ?? 'no-row')
        },
      })
      const wrapper = mount(Harness, {
        global: {
          plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 0 } }]],
        },
      })

      await flushPromises()
      expect(wrapper.text()).toBe(WORK_ORDER_ID)
      expect(sdk.workers).toHaveBeenCalledTimes(1)

      let resolveListRefresh!: (response: ReturnType<typeof listResponse>) => void
      sdk.list.mockResolvedValue(emptyListResponse())
      sdk.list.mockImplementationOnce(
        () =>
          new Promise<ReturnType<typeof listResponse>>((resolve) => (resolveListRefresh = resolve)),
      )
      const refreshPromise = queue.refresh()
      await nextTick()
      expect(sdk.list).toHaveBeenCalledTimes(2)

      if (drift === 'logout') {
        auth.principal = undefined
      } else {
        auth.principal = {
          principalId: drift === 'principal' ? 'principal-2' : 'principal-1',
          principalType: 'User',
          loginName: 'technician01',
          organizationId: drift === 'organization' ? 'org-002' : 'org-001',
          environmentId: drift === 'environment' ? 'env-prod' : 'env-dev',
          permissionCodes:
            drift === 'permission'
              ? ['business.maintenance.work-orders.read']
              : ['business.maintenance.work-orders.read', 'business.masterdata.resources.read'],
        } as never
      }
      await nextTick()
      await flushPromises()

      expect(wrapper.text()).toBe('no-row')
      const workerCallsAfterReactiveScopeChange = sdk.workers.mock.calls.length

      resolveListRefresh(listResponse())
      await refreshPromise
      await flushPromises()

      expect(wrapper.text()).toBe('no-row')
      expect(sdk.workers).toHaveBeenCalledTimes(workerCallsAfterReactiveScopeChange)
    },
  )

  it('keeps the principal query stable across list filters and refreshes it once explicitly', async () => {
    sdk.list.mockResolvedValue(listResponse())
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore(pinia)
    auth.principal = {
      principalId: 'principal-1',
      principalType: 'User',
      loginName: 'technician01',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.masterdata.resources.read',
      ],
    } as never

    let queue!: ReturnType<typeof useMaintenanceSelfWorkOrders>
    mount(
      defineComponent({
        setup() {
          queue = useMaintenanceSelfWorkOrders()
          return () => h('div', queue.principalDisplayName.value ?? 'no-principal')
        },
      }),
      {
        global: {
          plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 0 } }]],
        },
      },
    )

    await flushPromises()
    expect(sdk.workers).toHaveBeenCalledTimes(1)

    queue.filters.status = 'accepted'
    await nextTick()
    queue.filters.deviceAssetIds = ['DEV-CNC-01']
    await nextTick()
    for (const keyword of ['轴', '轴承', '轴承温']) {
      queue.filters.keyword = keyword
      await nextTick()
    }
    await flushPromises()

    expect(sdk.workers).toHaveBeenCalledTimes(1)

    await queue.refresh()
    await flushPromises()

    expect(sdk.workers).toHaveBeenCalledTimes(2)
  })

  it('loads a new principal identity for principal organization and environment changes', async () => {
    sdk.list.mockResolvedValue(emptyListResponse())
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore(pinia)
    const principal = (principalId: string, organizationId: string, environmentId: string) =>
      ({
        principalId,
        principalType: 'User',
        loginName: 'technician01',
        organizationId,
        environmentId,
        permissionCodes: [
          'business.maintenance.work-orders.read',
          'business.masterdata.resources.read',
        ],
      }) as never
    auth.principal = principal('principal-1', 'org-001', 'env-dev')

    mount(
      defineComponent({
        setup() {
          const queue = useMaintenanceSelfWorkOrders()
          return () => h('div', queue.principalDisplayName.value ?? 'no-principal')
        },
      }),
      {
        global: {
          plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 0 } }]],
        },
      },
    )

    await flushPromises()
    expect(sdk.workers).toHaveBeenCalledTimes(1)

    auth.principal = principal('principal-2', 'org-001', 'env-dev')
    await flushPromises()
    expect(sdk.workers).toHaveBeenCalledTimes(2)

    auth.principal = principal('principal-2', 'org-002', 'env-dev')
    await flushPromises()
    expect(sdk.workers).toHaveBeenCalledTimes(3)

    auth.principal = principal('principal-2', 'org-002', 'env-prod')
    await flushPromises()
    expect(sdk.workers).toHaveBeenCalledTimes(4)
  })

  it('isolates principal identity across permission loss and recovery', async () => {
    sdk.list.mockResolvedValue(emptyListResponse())
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore(pinia)
    const principal = (permissionCodes: string[]) =>
      ({
        principalId: 'principal-1',
        principalType: 'User',
        loginName: 'technician01',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionCodes,
      }) as never
    auth.principal = principal([
      'business.maintenance.work-orders.read',
      'business.masterdata.resources.read',
    ])

    let queue!: ReturnType<typeof useMaintenanceSelfWorkOrders>
    mount(
      defineComponent({
        setup() {
          queue = useMaintenanceSelfWorkOrders()
          return () => h('div', queue.principalDisplayName.value ?? 'no-principal')
        },
      }),
      {
        global: {
          plugins: [
            pinia,
            [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 300_000 } }],
          ],
        },
      },
    )

    await flushPromises()
    expect(sdk.workers).toHaveBeenCalledTimes(1)

    auth.principal = principal(['business.maintenance.work-orders.read'])
    await flushPromises()
    expect(queue.principalDisplayName.value).toBeUndefined()
    expect(sdk.workers).toHaveBeenCalledTimes(1)

    auth.principal = principal([
      'business.maintenance.work-orders.read',
      'business.masterdata.resources.read',
    ])
    await flushPromises()
    expect(queue.principalDisplayName.value).toBe('张维修')
    expect(sdk.workers).toHaveBeenCalledTimes(2)
  })

  it('refreshes each stable detail enrichment at most once with a stale cache', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore(pinia)
    auth.principal = {
      principalId: 'principal-1',
      principalType: 'User',
      loginName: 'technician01',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.masterdata.resources.read',
      ],
    } as never

    let detail!: ReturnType<typeof useMaintenanceSelfWorkOrderDetail>
    mount(
      defineComponent({
        setup() {
          detail = useMaintenanceSelfWorkOrderDetail(ref(WORK_ORDER_ID))
          return () => h('div', detail.workOrder.value?.workOrderId ?? 'no-detail')
        },
      }),
      {
        global: {
          plugins: [
            pinia,
            [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 300_000 } }],
          ],
        },
      },
    )

    await flushPromises()
    expect(sdk.detail).toHaveBeenCalledTimes(1)
    expect(resourceCalls('device-asset')).toBe(1)
    expect(resourceCalls('team')).toBe(1)
    expect(sdk.workers).toHaveBeenCalledTimes(1)

    await detail.refresh()
    await flushPromises()

    expect(sdk.detail).toHaveBeenCalledTimes(2)
    expect(resourceCalls('device-asset')).toBe(2)
    expect(resourceCalls('team')).toBe(2)
    expect(sdk.workers).toHaveBeenCalledTimes(2)
  })

  it('automatically loads each changed detail enrichment once without an explicit duplicate', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore(pinia)
    auth.principal = {
      principalId: 'principal-1',
      principalType: 'User',
      loginName: 'technician01',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.masterdata.resources.read',
      ],
    } as never

    let detail!: ReturnType<typeof useMaintenanceSelfWorkOrderDetail>
    mount(
      defineComponent({
        setup() {
          detail = useMaintenanceSelfWorkOrderDetail(ref(WORK_ORDER_ID))
          return () => h('div', detail.workOrder.value?.workOrderId ?? 'no-detail')
        },
      }),
      {
        global: {
          plugins: [
            pinia,
            [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 300_000 } }],
          ],
        },
      },
    )

    await flushPromises()
    sdk.detail.mockResolvedValueOnce(detailResponse(WORK_ORDER_ID, 'DEV-CNC-02', 'TEAM-B'))

    await detail.refresh()
    await flushPromises()

    expect(sdk.detail).toHaveBeenCalledTimes(2)
    expect(resourceCalls('device-asset')).toBe(2)
    expect(resourceCalls('team')).toBe(2)
    expect(sdk.workers).toHaveBeenCalledTimes(2)
  })

  it('does not refresh detail enrichments after a failed authoritative refresh', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore(pinia)
    auth.principal = {
      principalId: 'principal-1',
      principalType: 'User',
      loginName: 'technician01',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.masterdata.resources.read',
      ],
    } as never

    let detail!: ReturnType<typeof useMaintenanceSelfWorkOrderDetail>
    mount(
      defineComponent({
        setup() {
          detail = useMaintenanceSelfWorkOrderDetail(ref(WORK_ORDER_ID))
          return () => h('div', detail.workOrder.value?.workOrderId ?? 'no-detail')
        },
      }),
      {
        global: {
          plugins: [
            pinia,
            [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 300_000 } }],
          ],
        },
      },
    )

    await flushPromises()
    const deviceCalls = resourceCalls('device-asset')
    const teamCalls = resourceCalls('team')
    const workerCalls = sdk.workers.mock.calls.length
    sdk.detail.mockRejectedValueOnce(new Error('403 Forbidden'))

    await detail.refresh()
    await flushPromises()

    expect(sdk.detail).toHaveBeenCalledTimes(2)
    expect(resourceCalls('device-asset')).toBe(deviceCalls)
    expect(resourceCalls('team')).toBe(teamCalls)
    expect(sdk.workers).toHaveBeenCalledTimes(workerCalls)
    expect(detail.workOrder.value).toBeUndefined()
  })

  it('does not refresh the new enrichments when an old detail refresh finishes after route drift', async () => {
    const nextWorkOrderId = '019f0000-0000-7000-8000-000000000402'
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore(pinia)
    auth.principal = {
      principalId: 'principal-1',
      principalType: 'User',
      loginName: 'technician01',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.masterdata.resources.read',
      ],
    } as never

    const routeId = ref(WORK_ORDER_ID)
    let detail!: ReturnType<typeof useMaintenanceSelfWorkOrderDetail>
    mount(
      defineComponent({
        setup() {
          detail = useMaintenanceSelfWorkOrderDetail(routeId)
          return () => h('div', detail.workOrder.value?.workOrderId ?? 'no-detail')
        },
      }),
      {
        global: {
          plugins: [
            pinia,
            [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 300_000 } }],
          ],
        },
      },
    )

    await flushPromises()
    let resolveOldRefresh!: (response: ReturnType<typeof detailResponse>) => void
    sdk.detail.mockImplementationOnce(
      () =>
        new Promise<ReturnType<typeof detailResponse>>((resolve) => (resolveOldRefresh = resolve)),
    )
    const oldRefresh = detail.refresh()
    await nextTick()

    routeId.value = nextWorkOrderId
    await flushPromises()
    expect(detail.workOrder.value?.workOrderId).toBe(nextWorkOrderId)
    const deviceCallsAfterRouteDrift = resourceCalls('device-asset')
    const teamCallsAfterRouteDrift = resourceCalls('team')
    const workerCallsAfterRouteDrift = sdk.workers.mock.calls.length

    resolveOldRefresh(detailResponse(WORK_ORDER_ID))
    await oldRefresh
    await flushPromises()

    expect(detail.workOrder.value?.workOrderId).toBe(nextWorkOrderId)
    expect(resourceCalls('device-asset')).toBe(deviceCallsAfterRouteDrift)
    expect(resourceCalls('team')).toBe(teamCallsAfterRouteDrift)
    expect(sdk.workers).toHaveBeenCalledTimes(workerCallsAfterRouteDrift)
  })
})
