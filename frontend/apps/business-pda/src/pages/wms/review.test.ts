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
    { outboundOrderId: '11111111-1111-1111-1111-111111111111', outboundOrderNo: 'OB-2026-0001', status: 'open', createdAtUtc: '2026-06-11T08:00:00Z' },
    { outboundOrderId: '22222222-2222-2222-2222-222222222222', outboundOrderNo: 'OB-2026-0002', status: 'inProgress', createdAtUtc: '2026-06-11T09:00:00Z' },
  ],
  completeOutbound: vi.fn((_outboundOrderId: string, _input: { packReviewNo: string; passed: boolean; idempotencyKey: string }) => Promise.resolve()),
  completePending: false,
  error: null as unknown,
  pending: false,
}))

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsOutbound: () => ({
    filters: wmsState.filters,
    orders: computed(() => wmsState.orders),
    total: computed(() => wmsState.orders.length),
    pending: computed(() => wmsState.pending),
    error: computed(() => wmsState.error),
    refresh: vi.fn(),
    completeOutbound: wmsState.completeOutbound,
    completePending: computed(() => wmsState.completePending),
  }),
}))

import ReviewPage from './review.vue'

function resetState() {
  wmsState.filters.keyword = undefined
  wmsState.filters.status = undefined
  wmsState.orders = [
    { outboundOrderId: '11111111-1111-1111-1111-111111111111', outboundOrderNo: 'OB-2026-0001', status: 'open', createdAtUtc: '2026-06-11T08:00:00Z' },
    { outboundOrderId: '22222222-2222-2222-2222-222222222222', outboundOrderNo: 'OB-2026-0002', status: 'inProgress', createdAtUtc: '2026-06-11T09:00:00Z' },
  ]
  wmsState.completePending = false
  wmsState.error = null
  wmsState.pending = false
  wmsState.completeOutbound.mockClear()
  push.mockClear()
}

describe('WMS 复核发货', () => {
  beforeEach(() => resetState())

  it('渲染出库单号与中文状态（不出现原始状态码或 GUID）', () => {
    const wrapper = mount(ReviewPage)
    const text = wrapper.text()
    expect(text).toContain('OB-2026-0001')
    expect(text).toContain('OB-2026-0002')
    // 中文状态
    expect(text).toContain('待发货')
    expect(text).toContain('发货中')
    // 不暴露工程语言：原始状态码 / GUID
    expect(text).not.toContain('open')
    expect(text).not.toContain('inProgress')
    expect(text).not.toContain('11111111-1111-1111-1111-111111111111')
  })

  it('扫单号写入 filters.keyword', async () => {
    const wrapper = mount(ReviewPage)
    const input = wrapper.get('input[placeholder*="单号"]')
    await input.setValue('OB-2026-0002')
    await input.trigger('keydown.enter')
    expect(wmsState.filters.keyword).toBe('OB-2026-0002')
  })

  it('点单 → 抽屉 → 复核单号未填时确认按钮禁用', async () => {
    const wrapper = mount(ReviewPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm).toBeTruthy()
    expect(confirm.disabled).toBe(true)
    confirm.click()
    expect(wmsState.completeOutbound).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('复核单号仅含空白（"   "）时确认按钮禁用且不调用 completeOutbound', async () => {
    const wrapper = mount(ReviewPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const reviewInput = document.querySelector<HTMLInputElement>('[data-testid="pack-review-no"]')!
    reviewInput.value = '   '
    reviewInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
    confirm.click()
    expect(wmsState.completeOutbound).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('填写复核单号后 → 以该单 id 与 {packReviewNo,passed,idempotencyKey} 调用 completeOutbound', async () => {
    const wrapper = mount(ReviewPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const reviewInput = document.querySelector<HTMLInputElement>('[data-testid="pack-review-no"]')!
    expect(reviewInput).toBeTruthy()
    reviewInput.value = 'PR-1'
    reviewInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(false)
    confirm.click()
    expect(wmsState.completeOutbound).toHaveBeenCalledTimes(1)
    const [id, input] = wmsState.completeOutbound.mock.calls[0] as [string, { packReviewNo: string; passed: boolean; idempotencyKey: string }]
    expect(id).toBe('11111111-1111-1111-1111-111111111111')
    expect(input.packReviewNo).toBe('PR-1')
    expect(input.passed).toBe(true)
    // 页面生成稳定幂等键并随业务字段一并传入。
    expect(typeof input.idempotencyKey).toBe('string')
    expect(input.idempotencyKey.length).toBeGreaterThan(0)
    wrapper.unmount()
  })

  it('重试（不重新点单）复用同一 idempotencyKey；重新点单为新操作换新键', async () => {
    wmsState.completeOutbound.mockRejectedValueOnce(new Error('lost response'))
    const wrapper = mount(ReviewPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const reviewInput = document.querySelector<HTMLInputElement>('[data-testid="pack-review-no"]')!
    reviewInput.value = 'PR-1'
    reviewInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    confirm.click()
    await flushPromises()
    // 重试：不重新点单，直接再次确认。
    confirm.click()
    await flushPromises()
    expect(wmsState.completeOutbound).toHaveBeenCalledTimes(2)
    const firstKey = (wmsState.completeOutbound.mock.calls[0][1] as { idempotencyKey: string }).idempotencyKey
    const retryKey = (wmsState.completeOutbound.mock.calls[1][1] as { idempotencyKey: string }).idempotencyKey
    expect(retryKey).toBe(firstKey)

    // 重试成功 → 进入成功态；点「继续」回列表清空选择与 operationKey。
    const continueBtn = wrapper.findAll('button').find(b => b.text() === '继续')!
    expect(continueBtn).toBeTruthy()
    await continueBtn.trigger('click')

    // 重新点单（新操作）→ 新键。
    await wrapper.findAll('[data-row]')[1].trigger('click')
    const reviewInput2 = document.querySelector<HTMLInputElement>('[data-testid="pack-review-no"]')!
    reviewInput2.value = 'PR-2'
    reviewInput2.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()
    expect(wmsState.completeOutbound).toHaveBeenCalledTimes(3)
    const newOpKey = (wmsState.completeOutbound.mock.calls[2][1] as { idempotencyKey: string }).idempotencyKey
    expect(newOpKey).not.toBe(firstKey)
    wrapper.unmount()
  })

  it('completePending 时确认按钮禁用（防重）', async () => {
    wmsState.completePending = true
    const wrapper = mount(ReviewPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const reviewInput = document.querySelector<HTMLInputElement>('[data-testid="pack-review-no"]')!
    reviewInput.value = 'PR-1'
    reviewInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
    wrapper.unmount()
  })

  it('完成后显示成功 Result', async () => {
    const wrapper = mount(ReviewPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const reviewInput = document.querySelector<HTMLInputElement>('[data-testid="pack-review-no"]')!
    reviewInput.value = 'PR-1'
    reviewInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    const result = wrapper.find('[data-result][data-status="success"]')
    expect(result.exists()).toBe(true)
    expect(wrapper.text()).toContain('出库复核已完成')
    wrapper.unmount()
  })

  it('错误时显示错误横幅', () => {
    wmsState.error = new Error('boom')
    const wrapper = mount(ReviewPage)
    expect(wrapper.find('[data-testid="error-banner"]').exists()).toBe(true)
  })

  it('无单据且无错误时显示空态', () => {
    wmsState.orders = []
    const wrapper = mount(ReviewPage)
    expect(wrapper.text()).toContain('暂无待发货单据')
  })
})
