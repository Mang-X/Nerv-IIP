import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { useMesExactOperationTask, useMesWorkOrderDetail, useMesWorkOrders } from './useBusinessMes'

type WorkContextQuery = {
  organizationId: string
  environmentId: string
  permissionCode: string
}

type ScopedListQuery = {
  organizationId: string
  environmentId: string
  scopeKind?: string
  scopeId?: string
}

type ScopedDetailOptions = {
  path: { workOrderId: string }
  query: ScopedListQuery
}

type ExactTaskOptions = {
  query: ScopedListQuery & {
    workOrderId: string
    skip: number
    take: number
  }
  signal?: AbortSignal
}

const sdkState = vi.hoisted(() => ({
  workContextRequests: [] as Array<{
    query: WorkContextQuery
    resolve: (value: unknown) => void
    reject: (reason?: unknown) => void
  }>,
  workOrderListRequests: [] as Array<{
    query: ScopedListQuery
    resolve: (value: unknown) => void
  }>,
  workOrderDetailRequests: [] as Array<{
    options: ScopedDetailOptions
    resolve: (value: unknown) => void
  }>,
  exactTaskRequests: [] as Array<{
    options: ExactTaskOptions
    resolve: (value: unknown) => void
  }>,
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  const detailKey = (options: ScopedDetailOptions) => [
    'pda-work-order-detail',
    options.query.organizationId,
    options.query.environmentId,
    options.query.scopeKind,
    options.query.scopeId,
    options.path.workOrderId,
  ]

  return {
    ...actual,
    getBusinessConsolePrincipalWorkContextQueryOptions: vi.fn(
      ({ query }: { query: WorkContextQuery }) => ({
        key: [
          'pda-work-order-context',
          query.organizationId,
          query.environmentId,
          query.permissionCode,
        ],
        query: () =>
          new Promise((resolve, reject) => {
            sdkState.workContextRequests.push({ query, resolve, reject })
          }),
      }),
    ),
    getBusinessConsoleMesMaterialReadinessQueryOptions: vi.fn(
      ({ path, query }: ScopedDetailOptions) => ({
        key: [
          'pda-work-order-material-readiness',
          query.organizationId,
          query.environmentId,
          path.workOrderId,
        ],
        query: async () => ({ success: true, data: { status: 'ready' } }),
      }),
    ),
    getBusinessConsoleMesWorkOrderDetailQueryKey: vi.fn(detailKey),
    getBusinessConsoleMesWorkOrderDetailQueryOptions: vi.fn((options: ScopedDetailOptions) => ({
      key: detailKey(options),
      query: () =>
        new Promise((resolve) => {
          sdkState.workOrderDetailRequests.push({ options, resolve })
        }),
    })),
    listBusinessConsoleMesReportableOperationTasks: vi.fn(
      (options: ExactTaskOptions) =>
        new Promise((resolve) => {
          sdkState.exactTaskRequests.push({ options, resolve })
        }),
    ),
    listBusinessConsoleMesWorkOrdersQueryOptions: vi.fn(
      ({ query }: { query: ScopedListQuery }) => ({
        key: [
          'pda-work-order-list',
          query.organizationId,
          query.environmentId,
          query.scopeKind,
          query.scopeId,
        ],
        query: () =>
          new Promise((resolve) => {
            sdkState.workOrderListRequests.push({ query, resolve })
          }),
      }),
    ),
  }
})

function resolveWorkContext(permissionCode: string, index: number, scopeId: string) {
  const request = sdkState.workContextRequests.filter(
    (candidate) => candidate.query.permissionCode === permissionCode,
  )[index]
  expect(request).toBeDefined()
  request.resolve({
    success: true,
    data: {
      selectedScope: {
        kind: 'work-center',
        id: scopeId,
        displayName: scopeId === 'WC-A' ? '精加工一线' : '精加工二线',
      },
    },
  })
}

function workOrderDetail(workOrderId: string, skuId: string) {
  return {
    success: true,
    data: {
      workOrderId,
      skuId,
      quantity: 1,
      status: 'Released',
      readinessStatus: 'ready',
      blockingReasons: [],
      operationTasks: [],
    },
  }
}

function operationTask(workCenterId: string) {
  return {
    operationTaskId: 'OP-SHARED',
    workOrderId: 'WO-SHARED',
    status: 'Ready',
    operationSequence: 10,
    workCenterId,
    qualityStatus: 'Pending',
  }
}

async function createWorkOrderHarness() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore(pinia)
  auth.principal = {
    principalId: 'user-a',
    organizationId: 'org-001',
    environmentId: 'env-dev',
  } as never
  const workOrderId = ref('WO-SHARED')
  let listLastUpdatedAt: Readonly<{ value: string | null }> | undefined
  let detailLastUpdatedAt: Readonly<{ value: string | null }> | undefined

  const Harness = defineComponent({
    setup() {
      const list = useMesWorkOrders()
      const detail = useMesWorkOrderDetail(workOrderId)
      listLastUpdatedAt = list.lastUpdatedAt
      detailLastUpdatedAt = detail.lastUpdatedAt
      return () =>
        h(
          'div',
          [
            list.workOrderReadScope.value?.id ?? 'no-scope',
            list.workOrders.value[0]?.workOrderId ?? 'no-row',
            list.total.value,
            list.lastUpdatedAt.value ? 'list-fresh' : 'list-not-fresh',
            detail.workOrder.value?.skuId ?? 'no-detail',
            detail.lastUpdatedAt.value ? 'detail-fresh' : 'detail-not-fresh',
          ].join('|'),
        )
    },
  })

  const wrapper = mount(Harness, {
    global: {
      plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }]],
    },
  })
  await flushPromises()
  return {
    auth,
    wrapper,
    listLastUpdatedAt: () => listLastUpdatedAt?.value ?? null,
    detailLastUpdatedAt: () => detailLastUpdatedAt?.value ?? null,
  }
}

async function createExactTaskHarness() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore(pinia)
  auth.principal = {
    principalId: 'user-a',
    organizationId: 'org-001',
    environmentId: 'env-dev',
  } as never
  const workOrderId = ref('WO-SHARED')
  const operationTaskId = ref('OP-SHARED')
  const detail = ref({
    workOrderId: 'WO-SHARED',
    operationTasks: [],
  })

  const Harness = defineComponent({
    setup() {
      const exact = useMesExactOperationTask(workOrderId, operationTaskId, detail as never)
      return () =>
        h(
          'div',
          [
            exact.reportingReadScope.value?.id ?? 'no-scope',
            exact.task.value?.workCenterId ?? 'no-task',
          ].join('|'),
        )
    },
  })

  const wrapper = mount(Harness, {
    global: {
      plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }]],
    },
  })
  await flushPromises()
  return { auth, wrapper }
}

describe('PDA MES work-order principal scope identity', () => {
  beforeEach(() => {
    sdkState.workContextRequests.length = 0
    sdkState.workOrderListRequests.length = 0
    sdkState.workOrderDetailRequests.length = 0
    sdkState.exactTaskRequests.length = 0
    localStorage.clear()
  })

  it('keeps current list, total, freshness, and detail when old scope responses arrive late', async () => {
    const { auth, wrapper, listLastUpdatedAt, detailLastUpdatedAt } = await createWorkOrderHarness()
    resolveWorkContext('business.mes.work-orders.read', 0, 'WC-A')
    await flushPromises()

    const oldListRequest = sdkState.workOrderListRequests.find(
      (request) => request.query.scopeId === 'WC-A',
    )
    const oldDetailRequest = sdkState.workOrderDetailRequests.find(
      (request) => request.options.query.scopeId === 'WC-A',
    )
    expect(oldListRequest).toBeDefined()
    expect(oldDetailRequest).toBeDefined()
    if (!oldListRequest || !oldDetailRequest) throw new Error('旧主体范围请求未发起')

    auth.principal = {
      principalId: 'user-b',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    } as never
    await flushPromises()
    expect(wrapper.text()).toBe('no-scope|no-row|0|list-not-fresh|no-detail|detail-not-fresh')

    resolveWorkContext('business.mes.work-orders.read', 1, 'WC-B')
    await flushPromises()
    const currentListRequest = sdkState.workOrderListRequests.find(
      (request) => request.query.scopeId === 'WC-B',
    )
    const currentDetailRequest = sdkState.workOrderDetailRequests.find(
      (request) => request.options.query.scopeId === 'WC-B',
    )
    expect(currentListRequest).toBeDefined()
    expect(currentDetailRequest).toBeDefined()
    if (!currentListRequest || !currentDetailRequest) {
      throw new Error('新主体范围请求未发起')
    }
    currentListRequest.resolve({
      success: true,
      data: {
        items: [{ workOrderId: 'WO-B', status: 'Released' }],
        total: 1,
      },
    })
    currentDetailRequest.resolve(workOrderDetail('WO-SHARED', 'SKU-B'))
    await flushPromises()
    expect(wrapper.text()).toBe('WC-B|WO-B|1|list-fresh|SKU-B|detail-fresh')
    const currentListFreshness = listLastUpdatedAt()
    const currentDetailFreshness = detailLastUpdatedAt()
    expect(currentListFreshness).not.toBeNull()
    expect(currentDetailFreshness).not.toBeNull()

    await new Promise((resolve) => setTimeout(resolve, 5))
    oldListRequest.resolve({
      success: true,
      data: {
        items: [{ workOrderId: 'WO-A-LATE', status: 'InProgress' }],
        total: 99,
      },
    })
    oldDetailRequest.resolve(workOrderDetail('WO-SHARED', 'SKU-A-LATE'))
    await flushPromises()
    expect(wrapper.text()).toBe('WC-B|WO-B|1|list-fresh|SKU-B|detail-fresh')
    expect(listLastUpdatedAt()).toBe(currentListFreshness)
    expect(detailLastUpdatedAt()).toBe(currentDetailFreshness)
  })

  it('keeps the current reporting-read exact task when the old principal response arrives late', async () => {
    const { auth, wrapper } = await createExactTaskHarness()
    resolveWorkContext('business.mes.reporting.read', 0, 'WC-A')
    await flushPromises()
    const oldRequest = sdkState.exactTaskRequests.find(
      (request) => request.options.query.scopeId === 'WC-A',
    )
    expect(oldRequest).toBeDefined()
    if (!oldRequest) throw new Error('旧主体精确任务请求未发起')

    auth.principal = {
      principalId: 'user-b',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    } as never
    await flushPromises()
    expect(wrapper.text()).toBe('no-scope|no-task')

    resolveWorkContext('business.mes.reporting.read', 1, 'WC-B')
    await flushPromises()
    const currentRequest = sdkState.exactTaskRequests.find(
      (request) => request.options.query.scopeId === 'WC-B',
    )
    expect(currentRequest).toBeDefined()
    if (!currentRequest) throw new Error('新主体精确任务请求未发起')
    currentRequest.resolve({
      data: {
        success: true,
        data: { items: [operationTask('WC-B')], total: 1 },
      },
    })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-B|WC-B')

    oldRequest.resolve({
      data: {
        success: true,
        data: { items: [operationTask('WC-A-LATE')], total: 1 },
      },
    })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-B|WC-B')
  })

  it.each(['missing', 'failed'] as const)(
    'does not send an exact-task request when reporting-read scope is %s',
    async (state) => {
      const { wrapper } = await createExactTaskHarness()
      const workContextRequest = sdkState.workContextRequests.find(
        (request) => request.query.permissionCode === 'business.mes.reporting.read',
      )
      expect(workContextRequest).toBeDefined()
      if (!workContextRequest) throw new Error('报工读取范围请求未发起')

      if (state === 'missing') {
        workContextRequest.resolve({ success: true, data: { selectedScope: null } })
      } else {
        workContextRequest.reject(new Error('范围服务不可用'))
      }
      await flushPromises()

      expect(sdkState.exactTaskRequests).toHaveLength(0)
      expect(wrapper.text()).toBe('no-scope|no-task')
    },
  )
})
