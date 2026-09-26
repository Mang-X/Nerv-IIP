import { DOMWrapper, flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { PiniaColada } from '@pinia/colada'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import AndonWorkbench from './AndonWorkbench.vue'
import { createMemoryHistory, createRouter, RouterView, type LocationQueryRaw } from 'vue-router'

const api = vi.hoisted(() => ({
  list: vi.fn(),
  claim: vi.fn(),
  success: vi.fn(),
  failure: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  listBusinessConsoleMesAndonCalls: api.list,
  // 工作中心筛选走可搜目录：目录项的 id 与编码不同，用来证明筛选提交的是编码。
  listBusinessConsoleSearchableDirectory: async () => ({
    data: {
      success: true,
      data: {
        items: [
          { id: 'wc-a-id', code: 'WC-A', displayName: '总装一线', context: {} },
          { id: 'wc-b-id', code: 'WC-B', displayName: '总装二线', context: {} },
        ],
        total: 2,
      },
    },
  }),
  claimBusinessConsoleMesAndonCall: api.claim,
  getBusinessConsolePrincipalWorkContextQueryOptions: ({
    query,
  }: {
    query: Record<string, string>
  }) => ({
    key: ['andon-work-context', query],
    query: async () => ({
      success: true,
      data: {
        authorizedScopes: [
          { kind: 'work-center', id: 'WC-A', displayName: '总装一线' },
          { kind: 'work-center', id: 'WC-B', displayName: '总装二线' },
        ],
        selectedScope: query.scopeId ? { kind: query.scopeKind, id: query.scopeId } : null,
      },
    }),
  }),
}))
vi.mock('@/utils/notify', async (original) => ({
  ...(await original<typeof import('@/utils/notify')>()),
  notifySuccess: api.success,
  notifyOperationFailure: api.failure,
}))
const rows = [
  {
    id: 'call-1',
    category: 'equipment',
    status: 'open',
    workOrderId: 'WO-20260920-001',
    operationTaskId: 'WO-20260920-001-OP-20',
    workCenterId: 'WC-ASSEMBLY',
    raisedAtUtc: '2026-09-20T04:00:00Z',
    responseDurationSeconds: null,
  },
  {
    id: 'call-2',
    category: 'quality',
    status: 'claimed',
    workOrderId: 'WO-20260920-002',
    operationTaskId: 'WO-20260920-002-OP-10',
    responderId: 'user:responder-chen',
    responseDurationSeconds: 125,
    escalatedAtUtc: '2026-09-20T04:05:00Z',
    escalationRecipientId: '质量值班员',
  },
]
async function setup(query: LocationQueryRaw = {}) {
  const pinia = createPinia()
  useAuthStore(pinia).$patch({
    principal: {
      principalId: 'responder-chen',
      principalType: 'User',
      permissionCodes: [
        'business.mes.operations.read',
        'business.mes.operations.manage',
        'business.mes.work-orders.read',
      ],
    },
  })
  useBusinessContextStore(pinia).patchContext({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/mes/andon', component: AndonWorkbench },
      { path: '/source', component: { template: '<p>来源工单</p>' } },
    ],
  })
  await router.push({ path: '/mes/andon', query })
  const wrapper = mount(RouterView, {
    global: {
      plugins: [pinia, router, [PiniaColada, { queryOptions: { gcTime: 0 } }]],
    },
  })
  await flushPromises()
  await flushPromises()
  return { wrapper, router }
}
describe('安灯工作台用户行为（#3655）', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.list.mockResolvedValue({ data: { success: true, data: { items: rows, total: 23 } } })
  })
  it('显示首响、真实响应人和升级事实，未响应不显示零秒，并提供来源工序链接', async () => {
    const { wrapper } = await setup()
    expect(wrapper.text()).toContain('未响应')
    expect(wrapper.text()).toContain('125 秒')
    expect(wrapper.text()).toContain('user:responder-chen')
    expect(wrapper.text()).toContain('质量值班员')
    expect(wrapper.text()).toContain('已升级')
    expect(wrapper.findAll('a').some((link) => link.text() === 'WO-20260920-001-OP-20')).toBe(true)
    expect(wrapper.findAll('button').some((button) => button.text() === '关闭呼叫')).toBe(true)
    wrapper.unmount()
  })
  it('读失败呈现重试而非零呼叫空态，重试重新请求', async () => {
    api.list.mockRejectedValueOnce({ status: 503 })
    const { wrapper } = await setup()
    expect(wrapper.text()).toContain('数据加载失败')
    expect(wrapper.text()).not.toContain('暂无安灯呼叫')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '重新加载')!
      .trigger('click')
    await flushPromises()
    expect(api.list).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })
  it('写失败反馈失败且无成功提示', async () => {
    api.claim.mockRejectedValue({ status: 409, message: '呼叫已由其他人员认领' })
    const { wrapper } = await setup()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '认领')!
      .trigger('click')
    await flushPromises()
    expect(api.failure).toHaveBeenCalled()
    expect(api.success).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('URL 恢复队列、分类、工作中心和第二页，来源页往返保留同一筛选', async () => {
    const { wrapper, router } = await setup({
      queue: 'unclosed',
      category: 'equipment',
      workCenterId: 'WC-ASSEMBLY',
      page: '2',
      pageSize: '20',
    })
    expect(api.list).toHaveBeenLastCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          queue: 'unclosed',
          category: 'equipment',
          workCenterId: 'WC-ASSEMBLY',
          skip: 20,
          take: 20,
        }),
      }),
    )
    // 地址栏带入的工作中心不在目录当前页里：选择器仍要显示它并可清除，不能显示成「全部工作中心」。
    const picker = wrapper.get('button[aria-label="工作中心"]')
    expect(picker.text()).toContain('WC-ASSEMBLY')
    expect(wrapper.find('button[aria-label="清除工作中心"]').exists()).toBe(true)
    await router.push('/source')
    router.back()
    await flushPromises()
    await flushPromises()
    expect(wrapper.text()).toContain('待响应与处理中')
    expect(router.currentRoute.value.query.page).toBe('2')
    expect(api.list).toHaveBeenLastCalledWith(
      expect.objectContaining({ query: expect.objectContaining({ skip: 20, take: 20 }) }),
    )
    await wrapper.get('button[aria-label="工作中心"]').trigger('click')
    await flushPromises()
    const option = [...document.body.querySelectorAll('[role="option"]')].find((element) =>
      element.textContent?.includes('总装二线'),
    )!
    await new DOMWrapper(option).trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query).toMatchObject({
      queue: 'unclosed',
      category: 'equipment',
      workCenterId: 'WC-B',
    })
    expect(router.currentRoute.value.query.page).toBeUndefined()
    wrapper.unmount()
  })

  it('筛选无结果提供清空筛选，并按默认队列重新查询', async () => {
    api.list.mockResolvedValue({ data: { success: true, data: { items: [], total: 0 } } })
    const { wrapper, router } = await setup({ category: 'equipment', workCenterId: 'WC-NONE' })
    expect(wrapper.text()).toContain('没有符合条件的安灯呼叫')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '清空筛选')!
      .trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({})
    expect(api.list).toHaveBeenLastCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          queue: 'awaitingResponse',
          category: undefined,
          workCenterId: undefined,
          skip: 0,
        }),
      }),
    )
    wrapper.unmount()
  })
})
