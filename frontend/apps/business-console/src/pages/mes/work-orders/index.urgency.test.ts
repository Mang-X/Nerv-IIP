import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { reactive, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import WorkOrdersListPage from './index.vue'
vi.mock('@nerv-iip/ui', async (original) => ({
  ...(await original<typeof import('@nerv-iip/ui')>()),
  NvRowActions: { template: '<div><slot /></div>' },
  NvDropdownMenuItem: {
    props: ['disabled'],
    template: '<button :disabled="disabled"><slot /></button>',
  },
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ push: vi.fn() }),
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}))

vi.mock('@/composables/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({ byReference: { value: new Map() }, refresh: vi.fn() }),
}))
vi.mock('@/components/urgency/OrderUrgencyBadge.vue', () => ({
  default: {
    props: ['orderReference', 'mode', 'urgency'],
    template:
      '<span data-testid="order-urgency" :data-ref="orderReference" :data-mode="mode">未计算</span>',
  },
}))

vi.mock('@/composables/mes/useMesReferenceLabels', async (orig) => ({
  ...(await orig<typeof import('@/composables/mes/useMesReferenceLabels')>()),
}))
vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveSku: (v?: string | null) => v ?? '无',
    resolveWorkCenter: (v?: string | null) => v ?? '无',
  }),
}))
vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({ resources: ref([]) }),
  useBusinessSkus: () => ({ skus: ref([]) }),
}))

// 急单表单的物料 ▸ 生产版本目录走 colada 读面（需要 pinia），本用例只看紧急度徽章，整体打桩。
vi.mock('@/composables/useMesPickerCatalog', () => ({
  useMesMaterialVersionCatalog: () => ({
    productionVersionLabel: () => '—',
    skuOptions: ref([]),
    skusPending: ref(false),
    productionVersionOptions: () => [],
    productionVersionsPending: ref(false),
  }),
}))

const workOrders = vi.hoisted(() => ({
  items: [] as Array<Record<string, unknown>>,
  refresh: vi.fn(),
}))
vi.mock('@/composables/mes/useProductionReportSerialOptions', () => ({
  useProductionReportSerialOptions: () => ({
    serialPolicy: ref('none'),
    labelTemplates: ref([]),
    serialOptionsReady: ref(true),
    serialOptionsPending: ref(false),
    refreshSerialOptions: vi.fn(),
  }),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  makeIdempotencyKey: (prefix: string) => `${prefix}-test`,
  // #1288 工具栏作业范围选择入口（MesWorkScopeSelect）
  useMesWorkScopeSelection: () => ({
    scopeOptions: ref([]),
    scopeSelectionValue: ref(undefined),
    scopeReady: ref(true),
    scopeMessage: ref(''),
    scopePending: ref(false),
    scopeUnavailable: ref(false),
    selectedScope: ref(undefined),
    principalIdentity: ref('principal-test'),
    requireSelectedScope: vi.fn(),
  }),
  useMesProductionReporting: () => ({
    recordProductionReport: vi.fn(),
    restoreProductionReport: vi.fn(),
    readProductionPrintStatus: vi.fn(),
    recordProductionReportError: ref(undefined),
    recordProductionReportPending: ref(false),
  }),
  useMesProductionMaterialLots: () => ({
    materialsReadPermission: ref(false),
    materialLotsPending: ref(false),
    materialLotsError: ref(undefined),
    availableMaterialLots: ref([]),
    refreshMaterialLots: vi.fn(),
  }),
  useMesScrapReasonCodes: () => ({
    qualityInspectionRecordsReadPermission: ref(false),
    scrapReasonCodesPending: ref(false),
    scrapReasonCodesError: ref(undefined),
    scrapReasonCodes: ref([]),
    refreshScrapReasonCodes: vi.fn(),
  }),
  // 急单表单的「工序任务」改成只选，列表页新引入了工序任务读面。
  useMesOperationTasks: () => ({
    filters: reactive({ organizationId: 'org', environmentId: 'dev', skip: 0, take: 200 }),
    operationTasks: ref([]),
    operationTasksError: ref(undefined),
    operationTasksPending: ref(false),
    operationTasksTotal: ref(0),
    refreshOperationTasks: vi.fn(),
    completeOperationTask: vi.fn(),
    pauseOperationTask: vi.fn(),
  }),
  useMesWorkOrders: () => ({
    createRushWorkOrder: vi.fn(),
    createRushWorkOrderError: ref(undefined),
    createRushWorkOrderPending: ref(false),
    filters: reactive({
      organizationId: 'org',
      environmentId: 'dev',
      status: undefined,
      skip: 0,
      take: 20,
    }),
    recordProductionReport: vi.fn(),
    recordProductionReportError: ref(undefined),
    recordProductionReportPending: ref(false),
    refreshWorkOrders: workOrders.refresh,
    workOrders: ref(workOrders.items),
    workOrdersError: ref(undefined),
    workOrdersHasFailedResponse: ref(false),
    workOrdersHasSuccessfulResponse: ref(true),
    workOrdersLastUpdatedAt: ref('2026-07-30T00:00:00.000Z'),
    workOrdersPending: ref(false),
    workOrdersTotal: ref(workOrders.items.length),
    workOrderReadScope: ref({
      kind: 'work-center',
      id: 'WC-A',
      displayName: '精加工一线',
    }),
    workOrderReadScopeMessage: ref(''),
    workOrderReadScopeReady: ref(true),
    workOrderManageScope: ref({ kind: 'work-center', id: 'WC-A', displayName: '精加工一线' }),
    workOrderManageScopeMessage: ref(''),
    workOrderManageScopePending: ref(false),
    workOrderManageScopeReady: ref(true),
  }),
  useMesWorkOrderTransformations: () => ({
    splitWorkOrder: vi.fn(),
    mergeWorkOrders: vi.fn(),
    readTransformation: vi.fn(),
    splitWorkOrderPending: ref(false),
    mergeWorkOrdersPending: ref(false),
  }),
}))

function mountList(permissions: string[] = []) {
  const pinia = createPinia()
  useAuthStore(pinia).$patch({
    principal: {
      principalId: 'u1',
      principalType: 'user',
      organizationId: 'org',
      environmentId: 'dev',
      loginName: 'operator',
      permissionCodes: ['business.mes.work-orders.read', ...permissions],
    },
  })
  return mount(WorkOrdersListPage, {
    global: {
      plugins: [pinia],
      stubs: {
        // 行内工单抽屉自带一整套 MES 查询，本用例只看紧急度徽章，整体桩掉。
        WorkOrderDetailSheet: true,
        SingleOrderSchedulingDialog: {
          name: 'SingleOrderSchedulingDialog',
          props: ['workOrderId', 'contextLabel', 'open'],
          emits: ['scheduled'],
          template:
            '<div data-testid="scheduling-target">{{ workOrderId }} {{ contextLabel }}</div>',
        },
        BusinessLayout: { template: '<main><slot /></main>' },
        NvPageHeader: { template: '<header><slot name="actions" /></header>' },
        NvToolbar: { template: '<div><slot name="filters" /><slot name="actions" /></div>' },
        NvDataTable: {
          props: ['rows', 'columns'],
          template:
            '<div><div v-for="(row, i) in rows" :key="i" data-testid="work-order-row" :data-status="row.status"><slot name="cell-status" :row="row" /><slot name="cell-urgency" :row="row" /><slot name="cell-completedQuantity" :row="row" /><slot name="cell-actions" :row="row" /></div></div>',
        },
        NvStatusBadge: { props: ['value', 'label'], template: '<span>{{ label ?? value }}</span>' },
        NvButton: { template: '<button><slot /></button>' },
        NvSelect: { template: '<div><slot /></div>' },
        NvSelectTrigger: { template: '<button><slot /></button>' },
        NvSelectContent: { template: '<div><slot /></div>' },
        NvSelectItem: { props: ['value'], template: '<div><slot /></div>' },
        // The barrel alias carries its own `name` (#2879), so `NvSelectValue` is what
        // test-utils matches — keying it by reka's `SelectValue` would miss.
        NvSelectValue: { template: '<span />' },
        NvInput: { template: '<input />' },
        RouterLink: { props: ['to'], template: '<a><slot /></a>' },
      },
    },
  })
}

describe('work-order list — shared urgency reference mapping', () => {
  it('maps the real work order id into the shared urgency badge with the selected mode', () => {
    workOrders.items = [
      { workOrderId: 'WO-20260722-001', skuId: 'FG-1', status: 'released', operationTasks: [] },
    ]
    const wrapper = mountList()

    const badge = wrapper.get('[data-testid="order-urgency"]')
    expect(badge.attributes('data-ref')).toBe('WO-20260722-001')
    expect(badge.attributes('data-mode')).toBe('level')
  })

  it('shows terminal semantics instead of in-progress urgency for finished work orders', () => {
    const terminalStatuses = ['completed', 'closed', 'cancelled', 'scrapped', 'split', 'merged']
    workOrders.items = [
      { workOrderId: 'WO-ACTIVE', skuId: 'FG-1', status: 'released', operationTasks: [] },
      ...terminalStatuses.map((status) => ({
        workOrderId: `WO-${status.toUpperCase()}`,
        skuId: 'FG-1',
        status,
        operationTasks: [],
      })),
    ]
    const wrapper = mountList()

    const activeRow = wrapper.get('[data-testid="work-order-row"][data-status="released"]')
    expect(activeRow.get('[data-testid="order-urgency"]').attributes('data-ref')).toBe('WO-ACTIVE')

    for (const status of terminalStatuses) {
      const row = wrapper.get(`[data-testid="work-order-row"][data-status="${status}"]`)
      expect(row.find('[data-testid="order-urgency"]').exists()).toBe(false)
      expect(row.text()).toContain('已结束')
    }
  })
})

describe('工单列表需求变更标记', () => {
  it('只给对应工单显示 MES 返回的变更与取消标记', () => {
    workOrders.items = [
      {
        workOrderId: 'WO-CHANGED',
        skuId: 'SKU-A',
        status: 'released',
        operationTasks: [],
        hasChangedDemand: true,
        hasCancelledDemand: false,
      },
      {
        workOrderId: 'WO-CANCELLED',
        skuId: 'SKU-A',
        status: 'released',
        operationTasks: [],
        hasChangedDemand: false,
        hasCancelledDemand: true,
      },
      {
        workOrderId: 'WO-PLAIN',
        skuId: 'SKU-A',
        status: 'released',
        operationTasks: [],
        hasChangedDemand: false,
        hasCancelledDemand: false,
      },
    ]
    const rows = mountList().findAll('[data-testid="work-order-row"]')
    expect(rows[0].text()).toContain('需求已变更')
    expect(rows[0].text()).not.toContain('需求已取消')
    expect(rows[1].text()).toContain('需求已取消')
    expect(rows[1].text()).not.toContain('需求已变更')
    expect(rows[2].text()).not.toContain('需求已变更')
    expect(rows[2].text()).not.toContain('需求已取消')
  })
})

describe('工单列表完成量与排产', () => {
  it('展示服务端完成量和拆合中文状态', () => {
    workOrders.items = [
      { workOrderId: 'WO-SPLIT', status: 'split', completedQuantity: 0.0004 },
      { workOrderId: 'WO-MERGED', status: 'merged', completedQuantity: 0 },
    ]
    const rows = mountList().findAll('[data-testid="work-order-row"]')
    expect(rows[0].text()).toContain('0.0004')
    expect(rows[0].text()).toContain('已拆分')
    expect(rows[1].text()).toContain('已合并')
    expect(rows[1].text()).toContain('0')
  })
  it('点击第二行固定第二张工单，成功排产后刷新列表', async () => {
    workOrders.refresh.mockClear()
    workOrders.items = [
      { workOrderId: 'WO-20260930-002', productionVersionId: 'PV-FG-1000', status: 'released' },
      {
        workOrderId: 'WO-20260930-003',
        workOrderNo: 'WO-20260930-003',
        productionVersionId: 'PV-FG-1000',
        status: 'released',
      },
    ]
    const wrapper = mountList(['business.scheduling.plans.manage'])
    const button = wrapper
      .findAll('button')
      .filter((button) => button.text().includes('对该单排产'))[1]!
    expect(button.element.disabled).toBe(false)
    await button.trigger('click')
    expect(wrapper.get('[data-testid="scheduling-target"]').text()).toContain('WO-20260930-003')
    wrapper.findComponent({ name: 'SingleOrderSchedulingDialog' }).vm.$emit('scheduled', 'PLAN-007')
    expect(workOrders.refresh).toHaveBeenCalledOnce()
  })
  it('只有读取权限时不允许列表发起排产', () => {
    workOrders.items = [
      { workOrderId: 'WO-20260930-003', productionVersionId: 'PV-FG-1000', status: 'released' },
    ]
    const wrapper = mountList()
    expect(
      wrapper.findAll('button').find((button) => button.text().includes('对该单排产'))!.element
        .disabled,
    ).toBe(true)
    expect(wrapper.find('[data-testid="scheduling-target"]').exists()).toBe(false)
  })
})
