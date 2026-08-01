import type { BusinessConsoleMaintenanceWorkOrderItem } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({
  scopeReady: false,
  hasSuccessfulResponse: false,
  items: [] as BusinessConsoleMaintenanceWorkOrderItem[],
  filters: { status: '', deviceAssetId: '', keyword: '' },
  refresh: vi.fn(),
  loadMore: vi.fn(),
}))

vi.mock('@/composables/useMaintenanceSelfWorkOrders', () => ({
  useMaintenanceSelfWorkOrders: () => ({
    scopeReady: shallowRef(state.scopeReady),
    hasSuccessfulResponse: shallowRef(state.hasSuccessfulResponse),
    hasFailedResponse: computed(() => false),
    items: shallowRef(state.items),
    total: computed(() => state.items.length),
    loaded: computed(() => state.items.length),
    hasMore: computed(() => false),
    loadingMore: shallowRef(false),
    refreshing: shallowRef(false),
    loadMoreError: shallowRef(),
    pending: shallowRef(false),
    error: shallowRef(),
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
    state.items = []
    Object.assign(state.filters, { status: '', deviceAssetId: '', keyword: '' })
    sessionStorage.clear()
  })

  it('fails closed without principal scope and never claims that an unverified queue is personal', async () => {
    const { wrapper } = await mountPage()

    expect(wrapper.text()).toContain('维修工单')
    expect(wrapper.text()).toContain('个人维修范围未就绪')
    expect(wrapper.text()).not.toContain('我的工单')
    expect(wrapper.text()).not.toContain('我的维修工单')
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

    expect(wrapper.text()).toContain('当前维修人员（服务端 Self 范围）')
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
})
