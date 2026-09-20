import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { PiniaColada } from '@pinia/colada'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import AndonWorkbench from './AndonWorkbench.vue'

const api = vi.hoisted(() => ({
  list: vi.fn(),
  claim: vi.fn(),
  success: vi.fn(),
  failure: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  listBusinessConsoleMesAndonCalls: api.list,
  claimBusinessConsoleMesAndonCall: api.claim,
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
    responderId: '班长陈工',
    responseDurationSeconds: 125,
    escalatedAtUtc: '2026-09-20T04:05:00Z',
    escalationRecipientId: '质量值班员',
  },
]
async function setup() {
  const pinia = createPinia()
  useAuthStore(pinia).$patch({
    principal: {
      principalId: '班长陈工',
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
  const wrapper = mount(AndonWorkbench, {
    global: {
      plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 0 } }]],
      stubs: { RouterLink: { props: ['to'], template: '<a :href="to.path"><slot /></a>' } },
    },
  })
  await flushPromises()
  return wrapper
}
describe('安灯工作台用户行为（#3655）', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.list.mockResolvedValue({ data: { success: true, data: { items: rows, total: 23 } } })
  })
  it('显示首响、真实响应人和升级事实，未响应不显示零秒，并提供来源工序链接', async () => {
    const wrapper = await setup()
    expect(wrapper.text()).toContain('未响应')
    expect(wrapper.text()).toContain('125 秒')
    expect(wrapper.text()).toContain('班长陈工')
    expect(wrapper.text()).toContain('质量值班员')
    expect(wrapper.text()).toContain('已升级')
    expect(wrapper.findAll('a').some((link) => link.text() === 'WO-20260920-001-OP-20')).toBe(true)
    expect(wrapper.findAll('button').some((button) => button.text() === '关闭呼叫')).toBe(true)
    wrapper.unmount()
  })
  it('读失败呈现重试而非零呼叫空态，重试重新请求', async () => {
    api.list.mockRejectedValueOnce({ status: 503 })
    const wrapper = await setup()
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
    const wrapper = await setup()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '认领')!
      .trigger('click')
    await flushPromises()
    expect(api.failure).toHaveBeenCalled()
    expect(api.success).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
