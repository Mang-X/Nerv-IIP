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
  queryError: undefined as unknown,
}))

vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsoleWorkbenchSummaryQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleWorkbenchSummary' }],
    query: vi.fn(),
  })),
}))

// 设备名 join 走主数据台账；本用例只验工作台自身的三态，主数据整条链路桩掉，
// 免得为一个 resolveDevice 把 useBusinessMasterData 的全部 mutation options 都补进 mock。
vi.mock('@/composables/useMasterDataDisplayNames', () => ({
  useMasterDataDisplayNames: () => ({
    resolveDevice: (code: string) => (code === 'DEV-1001' ? '注塑机 1 号' : undefined),
    formatUom: (code: string, fallback: string) => code || fallback,
  }),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn(() => ({
    data: shallowRef(coladaState.queryData),
    error: shallowRef(coladaState.queryError),
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
    coladaState.queryError = undefined
    coladaState.queryData = {
      success: true,
      data: {
        kpis: [
          {
            key: 'releasedWorkOrders',
            label: 'Released work orders',
            value: 7,
            source: 'BusinessMES',
            status: 'available',
          },
          {
            key: 'openNcrs',
            label: 'Open NCRs',
            value: 2,
            source: 'BusinessQuality',
            status: 'available',
          },
          {
            key: 'sensitiveFinance',
            label: 'Sensitive receivables amount',
            value: 980000,
            source: 'BusinessERP',
            status: 'forbidden',
          },
        ],
        todos: {
          status: 'available',
          total: 2,
          items: [
            {
              source: 'BusinessApproval',
              itemId: 'approval-1',
              itemType: 'purchase-order',
              status: 'pending',
              referenceId: 'PO-260701-0001',
              dueAtUtc: '2026-07-01T08:00:00Z',
            },
            {
              source: 'Notification',
              itemId: 'task-1',
              itemType: 'inventory-count',
              status: 'open',
              referenceId: 'COUNT-260701-0002',
            },
          ],
        },
        messages: {
          status: 'available',
          total: 2,
          unread: 1,
          items: [
            {
              messageId: 'message-1',
              status: 'unread',
              severity: 'warning',
              resourceType: 'work-order',
              resourceId: 'WO-260701-0001',
              createdAtUtc: '2026-07-01T09:00:00Z',
              title: 'Sensitive customer escalation',
            },
          ],
        },
        alerts: {
          status: 'available',
          total: 1,
          critical: 1,
          items: [
            {
              alarmEventId: 'alarm-1',
              deviceAssetId: 'DEV-1001',
              alarmCode: 'TEMP_HIGH',
              severity: 'critical',
              raisedAtUtc: '2026-07-01T09:10:00Z',
            },
          ],
        },
        sourceStatuses: [
          { source: 'BusinessMES', status: 'available' },
          { source: 'BusinessQuality', status: 'available' },
          { source: 'BusinessApproval', status: 'available' },
          { source: 'Notification', status: 'available' },
          { source: 'IndustrialTelemetry', status: 'available' },
          {
            source: 'BusinessInventory',
            status: 'unsupported',
            permissionCode: 'business.inventory.ledger.read',
            reason: 'global-inventory-workbench-summary-not-connected',
          },
        ],
      },
    }
  })

  it('holds the dashboard shape with in-card skeletons while the summary is loading', async () => {
    coladaState.isLoading = true
    coladaState.queryData = undefined

    const wrapper = mountWorkbench(['business.mes.work-orders.read'])
    await flushPromises()

    // 加载态是卡内骨架，不是页面顶部一行裸文字
    expect(wrapper.findAll('[data-slot="skeleton"]').length).toBeGreaterThan(0)
    // 英雄区左格恒有节点：4 张骨架指标卡占位，环图卡不会被自动布局顶进 1fr 列
    const heroSection = wrapper.get('section[aria-label="跨域指标"]')
    expect(heroSection.element.children).toHaveLength(2)
    // 三张行动卡形状恒定，读数位也是骨架，不先亮 0 再跳变
    expect(wrapper.findAll('[data-focus]')).toHaveLength(3)

    const text = wrapper.text()
    expect(text).not.toContain('正在刷新工作台摘要')
    expect(text).not.toContain('项待处理')
    expect(text).not.toContain('暂无可显示指标')
    expect(text).not.toContain('当前角色没有可汇总的跨域指标')
    expect(text).not.toContain('待办已清空')
    expect(text).not.toContain('没有未读消息')
    expect(text).not.toContain('没有未解除的设备预警')
    expect(text).not.toContain('今天没有待处理事项')
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
    // 英雄区：facade KPI + 待办 / 设备预警的权威总量
    expect(text).toContain('已下达工单')
    expect(text).toContain('7')
    expect(text).toContain('未关闭质量异常')
    expect(text).toContain('待办事项')
    expect(text).toContain('未解除设备预警')
    // 页头汇总 = 待办 2 + 未读 1 + 预警 1
    expect(text).toContain('4 项待处理')
    // 行动卡条目展示 facade 返回的人读编码，而不是内部 id
    expect(text).toContain('PO-260701-0001')
    expect(text).toContain('WO-260701-0001')
    expect(text).toContain('DEV-1001')
    expect(text).not.toContain('approval-1')
    expect(text).not.toContain('message-1')
    expect(text).not.toContain('设备停机影响')
    expect(text).not.toContain('Sensitive customer escalation')
    expect(text).not.toContain('Sensitive receivables amount')
    expect(text).not.toContain('business.inventory.ledger.read')
    expect(text).not.toContain('global-inventory-workbench-summary-not-connected')
  })

  // Owner 裁决（覆盖 MAN-153 的 dashboard-01 渐变条款）：工作台各卡一律纯色平面。
  it('builds the hero from library metric components and keeps every surface flat', async () => {
    const wrapper = mountWorkbench([
      'business.mes.work-orders.read',
      'business.quality.ncr.read',
      'business.approvals.read',
      'business.notification.messages.read',
      'business.iiot.alarms.read',
    ])
    await flushPromises()

    // 英雄区四张 KPI 卡是库件 NvMetricCard，构成卡是库件 NvMetricRing——不自绘卡片
    expect(wrapper.findAll('.nv-metric')).toHaveLength(4)
    expect(wrapper.findAll('.nv-ring-card')).toHaveLength(1)
    // 任何渐变填充都不许回潮
    expect(wrapper.html()).not.toContain('bg-gradient')
  })

  // 真机回归：demo 网关的审批链消息 resourceId 就是内部 GUID，status 是 `unread`。
  it('keeps internal GUIDs and raw item statuses out of the action cards', async () => {
    coladaState.queryData = {
      success: true,
      data: {
        kpis: [],
        todos: {
          status: 'available',
          total: 1,
          items: [
            {
              source: 'BusinessApproval',
              itemId: 'approval-9',
              itemType: 'purchase-order',
              status: 'pending',
              referenceId: '019f9c8b-88f8-71fd-98d2-686490f945b7',
            },
          ],
        },
        messages: {
          status: 'available',
          total: 1,
          unread: 1,
          items: [
            {
              messageId: '019f9c8b-d1b2-78e6-9f16-3d209e243a87',
              status: 'unread',
              severity: 'info',
              resourceType: 'approval-chain',
              resourceId: '019f9c8b-88f8-71fd-98d2-686490f945b7',
              createdAtUtc: '2026-07-26T03:50:36Z',
            },
          ],
        },
        alerts: { status: 'available', total: 0, critical: 0, items: [] },
        sourceStatuses: [],
      },
    }

    const wrapper = mountWorkbench([
      'business.approvals.read',
      'business.notification.messages.read',
    ])
    await flushPromises()

    const text = wrapper.text()
    expect(text).not.toContain('019f9c8b')
    // GUID 不可读 → 回落到业务口径，而不是把内部 id 摆到主行
    expect(text).toContain('审批流转')
    expect(text).toContain('审批 · 采购单据')
    // 条目状态用条目自己的词表：unread → 未读、pending → 待处理，不是来源状态的兜底
    expect(text).toContain('未读')
    expect(text).toContain('待处理')
    expect(text).not.toContain('待确认')
  })

  it('never claims the shop floor is clear when the summary failed to load', async () => {
    coladaState.queryError = new Error('gateway unreachable')
    coladaState.queryData = undefined

    const wrapper = mountWorkbench(['business.iiot.alarms.read'])
    await flushPromises()

    const text = wrapper.text()
    // 失败态只说"取不到、无法判断"，绝不出现任何安慰性结论
    expect(text).toContain('工作台摘要读取失败')
    expect(text).toContain('设备预警读取失败')
    expect(text).toContain('待办读取失败')
    expect(text).toContain('消息读取失败')
    expect(text).not.toContain('设备当前运行正常')
    expect(text).not.toContain('没有未解除的设备预警')
    expect(text).not.toContain('待办已清空')
    expect(text).not.toContain('没有未读消息')
    expect(text).not.toContain('今天没有待处理事项')
    // 读数一律 `—`，不拿 0 冒充"已接入且为零"
    expect(text).toContain('—')
    expect(text).not.toContain('0 项待处理')
    expect(text).toContain('待处理数量取不到')
    // 有重试出口
    expect(text).toContain('重试')
  })

  it('excludes unavailable sources from the pending total instead of counting them as zero', async () => {
    coladaState.queryData = {
      success: true,
      data: {
        kpis: [],
        todos: { status: 'available', total: 3, items: [] },
        // 未接入 / 无权限：不得折成 0 计入合计
        messages: { status: 'unsupported', total: 0, unread: 0, items: [] },
        alerts: { status: 'forbidden', total: 0, critical: 0, items: [] },
        sourceStatuses: [],
      },
    }

    const wrapper = mountWorkbench(['business.notification.tasks.read'])
    await flushPromises()

    const text = wrapper.text()
    // 合计只算可用的那一路，并注明还有几路没算进来
    expect(text).toContain('3 项待处理（另有 2 路取不到）')
    expect(text).toContain('今日待处理构成（部分来源不可用）')
    // 不可用的两路不说"清空 / 正常"
    expect(text).toContain('消息暂时无法统计')
    expect(text).toContain('设备预警暂时无法统计')
    expect(text).not.toContain('没有未读消息')
    expect(text).not.toContain('没有未解除的设备预警')
  })

  it('collapses shortcuts into permitted business-domain tiles', async () => {
    const wrapper = mountWorkbench(['business.inventory.ledger.read'])
    await flushPromises()

    const links = wrapper.findAll('a').map((link) => link.attributes('href'))
    // 磁贴落点 = 该域中当前角色第一个有权限的页面
    expect(links).toContain('/inventory/availability')
    expect(links).not.toContain('/mes/work-orders')
    expect(links).not.toContain('/quality/ncrs')

    const text = wrapper.text()
    expect(text).toContain('业务域入口')
    expect(text).toContain('库存管理')
    // 域内页面收纳在域后面，首屏不再平铺成文字链接海
    expect(text).not.toContain('库存移动')
    expect(text).not.toContain('工单与派工')
  })

  it('centers the business-facing empty states instead of ops-style placeholders', async () => {
    coladaState.queryData = {
      success: true,
      data: {
        kpis: [],
        todos: { status: 'available', total: 0, items: [] },
        messages: { status: 'available', total: 0, unread: 0, items: [] },
        alerts: { status: 'available', total: 0, critical: 0, items: [] },
        sourceStatuses: [],
      },
    }

    const wrapper = mountWorkbench(['business.iiot.alarms.read'])
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('待办已清空')
    expect(text).toContain('没有未读消息')
    expect(text).toContain('没有未解除的设备预警')
    expect(text).toContain('今天没有待处理事项')
    expect(text).not.toContain('审批和通知任务按当前用户过滤')
    expect(text).not.toContain('只展示消息状态，不展开消息标题')
    expect(text).not.toContain('来自设备运行事实的当前报警')
    expect(text).not.toContain('仅展示当前角色可进入的页面')
  })

  it('hides the ops-only source status panel on the workbench first screen', async () => {
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

    expect(wrapper.find('[data-source="BusinessERP"]').exists()).toBe(false)
    expect(wrapper.find('[data-source="BusinessScheduling"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('来源状态')
  })
})
