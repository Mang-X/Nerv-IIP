import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { useMesWorkOrders, useMesWorkScopeSelection } from './useBusinessMes'

/**
 * PDA MES 作业范围闭环（#1297，Console 侧 #1296 的对称件）。
 *
 * 后端 work-context 不带 scopeKind/scopeId 时只回授权清单、selectedScope 恒空。
 * 这里验证的是完整闭环：授权清单 → 记住的选择/首项 → 带参重核验 → gate 打开后
 * 列表查询才带范围参数发出；以及 fail closed：清单为空时一条列表请求都不发。
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
        // 键必须含选择参数：否则带参重核验会命中不带参的缓存，闭环形同虚设。
        key: [
          'pda-work-context',
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

const authorizedScopes = [
  { kind: 'work-center', id: 'WC-A', displayName: '精加工一线' },
  { kind: 'work-center', id: 'WC-B', displayName: '精加工二线' },
]

function workContextRequests(scopeId?: string) {
  return sdkState.workContextRequests.filter((request) => request.query.scopeId === scopeId)
}

/** 授权清单响应（服务端未带选择参数时的形态：只有候选，没有已选）。 */
function catalogResponse() {
  return { success: true, data: { authorizedScopes, selectedScope: null } }
}

/** 带参重核验通过后的响应：清单照旧 + 服务端回填的已核验选择。 */
function verifiedResponse(scopeId: string) {
  return {
    success: true,
    data: {
      authorizedScopes,
      selectedScope: authorizedScopes.find((scope) => scope.id === scopeId),
    },
  }
}

type Harness = {
  scopeOptions: () => Array<{ label: string; value: string }>
  scopeMessage: () => string
  select: (value: string) => void
}

let harness: Harness | undefined

async function mountHarness() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore(pinia)
  auth.principal = {
    principalId: 'operator-1',
    organizationId: 'org-001',
    environmentId: 'env-dev',
  } as never

  const Harness = defineComponent({
    setup() {
      const list = useMesWorkOrders()
      const selection = useMesWorkScopeSelection('business.mes.work-orders.read')
      harness = {
        scopeOptions: () => selection.scopeOptions.value,
        scopeMessage: () => selection.scopeMessage.value,
        select: (value: string) => {
          selection.scopeSelectionValue.value = value
        },
      }
      return () =>
        h(
          'div',
          [
            list.workOrderReadScope.value?.id ?? 'no-scope',
            list.workOrders.value[0]?.workOrderId ?? 'no-row',
          ].join('|'),
        )
    },
  })

  const wrapper = mount(Harness, {
    global: { plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }]] },
  })
  await flushPromises()
  return wrapper
}

describe('PDA MES 作业范围选择闭环', () => {
  beforeEach(() => {
    sdkState.workContextRequests.length = 0
    sdkState.workOrderListRequests.length = 0
    harness = undefined
    localStorage.clear()
  })

  it('授权清单 → 自动选首项 → 带参重核验 → 列表查询才带范围发出', async () => {
    const wrapper = await mountHarness()

    // 第一跳：不带选择参数，只拿授权清单；此时 gate 关闭，列表一条都不发。
    const catalogRequests = workContextRequests(undefined)
    expect(catalogRequests.length).toBeGreaterThan(0)
    expect(sdkState.workOrderListRequests).toHaveLength(0)
    for (const request of catalogRequests) request.resolve(catalogResponse())
    await flushPromises()

    // 清单进了选择器，且自动兜底选了第一项。
    expect(harness?.scopeOptions()).toEqual([
      { label: '精加工一线（工作中心）', value: 'work-center:WC-A' },
      { label: '精加工二线（工作中心）', value: 'work-center:WC-B' },
    ])

    // 第二跳：带 scopeKind/scopeId 重核验。服务端回填前 gate 仍关闭。
    const verifyRequests = workContextRequests('WC-A')
    expect(verifyRequests.length).toBeGreaterThan(0)
    expect(verifyRequests[0].query.scopeKind).toBe('work-center')
    expect(sdkState.workOrderListRequests).toHaveLength(0)
    for (const request of verifyRequests) request.resolve(verifiedResponse('WC-A'))
    await flushPromises()

    // gate 打开：列表查询带着服务端核验过的范围发出。
    const listRequest = sdkState.workOrderListRequests.at(-1)
    expect(listRequest?.query).toMatchObject({ scopeKind: 'work-center', scopeId: 'WC-A' })
    listRequest?.resolve({ success: true, data: { items: [{ workOrderId: 'WO-1' }], total: 1 } })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-A|WO-1')
    expect(harness?.scopeMessage()).toBe('')
  })

  it('记住的选择仍被授权时优先于清单首项', async () => {
    localStorage.setItem(
      'nerv-iip.business-pda.mes-work-scope.v1:operator-1:org-001:env-dev',
      'work-center:WC-B',
    )
    await mountHarness()

    for (const request of workContextRequests(undefined)) request.resolve(catalogResponse())
    await flushPromises()

    expect(workContextRequests('WC-A')).toHaveLength(0)
    const verifyRequests = workContextRequests('WC-B')
    expect(verifyRequests.length).toBeGreaterThan(0)
  })

  it('显式切换范围：记住选择并按新范围重核验、重取列表', async () => {
    const wrapper = await mountHarness()
    for (const request of workContextRequests(undefined)) request.resolve(catalogResponse())
    await flushPromises()
    for (const request of workContextRequests('WC-A')) request.resolve(verifiedResponse('WC-A'))
    await flushPromises()
    sdkState.workOrderListRequests
      .at(-1)
      ?.resolve({ success: true, data: { items: [{ workOrderId: 'WO-1' }], total: 1 } })
    await flushPromises()

    harness?.select('work-center:WC-B')
    await flushPromises()

    expect(
      localStorage.getItem('nerv-iip.business-pda.mes-work-scope.v1:operator-1:org-001:env-dev'),
    ).toBe('work-center:WC-B')
    const verifyRequests = workContextRequests('WC-B')
    expect(verifyRequests.length).toBeGreaterThan(0)
    for (const request of verifyRequests) request.resolve(verifiedResponse('WC-B'))
    await flushPromises()

    const listRequest = sdkState.workOrderListRequests.at(-1)
    expect(listRequest?.query).toMatchObject({ scopeKind: 'work-center', scopeId: 'WC-B' })
    listRequest?.resolve({ success: true, data: { items: [{ workOrderId: 'WO-2' }], total: 1 } })
    await flushPromises()
    expect(wrapper.text()).toBe('WC-B|WO-2')
  })

  it('一个授权范围都没有时 fail closed：不重核验、不发列表，且说清缺什么', async () => {
    const wrapper = await mountHarness()

    for (const request of workContextRequests(undefined)) {
      request.resolve({ success: true, data: { authorizedScopes: [], selectedScope: null } })
    }
    await flushPromises()

    expect(sdkState.workContextRequests.filter((r) => r.query.scopeId)).toHaveLength(0)
    expect(sdkState.workOrderListRequests).toHaveLength(0)
    expect(harness?.scopeOptions()).toEqual([])
    expect(harness?.scopeMessage()).toContain('没有已授权的作业范围')
    expect(wrapper.text()).toBe('no-scope|no-row')
  })
})
