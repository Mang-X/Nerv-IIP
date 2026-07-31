import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter, useRoute } from 'vue-router'

import { useMesExactOperationTask, useMesWorkOrderDetail } from './useBusinessMes'
import { useMesReportIdentity } from './useMesReportIdentity'
import { useAuthStore } from '@/stores/auth'

type DetailEnvelope = {
  success: boolean
  data: {
    workOrderId: string
    skuId: string
    quantity: number
    status: string
    readinessStatus: string
    blockingReasons: string[]
    operationTasks: Task[]
  }
}

type Task = {
  operationTaskId: string
  workOrderId: string
  status: 'Ready'
  operationSequence: number
  workCenterId: string
  qualityStatus: string
}

const sdkState = vi.hoisted(() => ({
  detailRequests: [] as Array<{
    options: {
      path: { workOrderId: string }
      query: {
        organizationId: string
        environmentId: string
        scopeKind?: string
        scopeId?: string
      }
    }
    signal: AbortSignal
    resolve: (value: DetailEnvelope) => void
  }>,
  exactRequests: [] as Array<{
    options: {
      query: {
        organizationId: string
        environmentId: string
        workOrderId: string
        scopeKind?: string
        scopeId?: string
        skip: number
        take: number
      }
      signal: AbortSignal
    }
    resolve: (value: unknown) => void
  }>,
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  const detailKey = (options: {
    path: { workOrderId: string }
    query: {
      organizationId: string
      environmentId: string
      scopeKind?: string
      scopeId?: string
    }
  }) => [
    'integration-work-order-detail',
    options.query.organizationId,
    options.query.environmentId,
    options.query.scopeKind,
    options.query.scopeId,
    options.path.workOrderId,
  ]

  return {
    ...actual,
    getBusinessConsolePrincipalWorkContextQueryOptions: vi.fn(
      ({
        query,
      }: {
        query: {
          organizationId: string
          environmentId: string
          permissionCode: string
        }
      }) => ({
        key: [
          'integration-principal-work-context',
          query.organizationId,
          query.environmentId,
          query.permissionCode,
        ],
        query: async () => ({
          success: true,
          data: {
            selectedScope: {
              kind: 'work-center',
              id: query.organizationId === 'org-001' ? 'WC-1' : 'WC-2',
            },
          },
        }),
      }),
    ),
    getBusinessConsoleMesMaterialReadinessQueryOptions: vi.fn(
      ({
        path,
        query,
      }: {
        path: { workOrderId: string }
        query: { organizationId: string; environmentId: string }
      }) => ({
        key: [
          'integration-material-readiness',
          query.organizationId,
          query.environmentId,
          path.workOrderId,
        ],
        query: async () => ({ success: true, data: { status: 'ready' } }),
      }),
    ),
    getBusinessConsoleMesWorkOrderDetailQueryKey: vi.fn(detailKey),
    getBusinessConsoleMesWorkOrderDetailQueryOptions: vi.fn((options) => ({
      key: detailKey(options),
      query: ({ signal }: { signal: AbortSignal }) =>
        new Promise<DetailEnvelope>((resolve) => {
          sdkState.detailRequests.push({ options, signal, resolve })
        }),
    })),
    listBusinessConsoleMesReportableOperationTasks: vi.fn(
      (options: {
        query: {
          organizationId: string
          environmentId: string
          workOrderId: string
          scopeKind?: string
          scopeId?: string
          skip: number
          take: number
        }
        signal: AbortSignal
      }) =>
        new Promise((resolve) => {
          sdkState.exactRequests.push({ options, resolve })
        }),
    ),
  }
})

function task(workOrderId: string, sequence: number): Task {
  return {
    operationTaskId: `OP-${sequence}`,
    workOrderId,
    status: 'Ready',
    operationSequence: sequence,
    workCenterId: 'WC-1',
    qualityStatus: 'Pending',
  }
}

function detail(workOrderId: string, tasks: Task[]): DetailEnvelope {
  return {
    success: true,
    data: {
      workOrderId,
      skuId: `SKU-${workOrderId}`,
      quantity: 1,
      status: 'Released',
      readinessStatus: 'ready',
      blockingReasons: [],
      operationTasks: tasks,
    },
  }
}

async function createHarness(initialUrl: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/mes/report', component: { render: () => h('div') } }],
  })
  await router.push(initialUrl)
  await router.isReady()
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'user-001',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    },
  } as never)

  const Harness = defineComponent({
    setup() {
      const route = useRoute()
      const workOrderId = computed(() => String(route.query.workOrderId ?? '').trim())
      const operationTaskId = computed(() => String(route.query.operationTaskId ?? '').trim())
      const detailAuthority = useMesWorkOrderDetail(workOrderId)
      const exactAuthority = useMesExactOperationTask(
        workOrderId,
        operationTaskId,
        detailAuthority.workOrder,
      )
      const identity = useMesReportIdentity({
        workOrderDetail: detailAuthority.workOrder,
        workOrderDetailPending: detailAuthority.pending,
        workOrderDetailError: detailAuthority.error,
        exactOperationTask: exactAuthority.task,
        exactOperationTaskPending: exactAuthority.pending,
        exactOperationTaskError: exactAuthority.error,
        exactOperationTaskScopeReady: exactAuthority.reportingReadScopeReady,
        exactOperationTaskScopeMessage: exactAuthority.reportingReadScopeMessage,
      })
      return () =>
        h(
          'div',
          identity.pair.value
            ? `${identity.pair.value.workOrderId}/${identity.pair.value.operationTaskId}`
            : (identity.routeIssue.value ?? 'pending'),
        )
    },
  })

  const wrapper = mount(Harness, {
    global: {
      plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }], router],
    },
  })
  await flushPromises()
  return { auth, router, wrapper }
}

describe('MES report identity real router + Colada integration', () => {
  beforeEach(() => {
    sdkState.detailRequests.length = 0
    sdkState.exactRequests.length = 0
    localStorage.clear()
  })

  it('uses the production detail key/signal, cancels the old route, and ignores its late response', async () => {
    const { router, wrapper } = await createHarness(
      '/mes/report?workOrderId=WO-A&operationTaskId=OP-1',
    )
    expect(sdkState.detailRequests).toHaveLength(1)
    const requestA = sdkState.detailRequests[0]

    await router.push('/mes/report?workOrderId=WO-B&operationTaskId=OP-2')
    await flushPromises()
    expect(requestA.signal.aborted).toBe(true)
    const requestB = sdkState.detailRequests.find(
      (request) => request.options.path.workOrderId === 'WO-B',
    )!
    requestB.resolve(detail('WO-B', [task('WO-B', 2)]))
    await flushPromises()
    expect(wrapper.text()).toBe('WO-B/OP-2')

    requestA.resolve(detail('WO-A', [task('WO-A', 1)]))
    await flushPromises()
    expect(wrapper.text()).toBe('WO-B/OP-2')

    router.back()
    await flushPromises()
    expect(router.currentRoute.value.query).toMatchObject({
      workOrderId: 'WO-A',
      operationTaskId: 'OP-1',
    })
    // WO-A's first response was discarded with its cancelled request, so going
    // back refetches; the identity must follow the URL once that lands.
    const backRequestA = sdkState.detailRequests
      .filter((request) => request.options.path.workOrderId === 'WO-A')
      .at(-1)!
    expect(backRequestA).not.toBe(requestA)
    backRequestA.resolve(detail('WO-A', [task('WO-A', 1)]))
    await flushPromises()
    expect(wrapper.text()).toBe('WO-A/OP-1')

    router.forward()
    await flushPromises()
    expect(router.currentRoute.value.query).toMatchObject({
      workOrderId: 'WO-B',
      operationTaskId: 'OP-2',
    })
    // WO-B is served straight from the query cache — no second request is made,
    // so the identity has to rebind from the cached response alone.
    expect(
      sdkState.detailRequests.filter((request) => request.options.path.workOrderId === 'WO-B'),
    ).toHaveLength(1)
    expect(wrapper.text()).toBe('WO-B/OP-2')
  })

  it('resolves task 501 with bounded pages and cancels old exact keys on route/scope changes', async () => {
    const { auth, router, wrapper } = await createHarness(
      '/mes/report?workOrderId=WO-501&operationTaskId=OP-501',
    )
    sdkState.detailRequests[0].resolve(
      detail(
        'WO-501',
        Array.from({ length: 500 }, (_, index) => task('WO-501', index + 1)),
      ),
    )
    await flushPromises()

    for (let page = 0; page < 6; page += 1) {
      const request = sdkState.exactRequests[page]
      expect(request).toBeDefined()
      expect(request.options.query).toMatchObject({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        workOrderId: 'WO-501',
        skip: page * 100,
        take: 100,
      })
      const items =
        page === 5
          ? [task('WO-501', 501)]
          : Array.from({ length: 100 }, (_, index) => task('WO-501', page * 100 + index + 1))
      request.resolve({ data: { success: true, data: { items } } })
      await flushPromises()
    }
    expect(wrapper.text()).toBe('WO-501/OP-501')

    await router.push('/mes/report?workOrderId=WO-501&operationTaskId=OP-999')
    await flushPromises()
    const oldRouteRequest = sdkState.exactRequests.at(-1)!
    expect(oldRouteRequest.options.signal.aborted).toBe(false)
    await router.push('/mes/report?workOrderId=WO-501&operationTaskId=OP-998')
    await flushPromises()
    expect(oldRouteRequest.options.signal.aborted).toBe(true)

    const oldScopeRequest = sdkState.exactRequests.at(-1)!
    expect(oldScopeRequest).not.toBe(oldRouteRequest)
    expect(oldScopeRequest.options.signal.aborted).toBe(false)
    auth.principal = {
      principalId: 'user-001',
      organizationId: 'org-002',
      environmentId: 'env-prod',
    } as never
    await flushPromises()
    expect(oldScopeRequest.options.signal.aborted).toBe(true)
    const newScopeDetail = sdkState.detailRequests.find(
      (request) => request.options.query.organizationId === 'org-002',
    )!
    expect(newScopeDetail).toBeDefined()
    newScopeDetail.resolve(
      detail(
        'WO-501',
        Array.from({ length: 500 }, (_, index) => task('WO-501', index + 1)),
      ),
    )
    await flushPromises()
    expect(sdkState.exactRequests.at(-1)?.options.query).toMatchObject({
      organizationId: 'org-002',
      environmentId: 'env-prod',
      workOrderId: 'WO-501',
      take: 100,
    })
  })
})
