import type { BusinessConsoleMaintenanceWorkOrderItem } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import TaskListShell from '@/components/task-list/TaskListShell.vue'

const state = vi.hoisted(() => ({
  scopeKey: 'org-001:env-dev:self:principal-1',
  scopeReady: false,
  hasSuccessfulResponse: false,
  hasFailedResponse: false,
  error: undefined as unknown,
  items: [] as BusinessConsoleMaintenanceWorkOrderItem[],
  filters: { status: '', deviceAssetIds: [] as string[], keyword: '' },
  refresh: vi.fn(),
  loadMore: vi.fn(),
}))

vi.mock('@/composables/useMaintenanceSelfWorkOrders', () => ({
  normalizeMaintenanceDeviceReferences: (values: unknown) =>
    Array.isArray(values)
      ? [
          ...new Set(
            values.filter(
              (value): value is string => typeof value === 'string' && value.length > 0,
            ),
          ),
        ]
      : [],
  useMaintenanceSelfWorkOrders: () => ({
    scopeKey: shallowRef(state.scopeKey),
    scopeReady: shallowRef(state.scopeReady),
    hasSuccessfulResponse: shallowRef(state.hasSuccessfulResponse),
    hasFailedResponse: shallowRef(state.hasFailedResponse),
    items: shallowRef(state.items),
    total: computed(() => state.items.length),
    loaded: computed(() => state.items.length),
    hasMore: computed(() => false),
    loadingMore: shallowRef(false),
    refreshing: shallowRef(false),
    loadMoreError: shallowRef(),
    pending: shallowRef(false),
    error: shallowRef(state.error),
    lastUpdatedAt: shallowRef('2026-08-02T01:00:00.000Z'),
    filters: reactive(state.filters),
    principalDisplayName: shallowRef('张维修'),
    refresh: state.refresh,
    loadMore: state.loadMore,
  }),
}))

import WorkOrdersPage from './index.vue'

async function mountPage() {
  const target = { template: '<div>detail</div>' }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/equipment/work-orders', component: WorkOrdersPage },
      { path: '/equipment/work-orders/:workOrderId', component: target },
    ],
  })
  await router.push('/equipment/work-orders')
  await router.isReady()
  const wrapper = mount(WorkOrdersPage, {
    global: {
      plugins: [router],
      stubs: {
        DeviceAssetPicker: {
          name: 'DeviceAssetPicker',
          props: ['open'],
          template:
            "<button data-testid=\"select-device\" @click=\"$emit('select', { deviceAssetId: '019F0000-0000-7000-8000-000000000302', code: '019F0000-0000-7000-8000-0000000000AA', displayName: '一号数控机床' })\">选择设备</button>",
        },
      },
    },
  })
  return { wrapper, router }
}

describe('maintenance self work-order queue page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    state.scopeReady = false
    state.scopeKey = 'org-001:env-dev:self:principal-1'
    state.hasSuccessfulResponse = false
    state.hasFailedResponse = false
    state.error = undefined
    state.items = []
    Object.assign(state.filters, { status: '', deviceAssetIds: [], keyword: '' })
    sessionStorage.clear()
  })

  it('fails closed without principal scope and never claims that an unverified queue is personal', async () => {
    const { wrapper } = await mountPage()

    expect(wrapper.text()).toContain('维修工单')
    expect(wrapper.text()).toContain('当前账号暂无法查看维修工单')
    expect(wrapper.text()).not.toContain('我的工单')
    expect(wrapper.text()).not.toContain('我的维修工单')
    expect(wrapper.text()).toContain('当前账号暂无法查看，请重新登录或联系管理员')
    for (const diagnostic of ['Self', '服务端', '已授权', '不可解析', '未发起查询', '组织/环境']) {
      expect(wrapper.text()).not.toContain(diagnostic)
    }
    expect(wrapper.find('[data-testid="select-device"]').exists()).toBe(false)
  })

  it('renders the composable retry error and its already-safe empty rows', async () => {
    state.scopeReady = true
    state.hasFailedResponse = true
    state.items = []

    const { wrapper } = await mountPage()

    expect(wrapper.get('[data-testid="maintenance-self-work-orders-error"]').text()).toContain(
      '维修工单读取失败，请重试',
    )
    expect(wrapper.find('[data-testid="maintenance-work-order-row"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('暂无符合筛选条件')

    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(state.refresh).toHaveBeenCalledTimes(1)
  })

  it('opens a server-returned row by its strong work-order ID', async () => {
    state.scopeReady = true
    state.hasSuccessfulResponse = true
    state.items = [
      {
        workOrderId: '019f0000-0000-7000-8000-000000000301',
        sourceReferenceId: 'MWO-2026-0042',
        deviceAssetId: 'device-1',
        status: 'accepted',
        priority: 'critical',
        assignedTechnicianUserId: 'principal-1',
      },
    ]
    const { wrapper, router } = await mountPage()

    expect(wrapper.text()).toContain('维修人员 张维修')
    expect(wrapper.text()).toContain('优先级 紧急')
    expect(wrapper.text()).not.toContain('principal-1')
    expect(wrapper.text()).not.toContain('Self')
    expect(wrapper.text()).not.toContain('服务端')
    expect(wrapper.text()).toContain('设备已关联')
    expect(wrapper.text()).not.toContain('device-1')
    await wrapper.get('[data-testid="maintenance-work-order-row"]').trigger('keydown', { key: ' ' })
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe(
      '/equipment/work-orders/019f0000-0000-7000-8000-000000000301',
    )
  })

  it('renders a plan-generated work order with a stable Chinese priority', async () => {
    state.scopeReady = true
    state.hasSuccessfulResponse = true
    state.items = [
      {
        workOrderId: '019f0000-0000-7000-8000-000000000303',
        sourceReferenceId: 'PM-2026-0042',
        deviceAssetId: 'device-1',
        status: 'open',
        priority: 'planned',
        assignedTechnicianUserId: 'principal-1',
      },
    ]

    const { wrapper } = await mountPage()

    expect(wrapper.get('[data-testid="maintenance-work-order-row"]').text()).toContain(
      '优先级 计划保养',
    )
  })

  it('queries only by the selected strong device ID and retains the code only for session state', async () => {
    state.scopeReady = true
    const { wrapper } = await mountPage()

    await wrapper.get('[data-testid="select-device"]').trigger('click')

    expect(state.filters.deviceAssetIds).toEqual(['019f0000-0000-7000-8000-000000000302'])
    expect(wrapper.getComponent(TaskListShell).props('filterState')).toMatchObject({
      deviceAssetIds: ['019f0000-0000-7000-8000-000000000302'],
      deviceCode: '019F0000-0000-7000-8000-0000000000AA',
    })
    expect(wrapper.text()).toContain('一号数控机床')
  })

  it('fails closed when a picker or scan payload has no canonical strong device ID', async () => {
    state.scopeReady = true
    const { wrapper } = await mountPage()

    wrapper.getComponent({ name: 'DeviceAssetPicker' }).vm.$emit('select', {
      code: 'DEV-SCAN-ONLY',
      displayName: '仅编码设备',
    })
    await flushPromises()

    expect(state.filters.deviceAssetIds).toEqual([])
    expect(wrapper.text()).not.toContain('仅编码设备')
  })

  it('opens the device filter with Space', async () => {
    state.scopeReady = true
    const { wrapper } = await mountPage()

    await wrapper.get('[data-testid="maintenance-device-filter"]').trigger('keydown', { key: ' ' })

    expect(wrapper.getComponent({ name: 'DeviceAssetPicker' }).props('open')).toBe(true)
  })

  it('drops an unknown status restored from session state', async () => {
    state.scopeReady = true
    const { wrapper } = await mountPage()

    wrapper.findComponent(TaskListShell).vm.$emit('restore', {
      filters: { status: 'future-server-state', deviceAssetIds: [], keyword: '' },
    })
    await flushPromises()

    expect(state.filters.status).toBe('')
  })

  it('restores only a validated strong device ID and keeps its code out of query references', async () => {
    state.scopeReady = true
    const { wrapper } = await mountPage()

    wrapper.findComponent(TaskListShell).vm.$emit('restore', {
      filters: {
        status: '',
        deviceAssetIds: ['019F0000-0000-7000-8000-000000000302'],
        deviceCode: 'DEV-OLD-CODE',
        deviceLabel: '恢复设备',
        keyword: '',
      },
    })
    await flushPromises()

    expect(state.filters.deviceAssetIds).toEqual(['019f0000-0000-7000-8000-000000000302'])
    expect(wrapper.getComponent(TaskListShell).props('filterState')).toMatchObject({
      deviceAssetIds: ['019f0000-0000-7000-8000-000000000302'],
      deviceCode: 'DEV-OLD-CODE',
    })
    expect(wrapper.text()).toContain('恢复设备')
  })

  it('drops restored device code and label when the strong device ID is absent', async () => {
    state.scopeReady = true
    const { wrapper } = await mountPage()

    wrapper.findComponent(TaskListShell).vm.$emit('restore', {
      filters: {
        status: '',
        deviceAssetIds: ['DEV-SCAN-ONLY'],
        deviceCode: 'DEV-SCAN-ONLY',
        deviceLabel: '不可信设备',
        keyword: '',
      },
    })
    await flushPromises()

    expect(state.filters.deviceAssetIds).toEqual([])
    expect(wrapper.text()).not.toContain('不可信设备')
  })

  it('isolates restored device filters, labels, and scroll state by organization and environment', async () => {
    state.scopeReady = true
    sessionStorage.setItem(
      'nerv-iip.business-pda.task-list.maintenance-self-work-orders:org-old:env-old:self:principal-1',
      JSON.stringify({
        filters: {
          status: 'accepted',
          deviceAssetIds: ['019f0000-0000-7000-8000-000000000001', 'DEV-OLD'],
          deviceLabel: '旧环境设备',
          keyword: '旧环境关键字',
        },
        scrollTop: 286,
      }),
    )

    const { wrapper } = await mountPage()
    await flushPromises()

    const shell = wrapper.getComponent(TaskListShell)
    expect(shell.props('stateKey')).toBe(
      'maintenance-self-work-orders:org-001:env-dev:self:principal-1',
    )
    expect(state.filters).toEqual({ status: '', deviceAssetIds: [], keyword: '' })
    expect(wrapper.text()).not.toContain('旧环境设备')
    expect((wrapper.get('.nv-m-pr-scroll').element as HTMLElement).scrollTop).toBe(0)
  })
})
