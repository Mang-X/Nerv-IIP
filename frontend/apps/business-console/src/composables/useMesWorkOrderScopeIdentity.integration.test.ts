import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMesWorkOrderDetail, useMesWorkOrders } from './useBusinessMes'

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

const sdkState = vi.hoisted(() => ({
  workContextRequests: [] as Array<{
    query: WorkContextQuery
    resolve: (value: unknown) => void
  }>,
  workOrderListRequests: [] as Array<{
    query: ScopedListQuery
    resolve: (value: unknown) => void
  }>,
  workOrderDetailRequests: [] as Array<{
    options: ScopedDetailOptions
    resolve: (value: unknown) => void
  }>,
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  const detailKey = (options: ScopedDetailOptions) => [
    'pc-work-order-detail',
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
          'pc-work-order-context',
          query.organizationId,
          query.environmentId,
          query.permissionCode,
        ],
        query: () =>
          new Promise((resolve) => {
            sdkState.workContextRequests.push({ query, resolve })
          }),
      }),
    ),
    getBusinessConsoleMesMaterialReadinessQueryOptions: vi.fn(
      ({ path, query }: ScopedDetailOptions) => ({
        key: [
          'pc-work-order-material-readiness',
          query.organizationId,
          query.environmentId,
          path.workOrderId,
        ],
        query: async () => ({ success: true, data: { status: 'ready' } }),
      }),
    ),
    getBusinessConsoleMesWorkOrderDetailQueryOptions: vi.fn((options: ScopedDetailOptions) => ({
      key: detailKey(options),
      query: () =>
        new Promise((resolve) => {
          sdkState.workOrderDetailRequests.push({ options, resolve })
        }),
    })),
    listBusinessConsoleMesWorkOrdersQueryOptions: vi.fn(
      ({ query }: { query: ScopedListQuery }) => ({
        key: [
          'pc-work-order-list',
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

async function createHarness() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore(pinia)
  auth.principal = {
    principalId: 'user-a',
    organizationId: 'org-001',
    environmentId: 'env-dev',
  } as never
  useBusinessContextStore(pinia).patchContext({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
  let listLastUpdatedAt: Readonly<{ value: string | null }> | undefined

  const Harness = defineComponent({
    setup() {
      const list = useMesWorkOrders()
      const detail = useMesWorkOrderDetail()
      detail.filters.workOrderId = 'WO-SHARED'
      listLastUpdatedAt = list.workOrdersLastUpdatedAt
      return () =>
        h(
          'div',
          [
            list.workOrderReadScope.value?.id ?? 'no-scope',
            list.workOrders.value[0]?.workOrderId ?? 'no-row',
            list.workOrdersTotal.value,
            list.workOrdersLastUpdatedAt.value ? 'fresh' : 'not-fresh',
            detail.detail.value?.skuId ?? 'no-detail',
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
  }
}

describe('PC MES work-order principal scope identity', () => {
  beforeEach(() => {
    sdkState.workContextRequests.length = 0
    sdkState.workOrderListRequests.length = 0
    sdkState.workOrderDetailRequests.length = 0
    localStorage.clear()
  })

  it('keeps current rows, total, freshness, and detail when old scope responses arrive late', async () => {
    const { auth, wrapper, listLastUpdatedAt } = await createHarness()
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
    expect(wrapper.text()).toBe('no-scope|no-row|0|not-fresh|no-detail')

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
    currentDetailRequest.resolve({
      success: true,
      data: {
        workOrderId: 'WO-SHARED',
        skuId: 'SKU-B',
        quantity: 1,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: [],
      },
    })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-B|WO-B|1|fresh|SKU-B')
    const currentFreshness = listLastUpdatedAt()
    expect(currentFreshness).not.toBeNull()

    await new Promise((resolve) => setTimeout(resolve, 5))
    oldListRequest.resolve({
      success: true,
      data: {
        items: [{ workOrderId: 'WO-A-LATE', status: 'InProgress' }],
        total: 99,
      },
    })
    oldDetailRequest.resolve({
      success: true,
      data: {
        workOrderId: 'WO-SHARED',
        skuId: 'SKU-A-LATE',
        quantity: 9,
        status: 'InProgress',
        readinessStatus: 'blocked',
        blockingReasons: ['旧主体迟到响应'],
        operationTasks: [],
      },
    })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-B|WO-B|1|fresh|SKU-B')
    expect(listLastUpdatedAt()).toBe(currentFreshness)
  })
})
