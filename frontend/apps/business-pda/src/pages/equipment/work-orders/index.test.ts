import type { BusinessConsoleMaintenanceWorkOrderItem } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import TaskListShell from '@/components/task-list/TaskListShell.vue'

const state = vi.hoisted(() => ({
  scopeReady: false,
  hasSuccessfulResponse: false,
  hasFailedResponse: false,
  error: undefined as unknown,
  items: [] as BusinessConsoleMaintenanceWorkOrderItem[],
  filters: { status: '', deviceAssetId: '', keyword: '' },
  refresh: vi.fn(),
  loadMore: vi.fn(),
}))

vi.mock('@/composables/useMaintenanceSelfWorkOrders', () => ({
  useMaintenanceSelfWorkOrders: () => ({
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
          template:
            "<button data-testid=\"select-device\" @click=\"$emit('select', { deviceAssetId: 'device-1', displayName: '一号数控机床' })\">选择设备</button>",
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
    state.hasSuccessfulResponse = false
    state.hasFailedResponse = false
    state.error = undefined
    state.items = []
    Object.assign(state.filters, { status: '', deviceAssetId: '', keyword: '' })
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

  it('shows an explicit retryable error and no rows for a rejected success envelope', async () => {
    state.scopeReady = true
    state.hasFailedResponse = true
    state.items = [
      {
        workOrderId: 'STALE-WO',
        sourceReferenceId: '不应显示的旧工单',
      },
    ]

    const { wrapper } = await mountPage()

    expect(wrapper.get('[data-testid="maintenance-self-work-orders-error"]').text()).toContain(
      '维修工单读取失败，请重试',
    )
    expect(wrapper.find('[data-testid="maintenance-work-order-row"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('暂无符合筛选条件')
    expect(wrapper.text()).not.toContain('不应显示的旧工单')

    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(state.refresh).toHaveBeenCalledTimes(1)
  })

  it('opens a server-returned row by its strong work-order ID', async () => {
    state.scopeReady = true
    state.hasSuccessfulResponse = true
    state.items = [
      {
        workOrderId: '019f-strong-id',
        sourceReferenceId: 'MWO-2026-0042',
        deviceAssetId: 'device-1',
        status: 'accepted',
        priority: 'high',
      },
    ]
    const { wrapper, router } = await mountPage()

    expect(wrapper.text()).toContain('分派给当前维修人员')
    expect(wrapper.text()).not.toContain('Self')
    expect(wrapper.text()).not.toContain('服务端')
    expect(wrapper.text()).toContain('设备已关联')
    expect(wrapper.text()).not.toContain('device-1')
    await wrapper.get('[data-testid="maintenance-work-order-row"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/equipment/work-orders/019f-strong-id')
  })

  it('stores the stable device ID selected from the server directory', async () => {
    state.scopeReady = true
    const { wrapper } = await mountPage()

    await wrapper.get('[data-testid="select-device"]').trigger('click')

    expect(state.filters.deviceAssetId).toBe('device-1')
    expect(wrapper.text()).toContain('一号数控机床')
  })

  it('drops an unknown status restored from session state', async () => {
    state.scopeReady = true
    const { wrapper } = await mountPage()

    wrapper.findComponent(TaskListShell).vm.$emit('restore', {
      filters: { status: 'future-server-state', deviceAssetId: '', keyword: '' },
    })
    await flushPromises()

    expect(state.filters.status).toBe('')
  })
})
