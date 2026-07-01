import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, shallowRef } from 'vue'
import IndexPage from './index.vue'
import { createBusinessConsoleI18n } from '@/i18n'
import { useAuthStore } from '@/stores/auth'

const coladaState = vi.hoisted(() => ({
  isLoading: false,
  queryData: undefined as unknown,
}))

vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsoleWorkbenchSummaryQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleWorkbenchSummary' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn(() => ({
    data: shallowRef(coladaState.queryData),
    error: shallowRef(),
    isLoading: shallowRef(coladaState.isLoading),
    refetch: vi.fn(),
  })),
}))

const RouterLinkStub = defineComponent({
  name: 'RouterLink',
  props: {
    to: { type: [String, Object], required: true },
  },
  template: '<a :href="typeof to === \'string\' ? to : to.path"><slot /></a>',
})

function mountWorkbench(permissionCodes: string[]) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalType: 'user',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes,
    },
  })

  return mount(IndexPage, {
    global: {
      plugins: [pinia, createBusinessConsoleI18n({ locale: 'zh-CN' })],
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        RouterLink: RouterLinkStub,
      },
    },
  })
}

describe('business workbench page', () => {
  beforeEach(() => {
    coladaState.isLoading = false
    coladaState.queryData = {
      success: true,
      data: {
        kpis: [
          { key: 'releasedWorkOrders', label: 'Released work orders', value: 7, source: 'BusinessMES', status: 'available' },
          { key: 'openNcrs', label: 'Open NCRs', value: 2, source: 'BusinessQuality', status: 'available' },
          { key: 'sensitiveFinance', label: 'Sensitive receivables amount', value: 980000, source: 'BusinessERP', status: 'forbidden' },
        ],
        todos: {
          status: 'available',
          total: 2,
          items: [
            { source: 'BusinessApproval', itemId: 'approval-1', itemType: 'purchase-order', status: 'pending', referenceId: 'PO-260701-0001', dueAtUtc: '2026-07-01T08:00:00Z' },
            { source: 'Notification', itemId: 'task-1', itemType: 'inventory-count', status: 'open', referenceId: 'COUNT-260701-0002' },
          ],
        },
        messages: {
          status: 'available',
          total: 2,
          unread: 1,
          items: [
            { messageId: 'message-1', status: 'unread', severity: 'warning', resourceType: 'work-order', resourceId: 'WO-260701-0001', createdAtUtc: '2026-07-01T09:00:00Z', title: 'Sensitive customer escalation' },
          ],
        },
        alerts: {
          status: 'available',
          total: 1,
          critical: 1,
          items: [
            { alarmEventId: 'alarm-1', deviceAssetId: 'DEV-1001', alarmCode: 'TEMP_HIGH', severity: 'critical', raisedAtUtc: '2026-07-01T09:10:00Z' },
          ],
        },
        sourceStatuses: [
          { source: 'BusinessMES', status: 'available' },
          { source: 'BusinessQuality', status: 'available' },
          { source: 'BusinessApproval', status: 'available' },
          { source: 'Notification', status: 'available' },
          { source: 'IndustrialTelemetry', status: 'available' },
          { source: 'BusinessInventory', status: 'unsupported', permissionCode: 'business.inventory.ledger.read', reason: 'global-inventory-workbench-summary-not-connected' },
        ],
      },
    }
  })

  it('does not show negative empty states while the summary is loading', async () => {
    coladaState.isLoading = true
    coladaState.queryData = undefined

    const wrapper = mountWorkbench(['business.mes.work-orders.read'])
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('正在刷新工作台摘要')
    expect(text).not.toContain('暂无可显示指标')
    expect(text).not.toContain('当前角色没有可汇总的跨域指标')
    expect(text).not.toContain('暂无待处理事项')
    expect(text).not.toContain('暂无未读消息')
    expect(text).not.toContain('暂无当前预警')
    expect(text).not.toContain('正在等待来源状态')
  })

  it('renders the facade summary instead of local static workbench items', async () => {
    const wrapper = mountWorkbench([
      'business.mes.work-orders.read',
      'business.quality.ncr.read',
      'business.approvals.read',
      'business.notification.messages.read',
      'business.notification.tasks.read',
      'business.iiot.alarms.read',
    ])
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('已下达工单')
    expect(text).toContain('7')
    expect(text).toContain('未关闭质量异常')
    expect(text).toContain('待办 2')
    expect(text).toContain('消息 2')
    expect(text).toContain('设备预警 1')
    expect(text).toContain('DEV-1001')
    expect(text).toContain('库存管理')
    expect(text).toContain('未接入')
    expect(text).not.toContain('设备停机影响')
    expect(text).not.toContain('Sensitive customer escalation')
    expect(text).not.toContain('Sensitive receivables amount')
    expect(text).not.toContain('business.inventory.ledger.read')
    expect(text).not.toContain('global-inventory-workbench-summary-not-connected')
  })

  it('shows only route-ready shortcuts allowed by the principal permissions', async () => {
    const wrapper = mountWorkbench(['business.inventory.ledger.read'])
    await flushPromises()

    const links = wrapper.findAll('a').map((link) => link.attributes('href'))
    expect(links).toContain('/inventory/availability')
    expect(links).not.toContain('/mes/work-orders')
    expect(links).not.toContain('/quality/ncrs')
    expect(wrapper.text()).not.toContain('工单与派工')
  })

  it('keeps raw source identifiers for unmapped source status entries', async () => {
    coladaState.queryData = {
      success: true,
      data: {
        kpis: [],
        todos: { status: 'available', total: 0, items: [] },
        messages: { status: 'available', total: 0, unread: 0, items: [] },
        alerts: { status: 'available', total: 0, critical: 0, items: [] },
        sourceStatuses: [
          { source: 'BusinessERP', status: 'available' },
          { source: 'BusinessScheduling', status: 'unavailable' },
        ],
      },
    }

    const wrapper = mountWorkbench(['business.erp.procurement.read'])
    await flushPromises()

    expect(wrapper.find('[data-source="BusinessERP"]').exists()).toBe(true)
    expect(wrapper.find('[data-source="BusinessScheduling"]').exists()).toBe(true)
  })
})
