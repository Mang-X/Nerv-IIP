import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'

// ---- vue-router mock ----------------------------------------------------------
const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ query: {} }),
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
})

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

  it('submits entered measurement values with the inspection record', async () => {
    const wrapper = mount(InspectPage)
    await wrapper.findAll('[data-testid="plan-option"]')[0].trigger('click')
    await wrapper.get('[data-testid="result-pass"]').trigger('click')
    await wrapper.get('[data-testid="measurement-characteristic"]').setValue('bearing-temperature')
    await wrapper.get('[data-testid="measurement-value"]').setValue('65')
    await wrapper.get('[data-testid="measurement-uom"]').setValue('C')
    await wrapper.get('[data-testid="measurement-lower"]').setValue('0')
    await wrapper.get('[data-testid="measurement-upper"]').setValue('70')

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
})
