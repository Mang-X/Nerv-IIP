import { RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

// ---- vue-router mock ----------------------------------------------------------
const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ query: {} }),
}))

// ---- 拍照能力 mock（能力门控 + 采集可控）-------------------------------------
const photoMock = vi.hoisted(() => ({
  supported: { current: false },
  capture: vi.fn(),
  releasePhoto: vi.fn(),
}))
vi.mock('@/composables/useInspectionPhotoCapture', () => ({
  useInspectionPhotoCapture: () => ({
    supported: photoMock.supported.current,
    capture: photoMock.capture,
    releasePhoto: photoMock.releasePhoto,
  }),
}))

// ---- useBusinessMaintenance mock ----------------------------------------------
const recordInspection = vi.fn(async (_input: Record<string, unknown>) => ({}))
const recordPending = ref(false)
const plans = ref<Array<Record<string, unknown>>>([
  {
    planId: 'p1111111-1111-1111-1111-111111111111',
    deviceAssetId: 'DEV-1001',
    planCode: 'PLAN-A',
    interval: 'daily',
    startsOn: '2026-06-01',
  },
  {
    planId: 'p2222222-2222-2222-2222-222222222222',
    deviceAssetId: 'DEV-2002',
    planCode: 'PLAN-B',
    interval: 'weekly',
    startsOn: '2026-06-02',
  },
])
const plansPending = ref(false)
const plansError = ref<unknown>(null)
const plansTotal = ref(2)
const refreshPlans = vi.fn(async () => {})
const loadMorePlans = vi.fn()
const planFilters = { skip: 0, take: 100 }

const inspections = ref<Array<Record<string, unknown>>>([
  {
    inspectionId: 'i1111111-1111-1111-1111-111111111111',
    planId: 'p1111111-1111-1111-1111-111111111111',
    workOrderId: null,
    inspector: 'op-1',
    result: 'pass',
    inspectedAtUtc: '2026-06-10T08:00:00Z',
    measurements: [
      {
        characteristicCode: 'bearing-temperature',
        measuredValue: 65,
        uomCode: 'C',
        lowerSpecLimit: 0,
        upperSpecLimit: 70,
        isWithinSpec: true,
      },
    ],
  },
])
const inspectionsPending = ref(false)
const inspectionsError = ref<unknown>(null)
const refreshInspections = vi.fn(async () => {})
const inspectionFilters = { skip: 0, take: 100 }

vi.mock('@/composables/useBusinessMaintenance', () => ({
  useBusinessMaintenance: () => ({
    plans,
    plansPending,
    plansError,
    plansTotal,
    refreshPlans,
    loadMorePlans,
    planFilters,
    recordInspection,
    recordPending,
    inspections,
    inspectionsPending,
    inspectionsError,
    refreshInspections,
    inspectionFilters,
  }),
}))

import InspectPage from './inspect.vue'

// 数字键盘（Teleport 到 body）：点 Cell 打开键盘，再逐位敲键（需 attachTo document.body）。
async function enterViaKeyboard(
  wrapper: VueWrapper,
  cellTestId: string,
  digits: string,
): Promise<void> {
  await wrapper.get(`[data-testid="${cellTestId}"]`).trigger('click')
  await flushPromises()
  const keyboard = document.querySelector('[data-slot="number-keyboard"]')
  const buttons = Array.from(keyboard?.querySelectorAll('button') ?? []) as HTMLButtonElement[]
  for (const ch of digits) {
    const btn = buttons.find((b) => b.textContent?.trim() === ch)
    btn?.click()
    await flushPromises()
  }
}

async function startMeasuredRow(wrapper: VueWrapper): Promise<void> {
  await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
  await wrapper.get('[data-testid="result-pass"]').trigger('click')
  await wrapper.get('[data-testid="measurement-characteristic"]').setValue('bearing-temperature')
  await wrapper.get('[data-testid="measurement-uom"]').setValue('C')
}

beforeEach(() => {
  push.mockClear()
  recordInspection.mockClear()
  recordInspection.mockResolvedValue({})
  refreshPlans.mockClear()
  loadMorePlans.mockClear()
  refreshInspections.mockClear()
  recordPending.value = false
  plansError.value = null
  plansPending.value = false
  plansTotal.value = 2
  inspectionsError.value = null
  inspectionsPending.value = false
  photoMock.supported.current = false
  photoMock.capture.mockReset()
  photoMock.releasePhoto.mockReset()
})

// 全局 setup.ts 已 enableAutoUnmount(afterEach)：每个 wrapper 用例后自动卸载，
// teleport 到 body 的键盘/对话框随之清理，无需手动 unmount / 清 body。

describe('PDA equipment inspect page', () => {
  it('renders recent inspections with Chinese result + business refs', () => {
    const wrapper = mount(InspectPage)
    const text = wrapper.text()
    expect(text).toContain('通过') // result pass → 通过
    expect(text).toContain('PLAN-A') // planId resolved to business plan code is not available; planId shown
  })

  it('shows the empty state when there are no inspections', () => {
    const original = inspections.value
    inspections.value = []
    const wrapper = mount(InspectPage)
    expect(wrapper.text()).toContain('暂无点检记录')
    inspections.value = original
  })

  it('surfaces an inspections error banner instead of the empty state', () => {
    const original = inspections.value
    inspections.value = []
    inspectionsError.value = new Error('boom')
    const wrapper = mount(InspectPage)
    expect(wrapper.find('[data-testid="inspections-error"]').exists()).toBe(true)
    // 错误态优先于空态：加载失败时不得误显示"暂无点检记录"。
    expect(wrapper.text()).not.toContain('暂无点检记录')
    inspections.value = original
  })

  it('surfaces a plans error banner instead of the empty state', () => {
    const original = plans.value
    plans.value = []
    plansError.value = new Error('boom')
    const wrapper = mount(InspectPage)
    expect(wrapper.find('[data-testid="plans-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无保养计划')
    plans.value = original
  })

  it('filters plans client-side from a ScanBar scan (planCode / deviceAssetId)', async () => {
    const wrapper = mount(InspectPage)
    expect(wrapper.findAll('[data-testid="plan-option"]')).toHaveLength(2)

    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('PLAN-B')
    await scanInput.trigger('keydown.enter')

    const options = wrapper.findAll('[data-testid="plan-option"]')
    expect(options).toHaveLength(1)
    expect(options[0].text()).toContain('PLAN-B')
  })

  it('offers "加载更多" (not a dead-end) when a scan matches nothing on the loaded page but more plans exist server-side', async () => {
    // 2 plans loaded, but server has more (plansTotal > loadedPlans).
    plansTotal.value = 5
    const wrapper = mount(InspectPage)

    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('PLAN-ZZZ') // matches nothing on the loaded page
    await scanInput.trigger('keydown.enter')

    // No dead-end "未找到匹配的保养计划" message.
    expect(wrapper.text()).not.toContain('未找到匹配的保养计划')
    // Honest "loaded N, no match" + a load-more affordance.
    const loadMore = wrapper.find('[data-testid="load-more-plans"]')
    expect(loadMore.exists()).toBe(true)

    await loadMore.trigger('click')
    expect(loadMorePlans).toHaveBeenCalledTimes(1)
  })

  it('shows the definitive "未找到匹配的保养计划" only when all plans are loaded', async () => {
    // All plans loaded (plansTotal === loadedPlans), scan matches nothing.
    plansTotal.value = 2
    const wrapper = mount(InspectPage)

    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('PLAN-ZZZ')
    await scanInput.trigger('keydown.enter')

    expect(wrapper.text()).toContain('未找到匹配的保养计划')
    expect(wrapper.find('[data-testid="load-more-plans"]').exists()).toBe(false)
  })

  it('starts the flow at select-plan: no result options until a plan is chosen', () => {
    const wrapper = mount(InspectPage)
    // plan list is rendered
    expect(wrapper.find('[data-testid="plan-option"]').exists()).toBe(true)
    // result options not shown before a plan is selected
    expect(wrapper.find('[data-testid="result-pass"]').exists()).toBe(false)
  })

  it('reveals result options after selecting a plan, then submits recordInspection({ planId, result }) WITHOUT injected fields', async () => {
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    expect(wrapper.find('[data-testid="result-pass"]').exists()).toBe(true)

    await wrapper.get('[data-testid="result-pass"]').trigger('click')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(recordInspection).toHaveBeenCalledTimes(1)
    const body = recordInspection.mock.calls[0][0]
    expect(body).toEqual({
      planId: 'p1111111-1111-1111-1111-111111111111',
      result: 'pass',
    })
    expect(body).not.toHaveProperty('organizationId')
    expect(body).not.toHaveProperty('environmentId')
    expect(body).not.toHaveProperty('inspector')
    expect(body).not.toHaveProperty('inspectedAtUtc')
  })

  it('submits measurement values entered via the number keyboard with the inspection record', async () => {
    const wrapper = mount(InspectPage, { attachTo: document.body })
    await startMeasuredRow(wrapper)
    await enterViaKeyboard(wrapper, 'measurement-value', '65')
    await enterViaKeyboard(wrapper, 'measurement-lower', '0')
    await enterViaKeyboard(wrapper, 'measurement-upper', '70')

    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(recordInspection).toHaveBeenCalledTimes(1)
    expect(recordInspection.mock.calls[0][0]).toEqual({
      planId: 'p1111111-1111-1111-1111-111111111111',
      result: 'pass',
      measurements: [
        {
          characteristicCode: 'bearing-temperature',
          measuredValue: 65,
          uomCode: 'C',
          lowerSpecLimit: 0,
          upperSpecLimit: 70,
        },
      ],
    })
  })

  it('does not submit a partial measurement row without a measured value', async () => {
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    await wrapper.get('[data-testid="result-pass"]').trigger('click')
    await wrapper.get('[data-testid="measurement-characteristic"]').setValue('bearing-temperature')
    await wrapper.get('[data-testid="measurement-uom"]').setValue('C')

    const submit = wrapper.get('[data-testid="submit"]')
    expect(submit.attributes('disabled')).toBeDefined()
    await submit.trigger('click')
    await flushPromises()

    expect(recordInspection).not.toHaveBeenCalled()
  })

  it('rejects a row whose lower spec limit exceeds the upper (下限≤上限)', async () => {
    const wrapper = mount(InspectPage, { attachTo: document.body })
    await startMeasuredRow(wrapper)
    await enterViaKeyboard(wrapper, 'measurement-value', '65')
    await enterViaKeyboard(wrapper, 'measurement-lower', '90')
    await enterViaKeyboard(wrapper, 'measurement-upper', '10')

    expect(wrapper.find('[data-testid="measurement-error"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeDefined()
  })

  it('flags 超差 in real time and turns the value red only when it exceeds the spec limit', async () => {
    const wrapper = mount(InspectPage, { attachTo: document.body })
    await startMeasuredRow(wrapper)
    await enterViaKeyboard(wrapper, 'measurement-lower', '0')
    await enterViaKeyboard(wrapper, 'measurement-upper', '70')

    // within spec → no warning
    await enterViaKeyboard(wrapper, 'measurement-value', '65')
    expect(wrapper.find('[data-testid="out-of-tolerance"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="measurement-value-text"]').classes()).not.toContain(
      'text-destructive',
    )

    // over the upper limit → immediate warning + red value
    await enterViaKeyboard(wrapper, 'measurement-value', '9') // 65 → 659, well over 70
    expect(wrapper.find('[data-testid="out-of-tolerance"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="measurement-value-text"]').classes()).toContain(
      'text-destructive',
    )
  })

  it('asks for confirmation summarizing 超差 count, then submits on confirm', async () => {
    const wrapper = mount(InspectPage, { attachTo: document.body })
    await startMeasuredRow(wrapper)
    await enterViaKeyboard(wrapper, 'measurement-lower', '0')
    await enterViaKeyboard(wrapper, 'measurement-upper', '70')
    await enterViaKeyboard(wrapper, 'measurement-value', '80')

    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    // 超差先确认，未直接提交。
    expect(recordInspection).not.toHaveBeenCalled()
    const dialog = document.querySelector('[data-slot="mobile-dialog-content"]')
    expect(dialog?.textContent).toContain('1 项测量值超差')

    const confirmBtn = Array.from(dialog?.querySelectorAll('button') ?? []).find(
      (b) => b.textContent?.trim() === '仍要提交',
    ) as HTMLButtonElement | undefined
    confirmBtn?.click()
    await flushPromises()

    expect(recordInspection).toHaveBeenCalledTimes(1)
  })

  it('submits directly without a confirm dialog when nothing is 超差', async () => {
    const wrapper = mount(InspectPage, { attachTo: document.body })
    await startMeasuredRow(wrapper)
    await enterViaKeyboard(wrapper, 'measurement-lower', '0')
    await enterViaKeyboard(wrapper, 'measurement-upper', '70')
    await enterViaKeyboard(wrapper, 'measurement-value', '65')

    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(document.querySelector('[data-slot="mobile-dialog-content"]')).toBeNull()
    expect(recordInspection).toHaveBeenCalledTimes(1)
  })

  it('hides the photo-capture entry when the camera is unavailable', async () => {
    photoMock.supported.current = false
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    await wrapper.get('[data-testid="result-pass"]').trigger('click')
    expect(wrapper.find('[data-testid="capture-photo"]').exists()).toBe(false)
  })

  it('shows the photo entry and attaches a captured photo when the camera is available', async () => {
    photoMock.supported.current = true
    photoMock.capture.mockResolvedValueOnce({
      id: 1,
      url: 'blob:test',
      file: new File([], 'a.jpg'),
      name: 'a.jpg',
    })
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    await wrapper.get('[data-testid="result-pass"]').trigger('click')

    expect(wrapper.find('[data-testid="capture-photo"]').exists()).toBe(true)
    await wrapper.get('[data-testid="capture-photo"]').trigger('click')
    await flushPromises()

    expect(photoMock.capture).toHaveBeenCalled()
    expect(wrapper.find('[data-testid="measurement-photo"]').exists()).toBe(true)
  })

  it('disables submit while recordPending (double-submit guard)', async () => {
    recordPending.value = true
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    await wrapper.get('[data-testid="result-pass"]').trigger('click')
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeDefined()
  })

  it('disables submit until a plan and a result are both chosen', async () => {
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    // plan chosen, result not yet
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-testid="result-pass"]').trigger('click')
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeUndefined()
  })

  it('shows a success Result after a successful submit', async () => {
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    await wrapper.get('[data-testid="result-pass"]').trigger('click')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    const result = wrapper.find('[data-result][data-status="success"]')
    expect(result.exists()).toBe(true)
    expect(wrapper.text()).toContain('点检已记录')
  })

  it('shows an error Result with retry when submit fails', async () => {
    recordInspection.mockRejectedValueOnce(new Error('网络错误'))
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    await wrapper.get('[data-testid="result-pass"]').trigger('click')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
  })

  // P1-2：点检端点无服务端幂等键。超时/离线后结果不确定 → 不给"重试"，改引导核实。
  it('超时（结果不确定）时不给危险重试，改引导核实且绝不自动重提', async () => {
    recordInspection.mockRejectedValueOnce(new RequestTimeoutError())
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    await wrapper.get('[data-testid="result-pass"]').trigger('click')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('网络超时，请检查连接后重试')
    expect(wrapper.text()).toContain('请勿重复提交')
    expect(wrapper.find('[data-testid="retry"]').exists()).toBe(false)
    await wrapper.get('[data-testid="verify-list"]').trigger('click')
    expect(refreshInspections).toHaveBeenCalled()
    // 关键：核实动作不会重提 → recordInspection 仍只调用一次。
    expect(recordInspection).toHaveBeenCalledTimes(1)
  })
})
