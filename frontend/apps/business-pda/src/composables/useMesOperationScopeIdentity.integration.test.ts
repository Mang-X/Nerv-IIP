import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { useMesOperationTasks } from './useBusinessMes'

type WorkContextQuery = {
  organizationId: string
  environmentId: string
  permissionCode: string
}

type OperationListQuery = {
  organizationId: string
  environmentId: string
  scopeKind?: string
  scopeId?: string
  keyword?: string
  workOrderId?: string
}

const sdkState = vi.hoisted(() => ({
  workContextRequests: [] as Array<{
    query: WorkContextQuery
    resolve: (value: unknown) => void
  }>,
  operationListRequests: [] as Array<{
    query: OperationListQuery
    resolve: (value: unknown) => void
  }>,
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  return {
    ...actual,
    getBusinessConsolePrincipalWorkContextQueryOptions: vi.fn(
      ({ query }: { query: WorkContextQuery }) => ({
        key: [
          'operation-scope-work-context',
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
    listBusinessConsoleMesOperationTasksQueryOptions: vi.fn(
      ({ query }: { query: OperationListQuery }) => ({
        key: [
          'operation-scope-list',
          query.organizationId,
          query.environmentId,
          query.scopeKind,
          query.scopeId,
        ],
        query: () =>
          new Promise((resolve) => {
            sdkState.operationListRequests.push({ query, resolve })
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

  let harnessTasks: ReturnType<typeof useMesOperationTasks> | undefined
  const Harness = defineComponent({
    setup() {
      const tasks = useMesOperationTasks()
      harnessTasks = tasks
      return () =>
        h(
          'div',
          [
            tasks.operationListScope.value?.id ?? 'no-scope',
            tasks.operationTasks.value[0]?.operationTaskId ?? 'no-row',
            tasks.total.value,
            tasks.lastUpdatedAt.value ? 'fresh' : 'not-fresh',
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
  if (!harnessTasks) throw new Error('工序任务 harness 未初始化')
  return { auth, wrapper, tasks: harnessTasks }
}

describe('PDA MES operation-list principal scope identity', () => {
  beforeEach(() => {
    sdkState.workContextRequests.length = 0
    sdkState.operationListRequests.length = 0
    localStorage.clear()
  })

  it('ignores an old principal/scope response that arrives after a same-org switch', async () => {
    const { auth, wrapper } = await createHarness()
    resolveWorkContext('business.mes.operations.read', 0, 'WC-A')
    resolveWorkContext('business.mes.operations.manage', 0, 'WC-A')
    await flushPromises()

    const oldRequest = sdkState.operationListRequests.find(
      (request) => request.query.scopeId === 'WC-A',
    )
    expect(oldRequest).toBeDefined()
    if (!oldRequest) throw new Error('旧主体范围请求未发起')

    auth.principal = {
      principalId: 'user-b',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    } as never
    await flushPromises()
    expect(wrapper.text()).toContain('no-row|0|not-fresh')

    resolveWorkContext('business.mes.operations.read', 1, 'WC-B')
    resolveWorkContext('business.mes.operations.manage', 1, 'WC-B')
    await flushPromises()

    const currentRequest = sdkState.operationListRequests.find(
      (request) => request.query.scopeId === 'WC-B',
    )
    expect(currentRequest).toBeDefined()
    if (!currentRequest) throw new Error('新主体范围请求未发起')
    currentRequest.resolve({
      success: true,
      data: {
        items: [{ operationTaskId: 'OP-B', workOrderId: 'WO-B', status: 'Queued' }],
        total: 1,
      },
    })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-B|OP-B|1|fresh')

    oldRequest.resolve({
      success: true,
      data: {
        items: [{ operationTaskId: 'OP-A-LATE', workOrderId: 'WO-A', status: 'InProgress' }],
        total: 99,
      },
    })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-B|OP-B|1|fresh')
  })

  it('ignores an old same-scope response after the route pair filters change', async () => {
    const { wrapper, tasks } = await createHarness()
    resolveWorkContext('business.mes.operations.read', 0, 'WC-A')
    resolveWorkContext('business.mes.operations.manage', 0, 'WC-A')
    await flushPromises()

    const oldRequest = sdkState.operationListRequests.find(
      (request) => !request.query.workOrderId && !request.query.keyword,
    )
    expect(oldRequest).toBeDefined()
    if (!oldRequest) throw new Error('旧筛选请求未发起')

    tasks.filters.workOrderId = 'WO-B'
    tasks.filters.keyword = 'OP-B'
    await flushPromises()

    const currentRequest = sdkState.operationListRequests.find(
      (request) => request.query.workOrderId === 'WO-B' && request.query.keyword === 'OP-B',
    )
    expect(currentRequest).toBeDefined()
    if (!currentRequest) throw new Error('新双强 ID 筛选请求未发起')
    currentRequest.resolve({
      success: true,
      data: {
        items: [{ operationTaskId: 'OP-B', workOrderId: 'WO-B', status: 'Queued' }],
        total: 1,
      },
    })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-A|OP-B|1|fresh')

    oldRequest.resolve({
      success: true,
      data: {
        items: [{ operationTaskId: 'OP-A-LATE', workOrderId: 'WO-A', status: 'InProgress' }],
        total: 99,
      },
    })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-A|OP-B|1|fresh')
  })
})
