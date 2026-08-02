import { OfflineError, RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'
import { NvScanBar } from '@nerv-iip/ui-mobile'

// ---- vue-router mock（默认无 query；个别用例覆写 useRoute）---------------------
const push = vi.fn(() => Promise.resolve())
const route = reactive({ query: {} as Record<string, string> })
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => route,
}))

// ---- useBusinessMaintenance mock ----------------------------------------------
const createWorkOrder = vi.fn(async (_input: Record<string, unknown>) => ({}))
const createPending = ref(false)
const canReadWorkOrderDetail = ref(true)
const confirmedCreatedWorkOrder = ref<Record<string, unknown>>()
const createdDetailHasSuccessfulResponse = ref(false)
const createdDetailPending = ref(false)
const createdDetailHasFailedResponse = ref(false)
const createdDeviceHasFailedResponse = ref(false)
const refreshCreatedWorkOrder = vi.fn(async () => {})
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
const workOrdersRefreshing = ref(false)
const organizationId = ref('org-001')
const environmentId = ref('env-dev')
const scopeReady = ref(true)
const workOrdersLastUpdatedAt = ref('2026-07-28T10:20:30.000Z')
const refreshWorkOrders = vi.fn(async () => {})
const loadMoreWorkOrders = vi.fn(async () => {})

vi.mock('@/composables/useBusinessMaintenance', () => ({
  useBusinessMaintenance: () => ({
    workOrders,
    workOrdersTotal: computed(() => workOrders.value.length),
    workOrdersLoaded: computed(() => workOrders.value.length),
    workOrdersLoadingMore: ref(false),
    workOrdersLoadMoreError: ref<unknown>(),
    loadMoreWorkOrders,
    organizationId,
    environmentId,
    scopeReady,
    workOrdersLastUpdatedAt,
    workOrdersHasSuccessfulResponse: computed(
      () => !workOrdersPending.value && !workOrdersError.value,
    ),
    workOrdersHasFailedResponse: computed(() => false),
    workOrdersPending,
    workOrdersRefreshing,
    workOrdersError,
    refreshWorkOrders,
    workOrderFilters: reactive({ skip: 0, take: 20, status: undefined, keyword: undefined }),
    createWorkOrder,
    createPending,
    canReadWorkOrderDetail,
  }),
}))

vi.mock('@/composables/useMaintenanceSelfWorkOrders', () => ({
  useMaintenanceSelfWorkOrderDetail: () => ({
    authoritativeWorkOrder: confirmedCreatedWorkOrder,
    authoritativeHasSuccessfulResponse: createdDetailHasSuccessfulResponse,
    authoritativePending: createdDetailPending,
    authoritativeHasFailedResponse: createdDetailHasFailedResponse,
    deviceHasFailedResponse: createdDeviceHasFailedResponse,
    refresh: refreshCreatedWorkOrder,
  }),
}))

vi.mock('@/components/equipment/DeviceAssetPicker.vue', () => ({
  default: {
    name: 'DeviceAssetPicker',
    props: ['open'],
    emits: ['update:open', 'select'],
    template: '<div v-if="open" data-testid="device-picker-stub" />',
  },
}))

import RepairPage from './repair.vue'

const createdWorkOrderId = '33333333-3333-3333-3333-333333333333'

function confirmedCreateResponse(resourceId = createdWorkOrderId) {
  return {
    success: true,
    data: {
      workOrderId: createdWorkOrderId,
      operationReceipt: {
        operationType: 'maintenance.work-order.create',
        resourceType: 'maintenance-work-order',
        resourceId,
        outcome: 'confirmed',
        stateConfirmed: true,
        accepted: false,
        idempotencyKey: 'page-composable-confirmed',
      },
    },
  }
}

async function selectPriority(wrapper: ReturnType<typeof mount>, label: '高' | '中' | '低') {
  await wrapper.get('[data-testid="priority-trigger"]').trigger('click')
  await flushPromises()
  const option = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => button.textContent?.trim() === label,
  )
  expect(option).toBeTruthy()
  option!.click()
  await flushPromises()
}

beforeEach(() => {
  push.mockClear()
  createWorkOrder.mockClear()
  createWorkOrder.mockResolvedValue(confirmedCreateResponse())
  refreshWorkOrders.mockClear()
  route.query = {}
  createPending.value = false
  canReadWorkOrderDetail.value = true
  confirmedCreatedWorkOrder.value = undefined
  createdDetailHasSuccessfulResponse.value = false
  createdDetailPending.value = false
  createdDetailHasFailedResponse.value = false
  createdDeviceHasFailedResponse.value = false
  refreshCreatedWorkOrder.mockReset()
  workOrdersError.value = null
  workOrdersPending.value = false
  workOrdersRefreshing.value = false
})

describe('PDA equipment repair page', () => {
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

    expect(wrapper.find('[data-testid="priority-select"]').exists()).toBe(false)
    await selectPriority(wrapper, label as '高' | '中' | '低')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledWith({
      deviceAssetId: 'DEV-ROUTE-1',
      priority,
      assetUnavailableReason: '',
      idempotencyKey: expect.any(String),
    })
  })

  it('keeps the selected priority when the ActionSheet is cancelled', async () => {
    route.query = { deviceAssetId: 'DEV-ROUTE-1' }
    const wrapper = mount(RepairPage, { attachTo: document.body })

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
    expect(wrapper.text()).toContain('报警上下文')
    expect(wrapper.find('input[name="sourceAlarmId"]').exists()).toBe(false)
    expect(wrapper.find('input[name="openedBy"]').exists()).toBe(false)
    expect(wrapper.find('input[name="assignedTechnicianUserId"]').exists()).toBe(false)

    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledWith({
      deviceAssetId: 'DEV-1',
      priority: 'high',
      assetUnavailableReason: '',
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
    expect(wrapper.text()).toContain('报警上下文 · ALM-B')

    route.query = { deviceAssetId: 'DEV-A', sourceAlarmId: 'ALM-A' }
    await wrapper.vm.$nextTick()
    expect(wrapper.get('[data-testid="device-trigger"]').text()).toContain('DEV-A')
    expect(wrapper.text()).toContain('报警上下文 · ALM-A')

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
    await wrapper.get('[data-testid="reason-input"]').setValue('液压压力异常')

    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-SCAN-9')
    await scanInput.trigger('keydown.enter')

    expect(wrapper.get('[data-testid="device-trigger"]').text()).toContain('DEV-SCAN-9')
    expect(wrapper.get('[data-testid="priority-trigger"]').text()).toContain('低')
    expect(wrapper.text()).not.toContain('报警上下文')
    expect((wrapper.get('[data-testid="reason-input"]').element as HTMLTextAreaElement).value).toBe(
      '液压压力异常',
    )
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()
    expect(createWorkOrder.mock.calls.at(-1)?.[0]).toMatchObject({
      deviceAssetId: 'DEV-SCAN-9',
      priority: 'low',
      assetUnavailableReason: '液压压力异常',
    })
    expect(createWorkOrder.mock.calls.at(-1)?.[0]).not.toHaveProperty('sourceAlarmId')
  })

  it('pauses ScanBar focus reclaim while the reason textarea is focused', async () => {
    const wrapper = mount(RepairPage)
    const reason = wrapper.get('[data-testid="reason-input"]')

    await reason.trigger('focus')
    expect(wrapper.findComponent(NvScanBar).props('active')).toBe(false)

    await reason.trigger('blur')
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
    expect(wrapper.find('[data-testid="task-list-retained-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('下一页加载失败')
    expect(wrapper.text()).toContain('DEV-1001')
  })

  it('submits a new repair with an operation key but WITHOUT org/env/openedBy', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="reason-input"]').setValue('主轴异响')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledTimes(1)
    const body = createWorkOrder.mock.calls[0][0]
    expect(body).toMatchObject({
      deviceAssetId: 'DEV-9',
      priority: 'high',
      assetUnavailableReason: '主轴异响',
    })
    expect(body.idempotencyKey).toBeTruthy()
    expect(body).not.toHaveProperty('organizationId')
    expect(body).not.toHaveProperty('environmentId')
    expect(body).not.toHaveProperty('openedBy')
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

  it('shows a success Result after a successful submit', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    const result = wrapper.find('[data-result][data-status="success"]')
    expect(result.exists()).toBe(true)
    expect(wrapper.text()).toContain('报修已提交')
    expect(wrapper.get('[data-testid="created-work-order-assignment-state"]').text()).toContain(
      '尚未确认工单指派给当前账号',
    )
    expect(wrapper.find('[data-testid="view-created-work-order"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="recheck-created-work-order-assignment"]').text()).toContain(
      '重新核验指派状态',
    )
  })

  it('fails closed when payload and receipt repeat the same non-GUID identifier', async () => {
    createWorkOrder.mockResolvedValueOnce({
      success: true,
      data: {
        workOrderId: 'WO-INVALID',
        operationReceipt: { resourceId: 'WO-INVALID' },
      },
    })
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(false)
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="recheck-created-work-order-assignment"]').exists()).toBe(
      false,
    )
  })

  it('keeps confirmed assignment visible when device enrichment fails', async () => {
    route.query = { deviceAssetId: 'DEV-ROUTE-1' }
    confirmedCreatedWorkOrder.value = {
      workOrderId: '33333333-3333-3333-3333-333333333333',
    }
    createdDetailHasSuccessfulResponse.value = true
    createdDeviceHasFailedResponse.value = true
    const wrapper = mount(RepairPage, { attachTo: document.body })

    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="created-work-order-assignment-state"]').text()).toContain(
      '已确认工单指派给当前维修人员',
    )
    expect(wrapper.get('[data-testid="created-work-order-device-state"]').text()).toContain(
      '设备资料暂不可用',
    )
  })

  it('rechecks authoritative assignment before opening alarm-sourced repair detail', async () => {
    route.query = { deviceAssetId: 'DEV-9', sourceAlarmId: 'ALM-9' }
    refreshCreatedWorkOrder.mockImplementationOnce(async () => {
      confirmedCreatedWorkOrder.value = {
        workOrderId: '33333333-3333-3333-3333-333333333333',
        assignedTechnicianUserId: 'principal-1',
        sourceAlarmId: 'ALM-9',
      }
      createdDetailHasSuccessfulResponse.value = true
    })
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="view-created-work-order"]').exists()).toBe(false)
    await wrapper.get('[data-testid="recheck-created-work-order-assignment"]').trigger('click')
    await flushPromises()

    expect(refreshCreatedWorkOrder).toHaveBeenCalledTimes(1)
    expect(wrapper.get('[data-testid="created-work-order-assignment-state"]').text()).toContain(
      '已确认工单指派给当前维修人员',
    )
    await wrapper.get('[data-testid="view-created-work-order"]').trigger('click')

    expect(push).toHaveBeenCalledWith({
      path: '/equipment/work-orders/33333333-3333-3333-3333-333333333333',
      query: { sourceAlarmId: 'ALM-9' },
    })
  })

  it('does not offer a detail link until the self detail is authoritatively confirmed', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="view-created-work-order"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="recheck-created-work-order-assignment"]').exists()).toBe(
      true,
    )
  })

  it('does not offer detail navigation without both maintenance and device location reads', async () => {
    canReadWorkOrderDetail.value = false
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="view-created-work-order"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="recheck-created-work-order-assignment"]').exists()).toBe(
      false,
    )
    canReadWorkOrderDetail.value = true
  })

  it('shows an error Result with retry when submit fails', async () => {
    createWorkOrder.mockRejectedValueOnce(new Error('网络错误'))
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
  })

  it('结果未知时锁定原 payload 并用相同 idempotencyKey 安全重试', async () => {
    createWorkOrder.mockRejectedValueOnce(new RequestTimeoutError())
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="reason-input"]').setValue('主轴异响')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('网络超时，请检查连接后重试')
    expect(wrapper.text()).toContain('相同操作编号')
    expect(wrapper.find('[data-testid="verify-list"]').exists()).toBe(false)
    const firstPayload = createWorkOrder.mock.calls[0][0]

    await wrapper.get('[data-testid="retry"]').trigger('click')
    route.query = { deviceAssetId: 'DEV-ROUTE-CHANGED', sourceAlarmId: 'ALM-CHANGED' }
    await wrapper.vm.$nextTick()
    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-CHANGED')
    await scanInput.trigger('keydown.enter')
    await wrapper.get('[data-testid="priority-trigger"]').trigger('click')
    await wrapper.get('[data-testid="reason-input"]').setValue('篡改后的原因')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledTimes(2)
    expect(createWorkOrder.mock.calls[1][0]).toEqual(firstPayload)
  })

  it('确定业务失败后编辑设备、优先级、原因会形成新意图并旋转 idempotencyKey', async () => {
    createWorkOrder.mockRejectedValueOnce({ success: false, message: '设备不存在' })
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="reason-input"]').setValue('旧原因')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('设备不存在')
    // 服务端已明确失败、无副作用 → 给"重试"，不给"核实"入口。
    expect(wrapper.find('[data-testid="verify-list"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="retry"]').exists()).toBe(true)
    const firstKey = createWorkOrder.mock.calls[0][0].idempotencyKey

    await wrapper.get('[data-testid="retry"]').trigger('click')
    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-10')
    await scanInput.trigger('keydown.enter')
    await selectPriority(wrapper, '中')
    await wrapper.get('[data-testid="reason-input"]').setValue('新原因')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledTimes(2)
    expect(createWorkOrder.mock.calls[1][0]).toMatchObject({
      deviceAssetId: 'DEV-10',
      priority: 'medium',
      assetUnavailableReason: '新原因',
    })
    expect(createWorkOrder.mock.calls[1][0].idempotencyKey).not.toBe(firstKey)
  })

  // 离线预检在请求发出前抛出 → 服务端从未收到 → 安全重试（不逼用户绕路核实，#814 离线可操作）。
  it('离线（请求未发出）时给安全重试而非核实', async () => {
    createWorkOrder.mockRejectedValueOnce(new OfflineError())
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('当前离线，请检查网络连接后重试')
    expect(wrapper.find('[data-testid="retry"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="verify-list"]').exists()).toBe(false)
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
        assetUnavailableReason: '',
        sourceAlarmId: 'ALM-9',
        idempotencyKey: expect.any(String),
      }),
    )
  })
})
