import {
  route,
  createWorkOrder,
  createPending,
  workOrders,
  workOrdersError,
  workOrdersRefreshing,
  selectPriority,
  selectReason,
  resetFixture,
} from './repair.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { NvScanBar } from '@nerv-iip/ui-mobile'
import RepairPage from './repair.vue'

describe('PDA equipment repair page', () => {
  beforeEach(resetFixture)

  it('把分页器的真实刷新生命周期绑定给任务列表壳', async () => {
    const wrapper = mount(RepairPage)

    expect(wrapper.getComponent({ name: 'TaskListShell' }).props('refreshing')).toBe(false)
    workOrdersRefreshing.value = true
    await wrapper.vm.$nextTick()
    expect(wrapper.getComponent({ name: 'TaskListShell' }).props('refreshing')).toBe(true)
  })

  it('describes only the fields that the maintenance keyword query really searches', () => {
    const wrapper = mount(RepairPage)

    const searchbox = wrapper.get('input[aria-label="维修工单关键字"]')
    const placeholder = searchbox.attributes('placeholder')
    expect(placeholder).toBe('搜索设备、来源或负责人')
    expect(placeholder).not.toContain('工单')
    expect(wrapper.find('select[data-testid="work-order-status"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="work-order-status"]').text()).toContain('全部状态')
  })

  it.each([
    ['high', '高'],
    ['medium', '中'],
    ['low', '低'],
  ])('uses ActionSheet and round-trips priority %s', async (priority, label) => {
    route.query = { deviceAssetId: 'DEV-ROUTE-1' }
    const wrapper = mount(RepairPage, { attachTo: document.body })

    expect(wrapper.get('[data-testid="device-trigger"]').text()).not.toContain('由报警发起报修')
    expect(wrapper.find('[data-testid="priority-select"]').exists()).toBe(false)
    await selectPriority(wrapper, label as '高' | '中' | '低')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledWith({
      deviceAssetId: 'DEV-ROUTE-1',
      priority,
      assetUnavailableReasonCode: null,
      idempotencyKey: expect.any(String),
    })
  })

  it('keeps create choices limited to the three writable priorities', async () => {
    const wrapper = mount(RepairPage, { attachTo: document.body })

    await wrapper.get('[data-testid="priority-trigger"]').trigger('click')
    await flushPromises()

    const labels = [
      ...document.body.querySelectorAll<HTMLButtonElement>(
        '[data-slot="mobile-sheet-content"] button',
      ),
    ]
      .map((button) => button.textContent?.trim())
      .filter((label): label is string => Boolean(label))
    expect(labels).toEqual(['高', '中', '低', '取消'])
  })

  it('keeps the selected priority when the ActionSheet is cancelled', async () => {
    route.query = { deviceAssetId: 'DEV-ROUTE-1' }
    const wrapper = mount(RepairPage, { attachTo: document.body })
    expect(wrapper.get('[data-testid="device-trigger"]').text()).not.toContain('由报警发起报修')

    await selectPriority(wrapper, '中')

    await wrapper.get('[data-testid="priority-trigger"]').trigger('click')
    await flushPromises()
    ;[...document.body.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === '取消')!
      .click()
    await flushPromises()

    expect(wrapper.get('[data-testid="priority-trigger"]').text()).toContain('中')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()
    expect(createWorkOrder.mock.calls.at(-1)?.[0]).toMatchObject({ priority: 'medium' })
  })

  it('keeps route alarm context read-only and submits the exact route IDs', async () => {
    route.query = { deviceAssetId: 'DEV-1', sourceAlarmId: 'ALM-9' }
    const wrapper = mount(RepairPage, { attachTo: document.body })

    expect(wrapper.find('[data-testid="device-input"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="device-trigger"]').text()).toContain('DEV-1')
    expect(wrapper.text()).toContain('由报警发起报修')
    expect(wrapper.text()).not.toContain('ALM-9')
    expect(wrapper.find('input[name="sourceAlarmId"]').exists()).toBe(false)
    expect(wrapper.find('input[name="openedBy"]').exists()).toBe(false)
    expect(wrapper.find('input[name="assignedTechnicianUserId"]').exists()).toBe(false)

    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledWith({
      deviceAssetId: 'DEV-1',
      priority: 'high',
      assetUnavailableReasonCode: null,
      sourceAlarmId: 'ALM-9',
      idempotencyKey: expect.any(String),
    })
  })

  it('reactively replaces the route device and alarm as one pair on the same page instance', async () => {
    route.query = { deviceAssetId: 'DEV-A', sourceAlarmId: 'ALM-A' }
    const wrapper = mount(RepairPage, { attachTo: document.body })

    route.query = { deviceAssetId: 'DEV-B', sourceAlarmId: 'ALM-B' }
    await wrapper.vm.$nextTick()
    expect(wrapper.get('[data-testid="device-trigger"]').text()).toContain('DEV-B')
    expect(wrapper.text()).toContain('由报警发起报修')
    expect(wrapper.text()).not.toContain('ALM-B')

    route.query = { deviceAssetId: 'DEV-A', sourceAlarmId: 'ALM-A' }
    await wrapper.vm.$nextTick()
    expect(wrapper.get('[data-testid="device-trigger"]').text()).toContain('DEV-A')
    expect(wrapper.text()).toContain('由报警发起报修')

    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()
    expect(createWorkOrder.mock.calls.at(-1)?.[0]).toMatchObject({
      deviceAssetId: 'DEV-A',
      sourceAlarmId: 'ALM-A',
    })
  })

  it('lets scan replace the selected device and clears stale alarm provenance', async () => {
    route.query = { deviceAssetId: 'DEV-ROUTE-1', sourceAlarmId: 'ALM-9' }
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await selectPriority(wrapper, '低')
    await selectReason(wrapper, '液压泄漏')

    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-SCAN-9')
    await scanInput.trigger('keydown.enter')

    expect(wrapper.get('[data-testid="device-trigger"]').text()).toContain('DEV-SCAN-9')
    expect(wrapper.get('[data-testid="priority-trigger"]').text()).toContain('低')
    expect(wrapper.text()).not.toContain('由报警发起报修')
    expect(wrapper.get('[data-testid="reason-trigger"]').text()).toContain('液压泄漏')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()
    expect(createWorkOrder.mock.calls.at(-1)?.[0]).toMatchObject({
      deviceAssetId: 'DEV-SCAN-9',
      priority: 'low',
      assetUnavailableReasonCode: 'Hyd-Leak_01',
    })
    expect(createWorkOrder.mock.calls.at(-1)?.[0]).not.toHaveProperty('sourceAlarmId')
  })

  it('pauses ScanBar focus reclaim while the reason picker is open', async () => {
    const wrapper = mount(RepairPage, { attachTo: document.body })

    await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
    await flushPromises()
    expect(wrapper.findComponent(NvScanBar).props('active')).toBe(false)

    await selectReason(wrapper, '不登记设备不可用')
    expect(wrapper.findComponent(NvScanBar).props('active')).toBe(true)
  })

  it('opens both mobile selectors from keyboard Enter', async () => {
    const wrapper = mount(RepairPage)

    await wrapper.get('[data-testid="device-trigger"]').trigger('keydown.enter')
    expect(wrapper.find('[data-testid="device-picker-stub"]').exists()).toBe(true)

    await wrapper.get('[data-testid="priority-trigger"]').trigger('keydown.enter')
    await flushPromises()
    expect(
      [...document.body.querySelectorAll<HTMLButtonElement>('button')].some(
        (button) => button.textContent?.trim() === '高',
      ),
    ).toBe(true)
  })

  it('opens the reason picker from keyboard Enter and keeps it reachable as a button', async () => {
    const wrapper = mount(RepairPage, { attachTo: document.body })
    const trigger = wrapper.get('[data-testid="reason-trigger"]')

    expect(trigger.attributes('role')).toBe('button')
    expect(trigger.attributes('tabindex')).toBe('0')
    await trigger.trigger('keydown.enter')
    await flushPromises()

    expect(document.body.querySelector('[data-testid="reason-option-Hyd-Leak_01"]')).not.toBeNull()
    expect(document.body.querySelector('[data-testid="reason-option-none"]')).not.toBeNull()
  })

  it('renders recent maintenance work orders with Chinese priority + status', () => {
    const wrapper = mount(RepairPage)
    const text = wrapper.text()
    expect(text).toContain('DEV-1001')
    expect(text).toContain('高') // priority high
    expect(text).toContain('待处理') // status open
    expect(text).toContain('DEV-2002')
    expect(text).toContain('计划保养') // priority planned
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
    expect(wrapper.find('[data-testid="task-list-retained-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('下一页加载失败')
    expect(wrapper.text()).toContain('DEV-1001')
  })

  it('sets deviceAssetId from a ScanBar scan', async () => {
    const wrapper = mount(RepairPage)
    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-SCAN-7')
    await scanInput.trigger('keydown.enter')

    expect(wrapper.get('[data-testid="device-trigger"]').text()).toContain('DEV-SCAN-7')
  })

  it('disables submit while createPending (double-submit guard)', async () => {
    createPending.value = true
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeDefined()
  })

  it('disables submit until deviceAssetId + priority present', async () => {
    const wrapper = mount(RepairPage)
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeDefined()
    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-9')
    await scanInput.trigger('keydown.enter')
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeDefined()
    await selectPriority(wrapper, '中')
    expect(wrapper.get('[data-testid="submit"]').attributes('disabled')).toBeUndefined()
  })

  it('prefills deviceAssetId + sourceAlarmId from the route query (from alarms page)', async () => {
    route.query = { deviceAssetId: 'DEV-1', sourceAlarmId: 'ALM-9' }
    const wrapper = mount(RepairPage)
    expect(wrapper.get('[data-testid="device-trigger"]').text()).toContain('DEV-1')
    // sourceAlarmId is carried through to the submit body
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledWith(
      expect.objectContaining({
        deviceAssetId: 'DEV-1',
        priority: 'high',
        assetUnavailableReasonCode: null,
        sourceAlarmId: 'ALM-9',
        idempotencyKey: expect.any(String),
      }),
    )
  })
})
