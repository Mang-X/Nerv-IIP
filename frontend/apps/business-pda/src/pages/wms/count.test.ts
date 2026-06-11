import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
}))

// 真实组合式用真实的 ref/computed，贴合运行时解包行为。
const wmsState = vi.hoisted(() => ({
  filters: { skip: 0, take: 100, status: undefined as string | undefined, keyword: undefined as string | undefined, locationCode: undefined as string | undefined },
  executions: [
    { countExecutionId: '11111111-1111-1111-1111-111111111111', countNo: 'CT-2026-0001', skuCode: 'SKU-A', uomCode: 'EA', siteCode: 'S1', locationCode: 'A-01', expectedQuantity: 10, status: 'pending', createdAtUtc: '2026-06-11T08:00:00Z' },
    { countExecutionId: '22222222-2222-2222-2222-222222222222', countNo: 'CT-2026-0002', skuCode: 'SKU-B', uomCode: 'EA', siteCode: 'S1', locationCode: 'A-02', expectedQuantity: 5, status: 'inprogress', createdAtUtc: '2026-06-11T09:00:00Z' },
  ],
  completeCount: vi.fn(() => Promise.resolve()),
  completePending: false,
  error: null as unknown,
  pending: false,
}))

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsCount: () => ({
    filters: wmsState.filters,
    executions: computed(() => wmsState.executions),
    total: computed(() => wmsState.executions.length),
    pending: computed(() => wmsState.pending),
    error: computed(() => wmsState.error),
    refresh: vi.fn(),
    completeCount: wmsState.completeCount,
    completePending: computed(() => wmsState.completePending),
  }),
}))

import CountPage from './count.vue'

function resetState() {
  wmsState.filters.keyword = undefined
  wmsState.filters.status = undefined
  wmsState.filters.locationCode = undefined
  wmsState.executions = [
    { countExecutionId: '11111111-1111-1111-1111-111111111111', countNo: 'CT-2026-0001', skuCode: 'SKU-A', uomCode: 'EA', siteCode: 'S1', locationCode: 'A-01', expectedQuantity: 10, status: 'pending', createdAtUtc: '2026-06-11T08:00:00Z' },
    { countExecutionId: '22222222-2222-2222-2222-222222222222', countNo: 'CT-2026-0002', skuCode: 'SKU-B', uomCode: 'EA', siteCode: 'S1', locationCode: 'A-02', expectedQuantity: 5, status: 'inprogress', createdAtUtc: '2026-06-11T09:00:00Z' },
  ]
  wmsState.completePending = false
  wmsState.error = null
  wmsState.pending = false
  wmsState.completeCount.mockClear()
  push.mockClear()
}

describe('WMS 盘点', () => {
  beforeEach(() => resetState())

  it('渲染盘点号、SKU、库位、预期数与中文状态（不出现原始状态码或 GUID）', () => {
    const wrapper = mount(CountPage)
    const text = wrapper.text()
    expect(text).toContain('CT-2026-0001')
    expect(text).toContain('CT-2026-0002')
    expect(text).toContain('SKU-A')
    expect(text).toContain('A-01')
    expect(text).toContain('10')
    // 中文状态
    expect(text).toContain('待盘点')
    expect(text).toContain('盘点中')
    // 不暴露工程语言：原始状态码 / GUID
    expect(text).not.toContain('pending')
    expect(text).not.toContain('inprogress')
    expect(text).not.toContain('11111111-1111-1111-1111-111111111111')
  })

  it('扫库位写入 filters.locationCode', async () => {
    const wrapper = mount(CountPage)
    const input = wrapper.get('input[placeholder*="库位"]')
    await input.setValue('A-02')
    await input.trigger('keydown.enter')
    expect(wmsState.filters.locationCode).toBe('A-02')
  })

  it('点任务 → 抽屉 → 实盘数未填时确认按钮禁用', async () => {
    const wrapper = mount(CountPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm).toBeTruthy()
    expect(confirm.disabled).toBe(true)
    confirm.click()
    expect(wmsState.completeCount).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('填写实盘数后 → 以该执行 id 与 {countedQuantity} 调用 completeCount（不带 idempotencyKey）', async () => {
    const wrapper = mount(CountPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const countInput = document.querySelector<HTMLInputElement>('[data-testid="counted-quantity"]')!
    expect(countInput).toBeTruthy()
    countInput.value = '8'
    countInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(false)
    confirm.click()
    expect(wmsState.completeCount).toHaveBeenCalledTimes(1)
    expect(wmsState.completeCount).toHaveBeenCalledWith(
      '11111111-1111-1111-1111-111111111111',
      { countedQuantity: 8 },
    )
    wrapper.unmount()
  })

  it('实盘数为负时确认按钮禁用', async () => {
    const wrapper = mount(CountPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const countInput = document.querySelector<HTMLInputElement>('[data-testid="counted-quantity"]')!
    countInput.value = '-1'
    countInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
    wrapper.unmount()
  })

  it('completePending 时确认按钮禁用（防重）', async () => {
    wmsState.completePending = true
    const wrapper = mount(CountPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const countInput = document.querySelector<HTMLInputElement>('[data-testid="counted-quantity"]')!
    countInput.value = '8'
    countInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
    wrapper.unmount()
  })

  it('完成后显示成功 Result', async () => {
    const wrapper = mount(CountPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const countInput = document.querySelector<HTMLInputElement>('[data-testid="counted-quantity"]')!
    countInput.value = '8'
    countInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    const result = wrapper.find('[data-result][data-status="success"]')
    expect(result.exists()).toBe(true)
    expect(wrapper.text()).toContain('盘点已提交')
    wrapper.unmount()
  })

  it('错误时显示错误横幅', () => {
    wmsState.error = new Error('boom')
    const wrapper = mount(CountPage)
    expect(wrapper.find('[data-testid="error-banner"]').exists()).toBe(true)
  })

  it('无盘点任务且无错误时显示空态', () => {
    wmsState.executions = []
    const wrapper = mount(CountPage)
    expect(wrapper.text()).toContain('暂无盘点任务')
  })
})
