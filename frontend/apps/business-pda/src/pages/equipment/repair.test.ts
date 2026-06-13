import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'

// ---- vue-router mock（默认无 query；个别用例覆写 useRoute）---------------------
const push = vi.fn()
const route = { query: {} as Record<string, string> }
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => route,
}))

// ---- useBusinessMaintenance mock ----------------------------------------------
const createWorkOrder = vi.fn(async (_input: Record<string, unknown>) => ({}))
const createPending = ref(false)
const workOrders = ref<Array<Record<string, unknown>>>([
  {
    workOrderId: '11111111-1111-1111-1111-111111111111',
    deviceAssetId: 'DEV-1001',
    priority: 'high',
    status: 'open',
    openedAtUtc: '2026-06-10T08:00:00Z',
  },
  {
    workOrderId: '22222222-2222-2222-2222-222222222222',
    deviceAssetId: 'DEV-2002',
    priority: 'low',
    status: 'completed',
    openedAtUtc: '2026-06-09T10:30:00Z',
  },
])
const workOrdersError = ref<unknown>(null)
const workOrdersPending = ref(false)
const refreshWorkOrders = vi.fn(async () => {})

vi.mock('@/composables/useBusinessMaintenance', () => ({
  useBusinessMaintenance: () => ({
    workOrders,
    workOrdersTotal: computed(() => workOrders.value.length),
    workOrdersPending,
    workOrdersError,
    refreshWorkOrders,
    workOrderFilters: { skip: 0, take: 100 },
    createWorkOrder,
    createPending,
  }),
}))

import RepairPage from './repair.vue'

beforeEach(() => {
  push.mockClear()
  createWorkOrder.mockClear()
  createWorkOrder.mockResolvedValue({})
  refreshWorkOrders.mockClear()
  route.query = {}
  createPending.value = false
  workOrdersError.value = null
  workOrdersPending.value = false
})

describe('PDA equipment repair page', () => {
  it('renders recent maintenance work orders with Chinese priority + status', () => {
    const wrapper = mount(RepairPage)
    const text = wrapper.text()
    expect(text).toContain('DEV-1001')
    expect(text).toContain('高') // priority high
    expect(text).toContain('待处理') // status open
    expect(text).toContain('DEV-2002')
    expect(text).toContain('已完成') // status completed
  })

  it('shows the empty state when there are no work orders', async () => {
    const original = workOrders.value
    workOrders.value = []
    const wrapper = mount(RepairPage)
    expect(wrapper.text()).toContain('暂无维修工单')
    workOrders.value = original
  })

  it('surfaces a work-orders error banner', () => {
    workOrdersError.value = new Error('boom')
    const wrapper = mount(RepairPage)
    expect(wrapper.find('[data-testid="work-orders-error"]').exists()).toBe(true)
  })

  it('submits a new repair via createWorkOrder WITHOUT org/env/openedBy', async () => {
    const wrapper = mount(RepairPage)
    await wrapper.get('[data-testid="device-input"]').setValue('DEV-9')
    await wrapper.get('[data-testid="priority-select"]').setValue('high')
    await wrapper.get('[data-testid="reason-input"]').setValue('主轴异响')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledTimes(1)
    const body = createWorkOrder.mock.calls[0][0]
    expect(body).toEqual({
      deviceAssetId: 'DEV-9',
      priority: 'high',
      assetUnavailableReason: '主轴异响',
    })
    expect(body).not.toHaveProperty('organizationId')
    expect(body).not.toHaveProperty('environmentId')
    expect(body).not.toHaveProperty('openedBy')
  })

  it('sets deviceAssetId from a ScanBar scan', async () => {
    const wrapper = mount(RepairPage)
    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-SCAN-7')
    await scanInput.trigger('keydown.enter')

    expect((wrapper.get('[data-testid="device-input"]').element as HTMLInputElement).value).toBe('DEV-SCAN-7')
  })

  it('disables submit while createPending (double-submit guard)', async () => {
    createPending.value = true
    const wrapper = mount(RepairPage)
    await wrapper.get('[data-testid="device-input"]').setValue('DEV-9')
    await wrapper.get('[data-testid="priority-select"]').setValue('high')
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeDefined()
  })

  it('disables submit until deviceAssetId + priority present', async () => {
    const wrapper = mount(RepairPage)
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-testid="device-input"]').setValue('DEV-9')
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-testid="priority-select"]').setValue('medium')
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeUndefined()
  })

  it('shows a success Result after a successful submit', async () => {
    const wrapper = mount(RepairPage)
    await wrapper.get('[data-testid="device-input"]').setValue('DEV-9')
    await wrapper.get('[data-testid="priority-select"]').setValue('high')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    const result = wrapper.find('[data-result][data-status="success"]')
    expect(result.exists()).toBe(true)
    expect(wrapper.text()).toContain('报修已提交')
  })

  it('shows an error Result with retry when submit fails', async () => {
    createWorkOrder.mockRejectedValueOnce(new Error('网络错误'))
    const wrapper = mount(RepairPage)
    await wrapper.get('[data-testid="device-input"]').setValue('DEV-9')
    await wrapper.get('[data-testid="priority-select"]').setValue('high')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
  })

  it('prefills deviceAssetId + sourceAlarmId from the route query (from alarms page)', async () => {
    route.query = { deviceAssetId: 'DEV-1', sourceAlarmId: 'ALM-9' }
    const wrapper = mount(RepairPage)
    expect((wrapper.get('[data-testid="device-input"]').element as HTMLInputElement).value).toBe('DEV-1')
    // sourceAlarmId is carried through to the submit body
    await wrapper.get('[data-testid="priority-select"]').setValue('high')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledWith({
      deviceAssetId: 'DEV-1',
      priority: 'high',
      assetUnavailableReason: '',
      sourceAlarmId: 'ALM-9',
    })
  })
})
