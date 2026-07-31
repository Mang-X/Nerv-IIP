import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMesWorkOrders } from './useBusinessMes'

/**
 * #1288 作业范围就绪链路（root cause 回归）。
 *
 * 后端 work-context 按设计不替用户选范围：不带 scopeKind/scopeId 只回授权清单
 * （`selectedScope` 恒空）。此前前端从不带选择参数、也没有选择入口，导致 scope gate
 * 把所有 MES 查询永久拦在 enabled=false——工单列表/详情整页拒载、待排池渲染
 * 「当前没有待排产的工单」假空态。
 *
 * 本文件守住三件事：
 * 1. 拿到授权清单后自动选择（记住的选择优先，否则第一项），并带 scopeKind/scopeId
 *    重新核验，核验通过后列表查询真正发出；
 * 2. localStorage 记住的选择在仍被授权时优先生效；
 * 3. 授权清单确实为空时明确说「没有已授权的作业范围」，绝不发列表查询。
 */

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
  }>,
  workOrderListRequests: [] as Array<{
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
          new Promise((resolve) => {
            sdkState.workContextRequests.push({ query, resolve })
          }),
      }),
    ),
    listBusinessConsoleMesWorkOrdersQueryOptions: vi.fn(
      ({ query }: { query: ScopedListQuery }) => ({
        key: [
          'scope-selection-work-order-list',
          query.organizationId,
          query.environmentId,
          query.scopeKind ?? '',
          query.scopeId ?? '',
        ],
        query: () =>
          new Promise((resolve) => {
            sdkState.workOrderListRequests.push({ query, resolve })
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
    (request) => request.query.permissionCode === 'business.mes.work-orders.read',
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

  let list: ReturnType<typeof useMesWorkOrders> | undefined
  const Harness = defineComponent({
    setup() {
      list = useMesWorkOrders()
      return () =>
        h(
          'div',
          [
            list!.workOrderReadScope.value?.id ?? 'no-scope',
            list!.workOrderReadScopeReady.value ? 'ready' : 'not-ready',
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
  if (!list) throw new Error('工单 composable 未初始化')
  return { wrapper, list }
}

describe('PC MES work scope selection readiness (#1288)', () => {
  beforeEach(() => {
    sdkState.workContextRequests.length = 0
    sdkState.workOrderListRequests.length = 0
    localStorage.clear()
  })

  it('拿到授权清单后自动选择第一项并重核验，核验通过后列表查询才发出', async () => {
    const { wrapper, list } = await createHarness()

    // 首次请求不带选择参数（此时还不知道授权清单）。
    expect(readContextRequests()).toHaveLength(1)
    expect(readContextRequests()[0]!.query.scopeKind).toBeUndefined()
    expect(sdkState.workOrderListRequests).toHaveLength(0)
    expect(wrapper.text()).toBe('no-scope|not-ready')

    // 授权清单返回但未选择：自动选择第一项并带 scopeKind/scopeId 重新核验。
    resolveReadContext(0, { authorizedScopes: [ORGANIZATION_SCOPE], selectedScope: null })
    await flushPromises()
    expect(readContextRequests()).toHaveLength(2)
    expect(readContextRequests()[1]!.query.scopeKind).toBe('organization')
    expect(readContextRequests()[1]!.query.scopeId).toBe('org-001')
    // 核验响应到达前，gate 仍关闭、列表查询不发。
    expect(sdkState.workOrderListRequests).toHaveLength(0)

    // 服务端核验通过（selectedScope 回填）：gate 打开，列表查询带范围参数发出。
    resolveReadContext(1, {
      authorizedScopes: [ORGANIZATION_SCOPE],
      selectedScope: ORGANIZATION_SCOPE,
    })
    await flushPromises()
    expect(wrapper.text()).toBe('org-001|ready')
    expect(list.workOrderReadScopeMessage.value).toBe('')
    expect(sdkState.workOrderListRequests).toHaveLength(1)
    expect(sdkState.workOrderListRequests[0]!.query.scopeKind).toBe('organization')
    expect(sdkState.workOrderListRequests[0]!.query.scopeId).toBe('org-001')
  })

  it('记住的选择仍被授权时优先于清单第一项', async () => {
    localStorage.setItem(
      'nerv-iip.business-console.mes-work-scope.v1:user-a:org-001:env-dev',
      'work-center:WC-B',
    )
    await createHarness()

    resolveReadContext(0, {
      authorizedScopes: [ORGANIZATION_SCOPE, WORK_CENTER_SCOPE],
      selectedScope: null,
    })
    await flushPromises()
    expect(readContextRequests()).toHaveLength(2)
    expect(readContextRequests()[1]!.query.scopeKind).toBe('work-center')
    expect(readContextRequests()[1]!.query.scopeId).toBe('WC-B')
  })

  it('授权清单为空时明确提示没有已授权的作业范围，且绝不发列表查询', async () => {
    const { wrapper, list } = await createHarness()

    resolveReadContext(0, { authorizedScopes: [], selectedScope: null })
    await flushPromises()

    // 没有可选范围：不再发第二次核验请求，也不发列表查询。
    expect(readContextRequests()).toHaveLength(1)
    expect(sdkState.workOrderListRequests).toHaveLength(0)
    expect(wrapper.text()).toBe('no-scope|not-ready')
    expect(list.workOrderReadScopeMessage.value).toContain('没有已授权的作业范围')
  })
})
