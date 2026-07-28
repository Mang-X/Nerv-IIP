import { flushPromises, mount } from '@vue/test-utils'
import { NvBottomSheet } from '@nerv-iip/ui-mobile'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import { RequestTimeoutError } from '@/api/request-timeout'

const push = vi.fn()
const routeGuardState = vi.hoisted(() => ({
  guard: undefined as (() => boolean) | undefined,
}))
vi.mock('vue-router', () => ({
  onBeforeRouteLeave: vi.fn((guard: () => boolean) => {
    routeGuardState.guard = guard
  }),
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
}))

// 真实组合式用真实的 ref/computed，贴合运行时解包行为。
const wmsState = vi.hoisted(() => ({
  filters: {
    skip: 0,
    take: 100,
    status: undefined as string | undefined,
    keyword: undefined as string | undefined,
    locationCode: undefined as string | undefined,
  },
  executions: [
    {
      countExecutionId: '11111111-1111-1111-1111-111111111111',
      countNo: 'CT-2026-0001',
      skuCode: 'SKU-A',
      uomCode: 'EA',
      siteCode: 'S1',
      locationCode: 'A-01',
      expectedQuantity: 10,
      status: 'Open',
      createdAtUtc: '2026-06-11T08:00:00Z',
    },
    {
      countExecutionId: '22222222-2222-2222-2222-222222222222',
      countNo: 'CT-2026-0002',
      skuCode: 'SKU-B',
      uomCode: 'EA',
      siteCode: 'S1',
      locationCode: 'A-02',
      expectedQuantity: 5,
      status: 'Completed',
      createdAtUtc: '2026-06-11T09:00:00Z',
    },
  ],
  completeCount: vi.fn(
    (
      _countExecutionId: string,
      _input: { countedQuantity: number; idempotencyKey: string },
      options?: { attempt: 'initial' | 'retry'; onCommandAttempt?: () => void },
    ) => {
      options?.onCommandAttempt?.()
      return Promise.resolve()
    },
  ),
  completePending: false,
  error: null as unknown,
  pending: false,
  refresh: vi.fn(async () => {}),
}))

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsCount: () => ({
    filters: wmsState.filters,
    executions: computed(() => wmsState.executions),
    total: computed(() => wmsState.executions.length),
    pending: computed(() => wmsState.pending),
    error: computed(() => wmsState.error),
    refresh: wmsState.refresh,
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
    {
      countExecutionId: '11111111-1111-1111-1111-111111111111',
      countNo: 'CT-2026-0001',
      skuCode: 'SKU-A',
      uomCode: 'EA',
      siteCode: 'S1',
      locationCode: 'A-01',
      expectedQuantity: 10,
      status: 'Open',
      createdAtUtc: '2026-06-11T08:00:00Z',
    },
    {
      countExecutionId: '22222222-2222-2222-2222-222222222222',
      countNo: 'CT-2026-0002',
      skuCode: 'SKU-B',
      uomCode: 'EA',
      siteCode: 'S1',
      locationCode: 'A-02',
      expectedQuantity: 5,
      status: 'Completed',
      createdAtUtc: '2026-06-11T09:00:00Z',
    },
  ]
  wmsState.completePending = false
  wmsState.error = null
  wmsState.pending = false
  wmsState.completeCount.mockClear()
  wmsState.refresh.mockClear()
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
    expect(text).toContain('已完成')
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

  it('填写实盘数后 → 以该执行 id 与 {countedQuantity,idempotencyKey} 调用 completeCount', async () => {
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
    const [id, input] = wmsState.completeCount.mock.calls[0] as [
      string,
      { countedQuantity: number; idempotencyKey: string },
    ]
    expect(id).toBe('11111111-1111-1111-1111-111111111111')
    expect(input.countedQuantity).toBe(8)
    // 页面生成稳定幂等键并随实盘数一并传入。
    expect(typeof input.idempotencyKey).toBe('string')
    expect(input.idempotencyKey.length).toBeGreaterThan(0)
    wrapper.unmount()
  })

  it('重试（不重新点任务）复用同一 idempotencyKey；重新点任务为新操作换新键', async () => {
    wmsState.completeCount.mockImplementationOnce(
      (_id: string, _input: unknown, options?: { onCommandAttempt?: () => void }) => {
        options?.onCommandAttempt?.()
        return Promise.reject(new Error('lost response'))
      },
    )
    const wrapper = mount(CountPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const countInput = document.querySelector<HTMLInputElement>('[data-testid="counted-quantity"]')!
    countInput.value = '8'
    countInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    confirm.click()
    await flushPromises()
    // 重试：不重新点任务，直接再次确认。
    confirm.click()
    await flushPromises()
    expect(wmsState.completeCount).toHaveBeenCalledTimes(2)
    const firstKey = (wmsState.completeCount.mock.calls[0][1] as { idempotencyKey: string })
      .idempotencyKey
    const retryKey = (wmsState.completeCount.mock.calls[1][1] as { idempotencyKey: string })
      .idempotencyKey
    expect(retryKey).toBe(firstKey)

    // 重试成功 → 进入成功态；点「继续」回列表清空选择与 operationKey。
    const continueBtn = wrapper.findAll('button').find((b) => b.text() === '继续')!
    expect(continueBtn).toBeTruthy()
    await continueBtn.trigger('click')

    // 重新点任务（新操作）→ 新键。
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const countInput2 = document.querySelector<HTMLInputElement>(
      '[data-testid="counted-quantity"]',
    )!
    countInput2.value = '3'
    countInput2.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()
    expect(wmsState.completeCount).toHaveBeenCalledTimes(3)
    const newOpKey = (wmsState.completeCount.mock.calls[2][1] as { idempotencyKey: string })
      .idempotencyKey
    expect(newOpKey).not.toBe(firstKey)
    wrapper.unmount()
  })

  it('确定性 422 后编辑实盘数会轮换 key，并按 initial 新意图提交', async () => {
    wmsState.completeCount.mockImplementationOnce(
      (_id: string, _input: unknown, options?: { onCommandAttempt?: () => void }) => {
        options?.onCommandAttempt?.()
        return Promise.reject({ success: false, statusCode: 422, message: '实盘数无效' })
      },
    )
    const wrapper = mount(CountPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const countInput = document.querySelector<HTMLInputElement>('[data-testid="counted-quantity"]')!
    countInput.value = '8'
    countInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    confirm.click()
    await flushPromises()

    const firstKey = (wmsState.completeCount.mock.calls[0][1] as { idempotencyKey: string })
      .idempotencyKey
    countInput.value = '9'
    countInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    confirm.click()
    await flushPromises()

    const secondKey = (wmsState.completeCount.mock.calls[1][1] as { idempotencyKey: string })
      .idempotencyKey
    expect(secondKey).not.toBe(firstKey)
    expect(wmsState.completeCount.mock.calls[1][2]).toMatchObject({ attempt: 'initial' })
    wrapper.unmount()
  })

  it('结果未知时所有关闭入口都保留冻结盘点意图', async () => {
    wmsState.completeCount.mockImplementationOnce(
      (_id: string, _input: unknown, options?: { onCommandAttempt?: () => void }) => {
        options?.onCommandAttempt?.()
        return Promise.reject(new RequestTimeoutError())
      },
    )
    const wrapper = mount(CountPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const countInput = document.querySelector<HTMLInputElement>('[data-testid="counted-quantity"]')!
    countInput.value = '8'
    countInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    const sheet = wrapper.findComponent(NvBottomSheet)
    sheet.vm.$emit('update:open', false)
    await wrapper.vm.$nextTick()
    expect(sheet.props('open')).toBe(true)
    const cancel = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === '取消',
    )
    expect(cancel?.disabled).toBe(true)
    expect(countInput.disabled).toBe(true)
    expect(routeGuardState.guard?.()).toBe(false)
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

  it('409 后刷新并关闭旧抽屉、清除过期选择', async () => {
    wmsState.completeCount.mockRejectedValueOnce({
      success: false,
      message: 'lifecycle-conflict',
    })
    const wrapper = mount(CountPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const countInput = document.querySelector<HTMLInputElement>('[data-testid="counted-quantity"]')!
    countInput.value = '8'
    countInput.dispatchEvent(new Event('input', { bubbles: true }))
    await wrapper.vm.$nextTick()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    expect(wmsState.refresh).toHaveBeenCalledTimes(1)
    expect(document.querySelector('[data-testid="confirm-complete"]')).toBeNull()
    expect(document.body.textContent).toContain('状态已被其他操作更新')
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
