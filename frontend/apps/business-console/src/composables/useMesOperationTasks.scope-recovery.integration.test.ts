import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMesOperationTasks } from './useBusinessMes'
import MesWorkScopeSelect from '@/components/mes/MesWorkScopeSelect.vue'

type WorkContextQuery = {
  organizationId: string
  environmentId: string
  permissionCode: string
  scopeKind?: string
  scopeId?: string
}

type ScopedListQuery = {
  organizationId: string
  environmentId: string
  scopeKind?: string
  scopeId?: string
}

const sdkState = vi.hoisted(() => ({
  workContextRequests: [] as Array<{
    query: WorkContextQuery
    resolve: (value: unknown) => void
    reject: (reason: unknown) => void
  }>,
  operationTaskListRequests: [] as Array<{
    query: ScopedListQuery
    resolve: (value: unknown) => void
  }>,
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  return {
    ...actual,
    getBusinessConsolePrincipalWorkContextQueryOptions: vi.fn(
      ({ query }: { query: WorkContextQuery }) => ({
        // 与真实 codegen 一致：query 参数（含 scopeKind/scopeId）参与查询键，
        // 选择变化必须触发一次新的核验请求。
        key: [
          'scope-selection-work-context',
          query.organizationId,
          query.environmentId,
          query.permissionCode,
          query.scopeKind ?? '',
          query.scopeId ?? '',
        ],
        query: () =>
          new Promise((resolve, reject) => {
            sdkState.workContextRequests.push({ query, resolve, reject })
          }),
      }),
    ),
    listBusinessConsoleMesOperationTasksQueryOptions: vi.fn(
      ({ query }: { query: ScopedListQuery }) => ({
        key: [
          'operation-recovery-list',
          query.organizationId,
          query.environmentId,
          query.scopeKind ?? '',
          query.scopeId ?? '',
        ],
        query: () =>
          new Promise((resolve) => {
            sdkState.operationTaskListRequests.push({ query, resolve })
          }),
      }),
    ),
  }
})

const ORGANIZATION_SCOPE = {
  kind: 'organization',
  id: 'org-001',
  displayName: '当前组织',
}
const WORK_CENTER_SCOPE = {
  kind: 'work-center',
  id: 'WC-B',
  displayName: '精加工二线',
}

function readContextRequests() {
  return sdkState.workContextRequests.filter(
    (request) => request.query.permissionCode === 'business.mes.operations.read',
  )
}

function resolveReadContext(
  index: number,
  payload: {
    authorizedScopes: Array<{ kind: string; id: string; displayName?: string }>
    selectedScope?: { kind: string; id: string; displayName?: string } | null
  },
) {
  const request = readContextRequests()[index]
  expect(request).toBeDefined()
  request!.resolve({
    success: true,
    data: {
      authorizedScopes: payload.authorizedScopes,
      selectedScope: payload.selectedScope ?? null,
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

  let list: ReturnType<typeof useMesOperationTasks> | undefined
  const Harness = defineComponent({
    setup() {
      list = useMesOperationTasks()
      return () =>
        h('div', [
          h('button', { onClick: () => list!.refreshOperationTasks() }, '刷新'),
          h(MesWorkScopeSelect, {
            permissionCode: 'business.mes.operations.read',
            readFailed: !!list!.operationListScopeError.value,
            readPending: list!.operationListScopePending.value,
          }),
          h(
            'span',
            [
              list!.operationListScope.value?.id ?? 'no-scope',
              list!.operationListScopeReady.value ? 'ready' : 'not-ready',
            ].join('|'),
          ),
          ...list!.operationTasks.value.map((task) => h('p', task.operationTaskId ?? '')),
        ])
    },
  })

  const wrapper = mount(Harness, {
    global: {
      plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }]],
    },
  })
  await flushPromises()
  if (!list) throw new Error('工单 composable 未初始化')
  return { wrapper, list }
}

describe('工序任务范围失败恢复', () => {
  beforeEach(() => {
    sdkState.workContextRequests.length = 0
    sdkState.operationTaskListRequests.length = 0
    localStorage.clear()
  })

  it('页内刷新重读失败范围，记住的范围核验成功后以原搜索读取同一任务', async () => {
    const storageKey = 'nerv-iip.business-console.mes-work-scope.v1:user-a:org-001:env-dev'
    localStorage.setItem(storageKey, 'work-center:WC-B')
    const { wrapper, list } = await createHarness()
    list.filters.keyword = 'WO-20261013-000002'
    readContextRequests()[0]!.reject(new DOMException('请求中止', 'AbortError'))
    await flushPromises()
    expect(list.operationListScopeMessage.value).toContain('核验失败')
    expect(wrapper.text()).toContain('范围读取失败')
    expect(wrapper.text()).not.toContain('无可选授权范围')
    expect(sdkState.operationTaskListRequests).toHaveLength(0)

    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(readContextRequests()).toHaveLength(2)
    resolveReadContext(1, { authorizedScopes: [ORGANIZATION_SCOPE, WORK_CENTER_SCOPE] })
    await flushPromises()
    expect(readContextRequests()[2]!.query).toMatchObject({
      scopeKind: 'work-center',
      scopeId: 'WC-B',
    })
    expect(sdkState.operationTaskListRequests).toHaveLength(0)
    resolveReadContext(2, {
      authorizedScopes: [ORGANIZATION_SCOPE, WORK_CENTER_SCOPE],
      selectedScope: WORK_CENTER_SCOPE,
    })
    await flushPromises()
    expect(sdkState.operationTaskListRequests).toHaveLength(1)
    expect(sdkState.operationTaskListRequests[0]!.query).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      scopeKind: 'work-center',
      scopeId: 'WC-B',
      keyword: 'WO-20261013-000002',
    })
    sdkState.operationTaskListRequests[0]!.resolve({
      success: true,
      data: { items: [{ operationTaskId: 'WO-20261013-000002-OP-10' }], total: 1 },
    })
    await flushPromises()
    expect(list.operationTasks.value.map((task) => task.operationTaskId)).toEqual([
      'WO-20261013-000002-OP-10',
    ])
    expect(list.filters.keyword).toBe('WO-20261013-000002')
    expect(localStorage.getItem(storageKey)).toBe('work-center:WC-B')
    expect(list.operationListScopeMessage.value).toBe('')
    wrapper.unmount()
  })

  it('真实无授权范围保持无授权语义且不读取任务', async () => {
    const { wrapper, list } = await createHarness()
    resolveReadContext(0, { authorizedScopes: [] })
    await flushPromises()
    expect(list.operationListScopeMessage.value).toContain('没有已授权的作业范围')
    expect(sdkState.operationTaskListRequests).toHaveLength(0)
    wrapper.unmount()
  })
})
