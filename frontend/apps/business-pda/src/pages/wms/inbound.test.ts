import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
}))

// 真实组合式用真实的 ref/computed，贴合运行时解包行为。
const wmsState = vi.hoisted(() => ({
  filters: { skip: 0, take: 100, status: undefined as string | undefined, keyword: undefined as string | undefined },
  orders: [
    { inboundOrderId: '11111111-1111-1111-1111-111111111111', inboundOrderNo: 'IB-2026-0001', status: 'open', createdAtUtc: '2026-06-11T08:00:00Z' },
    { inboundOrderId: '22222222-2222-2222-2222-222222222222', inboundOrderNo: 'IB-2026-0002', status: 'inProgress', createdAtUtc: '2026-06-11T09:00:00Z' },
  ],
  completeInbound: vi.fn((_inboundOrderId: string, _idempotencyKey: string) => Promise.resolve()),
  completePending: false,
  error: null as unknown,
  pending: false,
}))

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsInbound: () => ({
    filters: wmsState.filters,
    orders: computed(() => wmsState.orders),
    total: computed(() => wmsState.orders.length),
    pending: computed(() => wmsState.pending),
    error: computed(() => wmsState.error),
    refresh: vi.fn(),
    completeInbound: wmsState.completeInbound,
    completePending: computed(() => wmsState.completePending),
  }),
}))

import InboundPage from './inbound.vue'

function resetState() {
  wmsState.filters.keyword = undefined
  wmsState.filters.status = undefined
  wmsState.orders = [
    { inboundOrderId: '11111111-1111-1111-1111-111111111111', inboundOrderNo: 'IB-2026-0001', status: 'open', createdAtUtc: '2026-06-11T08:00:00Z' },
    { inboundOrderId: '22222222-2222-2222-2222-222222222222', inboundOrderNo: 'IB-2026-0002', status: 'inProgress', createdAtUtc: '2026-06-11T09:00:00Z' },
  ]
  wmsState.completePending = false
  wmsState.error = null
  wmsState.pending = false
  wmsState.completeInbound.mockClear()
  push.mockClear()
}

describe('WMS 收货入库', () => {
  beforeEach(() => resetState())

  it('渲染收货单号与中文状态（不出现原始状态码或 GUID）', () => {
    const wrapper = mount(InboundPage)
    const text = wrapper.text()
    expect(text).toContain('IB-2026-0001')
    expect(text).toContain('IB-2026-0002')
    // 中文状态
    expect(text).toContain('待入库')
    expect(text).toContain('入库中')
    // 不暴露工程语言：原始状态码 / GUID
    expect(text).not.toContain('open')
    expect(text).not.toContain('inProgress')
    expect(text).not.toContain('11111111-1111-1111-1111-111111111111')
  })

  it('扫单号写入 filters.keyword', async () => {
    const wrapper = mount(InboundPage)
    const input = wrapper.get('input[placeholder*="单号"]')
    await input.setValue('IB-2026-0002')
    await input.trigger('keydown.enter')
    expect(wmsState.filters.keyword).toBe('IB-2026-0002')
  })

  it('点单 → 抽屉确认 → 以该单 id 与页面生成的稳定 idempotencyKey 调用 completeInbound', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    // BottomSheet 经 reka-ui Portal teleport 到 body，需在 document 中查询。
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm).toBeTruthy()
    confirm.click()
    expect(wmsState.completeInbound).toHaveBeenCalledTimes(1)
    const [id, key] = wmsState.completeInbound.mock.calls[0]
    expect(id).toBe('11111111-1111-1111-1111-111111111111')
    expect(typeof key).toBe('string')
    expect((key as string).length).toBeGreaterThan(0)
    wrapper.unmount()
  })

  it('重试（不重新点单）复用同一 idempotencyKey；重新点单为新操作换新键', async () => {
    // 首次提交失败，停留在抽屉，operationKey 不变。
    wmsState.completeInbound.mockRejectedValueOnce(new Error('lost response'))
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    confirm.click()
    await flushPromises()
    // 重试：不重新点单，直接再次确认。
    confirm.click()
    await flushPromises()
    expect(wmsState.completeInbound).toHaveBeenCalledTimes(2)
    const firstKey = wmsState.completeInbound.mock.calls[0][1]
    const retryKey = wmsState.completeInbound.mock.calls[1][1]
    // 重试复用同一键——丢响应也不会重复入库。
    expect(retryKey).toBe(firstKey)

    // 重试成功 → 进入成功态；点「继续」回列表清空选择与 operationKey。
    const continueBtn = wrapper.findAll('button').find(b => b.text() === '继续')!
    expect(continueBtn).toBeTruthy()
    await continueBtn.trigger('click')

    // 重新点单（新操作）→ 新键。
    await wrapper.findAll('[data-row]')[1].trigger('click')
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()
    expect(wmsState.completeInbound).toHaveBeenCalledTimes(3)
    const newOpKey = wmsState.completeInbound.mock.calls[2][1]
    expect(newOpKey).not.toBe(firstKey)
    wrapper.unmount()
  })

  it('completePending 时确认按钮禁用（防重）', async () => {
    wmsState.completePending = true
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
    wrapper.unmount()
  })

  it('完成后显示成功 Result', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    const result = wrapper.find('[data-result][data-status="success"]')
    expect(result.exists()).toBe(true)
    expect(wrapper.text()).toContain('入库已完成')
    wrapper.unmount()
  })

  it('错误时显示错误横幅', () => {
    wmsState.error = new Error('boom')
    const wrapper = mount(InboundPage)
    expect(wrapper.find('[data-testid="error-banner"]').exists()).toBe(true)
  })

  it('无单据且无错误时显示空态', () => {
    wmsState.orders = []
    const wrapper = mount(InboundPage)
    expect(wrapper.text()).toContain('暂无待收货单据')
  })
})
