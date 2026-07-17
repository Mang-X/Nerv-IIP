import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'

import OperationTasksPage from './operation-tasks.vue'
import PlansPage from './plans.vue'
import ReceiptsPage from './receipts.vue'
import TraceabilityPage from './traceability.vue'
import WorkOrdersPage from './work-orders/index.vue'

const routeState = vi.hoisted(() => ({
  query: {} as Record<string, string>,
}))

const routerState = vi.hoisted(() => ({
  push: vi.fn(),
}))

const mesSpies = vi.hoisted(() => ({
  createReceiptRequest: vi.fn(async () => undefined),
  refreshReceiptRequests: vi.fn(async () => undefined),
  retryInventoryPosting: vi.fn(async () => undefined),
  traceabilityFilters: undefined as
    | { batchOrSerial: string; materialLotId: string; mode: string; workOrderId: string }
    | undefined,
}))

vi.mock('vue-router', () => ({
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
  },
  useRoute: () => routeState,
  useRouter: () => routerState,
}))

vi.mock('@/utils/notify', () => ({ notifySuccess: vi.fn(), notifyError: vi.fn() }))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({ resources: ref([]) }),
  useBusinessSkus: () => ({ skus: ref([]) }),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  describeMesReadinessReason: (code: string) => ({
    code,
    label: code || '未检',
    nextStep: '请按质量或设备处理要求跟进。',
  }),
  makeIdempotencyKey: (prefix: string) => `${prefix}-test`,
  useMesWorkOrderProducedLots: () => ({
    // 单一产出批次自动选中，使完工入库提交用例可通过 canCreate（后端强制引用真实产出批次）。
    producedLots: ref([
      { producedLotNo: 'LOT-WO-001', reportNo: 'PRPT-1', goodQuantity: 10, remainingQuantity: 10 },
    ]),
    producedLotsError: ref(undefined),
    producedLotsPending: ref(false),
    refreshProducedLots: vi.fn(),
  }),
  useMesFinishedGoodsReceipts: () => ({
    createReceiptRequest: mesSpies.createReceiptRequest,
    createReceiptRequestError: ref(undefined),
    createReceiptRequestPending: ref(false),
    filters: {
      environmentId: 'dev',
      organizationId: 'org',
      status: undefined,
      take: 20,
    },
    receiptRequests: ref([]),
    receiptRequestsError: ref(undefined),
    receiptRequestsPending: ref(false),
    receiptRequestsTotal: ref(0),
    refreshReceiptRequests: mesSpies.refreshReceiptRequests,
    retryInventoryPosting: mesSpies.retryInventoryPosting,
    retryInventoryPostingError: ref(undefined),
    isRetrying: () => false,
  }),
  useMesOperationTasks: () => ({
    filters: {
      environmentId: 'dev',
      organizationId: 'org',
      status: undefined,
    },
    operationTasks: ref([
      {
        operationTaskId: 'OP-001-10',
        workOrderId: 'WO-001',
        status: 'ready',
        operationSequence: 10,
        workCenterId: 'WC-01',
      },
    ]),
    operationTasksError: ref(undefined),
    operationTasksPending: ref(false),
    operationTasksTotal: ref(1),
    refreshOperationTasks: vi.fn(),
  }),
  useMesCurrentOperationSops: () => ({
    filters: {
      environmentId: 'dev',
      organizationId: 'org',
      operationCode: '',
      workCenterCode: '',
    },
    currentSops: ref([]),
    currentSopsError: ref(undefined),
    currentSopsPending: ref(false),
    refreshCurrentSops: vi.fn(),
  }),
  useMesProductionPlans: () => ({
    convertPlanToWorkOrder: vi.fn(),
    convertPlanToWorkOrderError: ref(undefined),
    convertPlanToWorkOrderPending: ref(false),
    filters: {
      environmentId: 'dev',
      organizationId: 'org',
    },
    productionPlans: ref([
      {
        productionPlanId: 'PLAN-001',
        sourceSystem: 'sales-order',
        sourceDocumentId: 'SO-001',
        skuId: 'FG-001',
        plannedQuantity: 10,
        readinessStatus: 'Ready',
        plannedStartUtc: '2026-05-25T08:00:00.000Z',
      },
    ]),
    productionPlansError: ref(undefined),
    productionPlansPending: ref(false),
    productionPlansTotal: ref(1),
    refreshProductionPlans: vi.fn(),
  }),
  useMesTraceability: () => {
    const filters = reactive({
      batchOrSerial: '',
      materialLotId: '',
      mode: 'work-order',
      workOrderId: '',
    })
    mesSpies.traceabilityFilters = filters

    return {
      filters,
      refreshTraceability: vi.fn(),
      traceability: ref({ edges: [], nodes: [] }),
      traceabilityError: ref(undefined),
      traceabilityPending: ref(false),
    }
  },
  useMesWorkOrderDetail: () => ({
    detail: ref(null),
    detailError: ref(null),
    detailPending: ref(false),
    filters: reactive({ workOrderId: '' }),
  }),
  useMesWorkOrders: () => ({
    createRushWorkOrder: vi.fn(),
    createRushWorkOrderError: ref(undefined),
    createRushWorkOrderPending: ref(false),
    filters: {
      environmentId: 'dev',
      organizationId: 'org',
      status: undefined,
    },
    recordProductionReport: vi.fn(),
    recordProductionReportError: ref(undefined),
    recordProductionReportPending: ref(false),
    refreshWorkOrders: vi.fn(),
    workOrders: ref([
      {
        workOrderId: 'WO-001',
        skuId: 'FG-001',
        quantity: 10,
        status: 'ready',
        operationTasks: [
          {
            operationTaskId: 'OP-001-10',
            operationSequence: 10,
            status: 'ready',
            workCenterId: 'WC-01',
          },
        ],
      },
    ]),
    workOrdersError: ref(undefined),
    workOrdersPending: ref(false),
    workOrdersTotal: ref(1),
  }),
}))

const businessStubs = {
  BusinessActionSheet: {
    props: ['open', 'title', 'description'],
    template: '<section><h2>{{ title }}</h2><p>{{ description }}</p><slot /></section>',
  },
  BusinessContextBar: {
    template: '<section><slot /></section>',
  },
  BusinessEmptyState: {
    props: ['title', 'description', 'action'],
    template: '<div>{{ title }} {{ description }} {{ action }}</div>',
  },
  BusinessFormStatus: true,
  BusinessLayout: {
    template: '<main><slot /></main>',
  },
  BusinessMetricCell: {
    props: ['label', 'value', 'detail'],
    template: '<div>{{ label }} {{ value }} {{ detail }}</div>',
  },
  BusinessPageHeader: {
    props: ['domain', 'title', 'kicker', 'summary'],
    template: '<header><h1>{{ title }}</h1><p>{{ summary }}</p><slot name="actions" /></header>',
  },
  BusinessRowActions: {
    template: '<div><slot /></div>',
  },
  BusinessStatusBadge: {
    props: ['value'],
    template: '<span>{{ value }}</span>',
  },
  BusinessTablePagination: true,
}

const uiStubs = {
  // FE-2 block components (used by the migrated operation-tasks gold-standard page).
  PageHeader: {
    props: ['title', 'breadcrumbs', 'count'],
    template: '<header><h1>{{ title }}</h1><slot name="actions" /></header>',
  },
  SectionCards: {
    props: ['columns'],
    template: '<div><slot /></div>',
  },
  SectionCard: {
    props: ['description', 'value', 'hint', 'footnote', 'trend'],
    template: '<div>{{ description }} {{ value }} {{ hint }}</div>',
  },
  Toolbar: {
    props: ['search', 'searchPlaceholder'],
    template: '<div><slot name="filters" /><slot name="actions" /></div>',
  },
  NvDataTable: {
    props: ['rows', 'columns', 'rowKey', 'sort', 'clientSort', 'loading', 'emptyMessage'],
    template: `<div><template v-for="(row, i) in rows" :key="i">
      <slot name="cell-workOrderId" :row="row" />
      <slot name="cell-status" :row="row" />
      <slot name="cell-qualityStatus" :row="row" />
      <slot name="cell-actions" :row="row" />
    </template></div>`,
  },
  DataTablePagination: true,
  DialogRoot: {
    props: ['open'],
    template: '<div><slot /></div>',
  },
  NvDialogContent: {
    template: '<div><slot /></div>',
  },
  NvDialogHeader: {
    template: '<div><slot /></div>',
  },
  NvDialogTitle: {
    template: '<h2><slot /></h2>',
  },
  NvDialogDescription: {
    template: '<p><slot /></p>',
  },
  NvDialogFooter: {
    template: '<div><slot /></div>',
  },
  NvSheet: {
    props: ['open'],
    template: '<div><slot /></div>',
  },
  NvSheetContent: {
    template: '<div><slot /></div>',
  },
  NvSheetHeader: {
    template: '<div><slot /></div>',
  },
  NvSheetTitle: {
    template: '<h2><slot /></h2>',
  },
  NvSheetDescription: {
    template: '<p><slot /></p>',
  },
  NvSheetFooter: {
    template: '<div><slot /></div>',
  },
  RowActions: {
    props: ['label'],
    template: '<div><slot /></div>',
  },
  NvStatusBadge: {
    props: ['value'],
    template: '<span>{{ value }}</span>',
  },
  NvButton: {
    template: '<button v-bind="$attrs"><slot /></button>',
  },
  NvCheckbox: {
    template: '<input type="checkbox" />',
  },
  DropdownMenuItem: {
    template: '<button v-bind="$attrs"><slot /></button>',
  },
  DropdownMenuSeparator: true,
  Field: {
    template: '<div><slot /></div>',
  },
  FieldDescription: {
    template: '<p><slot /></p>',
  },
  FieldGroup: {
    template: '<div><slot /></div>',
  },
  FieldLabel: {
    template: '<label><slot /></label>',
  },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvSelect: {
    template: '<div><slot /></div>',
  },
  NvSelectContent: {
    template: '<div><slot /></div>',
  },
  NvSelectItem: {
    props: ['value'],
    template: '<div><slot /></div>',
  },
  NvSelectTrigger: {
    template: '<button><slot /></button>',
  },
  SelectValue: {
    props: ['placeholder'],
    template: '<span>{{ placeholder }}</span>',
  },
  Spinner: true,
  Table: {
    template: '<table><slot /></table>',
  },
  TableBody: {
    template: '<tbody><slot /></tbody>',
  },
  TableCell: {
    template: '<td><slot /></td>',
  },
  TableEmpty: {
    template: '<tr><td><slot /></td></tr>',
  },
  TableHead: {
    template: '<th><slot /></th>',
  },
  TableHeader: {
    template: '<thead><slot /></thead>',
  },
  TableRow: {
    template: '<tr><slot /></tr>',
  },
}

function mountMesPage(component: unknown) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'u1',
      principalType: 'user',
      organizationId: 'org',
      environmentId: 'dev',
      loginName: 'op',
      permissionCodes: ['business.mes.receipts.manage'],
    },
  })
  return mount(component, {
    global: {
      plugins: [pinia],
      stubs: {
        ...businessStubs,
        ...uiStubs,
        RouterLink: {
          props: ['to'],
          template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
        },
      },
    },
  })
}

function expectNoForbiddenVisibleTerms(text: string) {
  expect(text).not.toMatch(
    /demo|mock|seed|样例|用于验证|接口|契约|组织|环境|sourceSystem|operationId|联动测试|内置|幂等键/i,
  )
}

describe('MES workflow copy', () => {
  beforeEach(() => {
    routeState.query = {}
    routerState.push.mockReset()
    mesSpies.createReceiptRequest.mockClear()
    mesSpies.refreshReceiptRequests.mockClear()
    mesSpies.traceabilityFilters = undefined
  })

  it('keeps work-order reporting business-facing and row-context driven', () => {
    routeState.query = {
      operationTaskId: 'OP-001-10',
      workOrderId: 'WO-001',
    }

    const wrapper = mountMesPage(WorkOrdersPage)

    expect(wrapper.text()).toContain('工单与工序来自所选行')
    expect(wrapper.find('#report-work-order').attributes('readonly')).toBeDefined()
    expect(wrapper.find('#report-operation-task').attributes('readonly')).toBeDefined()
    expectNoForbiddenVisibleTerms(wrapper.text())
  })

  it('points formal scheduling output from work orders to the Scheduling workbench', () => {
    const wrapper = mountMesPage(WorkOrdersPage)
    const schedulingLink = wrapper
      .findAll('[data-router-link]')
      .find((link) => link.attributes('data-to') === '"/scheduling"')

    expect(wrapper.text()).toContain('正式排产输出')
    expect(wrapper.text()).not.toContain('排程结果')
    expect(schedulingLink).toBeDefined()
    expect(schedulingLink!.text()).toContain('排产工作台')
  })

  it('keeps operation tasks focused on supported row actions', () => {
    const wrapper = mountMesPage(OperationTasksPage)

    expect(wrapper.text()).toContain('报工')
    expect(wrapper.text()).not.toContain('带入工单报工')
    expect(wrapper.text()).not.toContain('进入执行')
    expectNoForbiddenVisibleTerms(wrapper.text())
  })

  it('carries operation-task context into the work-order reporting sheet route', async () => {
    const wrapper = mountMesPage(OperationTasksPage)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('报工'))!
      .trigger('click')

    expect(routerState.push).toHaveBeenCalledWith({
      path: '/mes/work-orders',
      query: {
        operationTaskId: 'OP-001-10',
        workCenterId: 'WC-01',
        workOrderId: 'WO-001',
      },
    })
  })

  it('keeps production plans business-facing without manual system number generation', () => {
    const wrapper = mountMesPage(PlansPage)

    expect(wrapper.text()).toContain('生产计划')
    expect(wrapper.text()).toContain('转工单')
    expect(wrapper.find('#add-plan-id').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('生成')
    expectNoForbiddenVisibleTerms(wrapper.text())
  })

  it('requires finished-goods receipt context instead of hand-entered system fields', () => {
    const wrapper = mountMesPage(ReceiptsPage)

    expect(wrapper.text()).toContain('从工单详情发起')
    expect(wrapper.find('#receipt-work-order').attributes('readonly')).toBeDefined()
    expect(wrapper.find('#receipt-sku').attributes('readonly')).toBeDefined()
    expectNoForbiddenVisibleTerms(wrapper.text())
  })

  it('submits finished-goods receipt context with unit cost', async () => {
    routeState.query = {
      quantity: '10',
      skuId: 'FG-001',
      workOrderId: 'WO-001',
    }
    const wrapper = mountMesPage(ReceiptsPage)

    await wrapper.find('#receipt-unit-cost').setValue('12.34')
    await wrapper.find('form').trigger('submit')

    expect(mesSpies.createReceiptRequest).toHaveBeenCalledWith(
      expect.objectContaining({
        environmentId: 'dev',
        organizationId: 'org',
        quantity: 10,
        skuId: 'FG-001',
        unitCost: 12.34,
        uomCode: 'EA',
        workOrderId: 'WO-001',
      }),
    )
  })

  it('links non-work-order traceability to scan records without hardcoding a workflow filter', () => {
    routeState.query = { batchOrSerial: 'LOT-001', mode: 'batch' }
    const wrapper = mountMesPage(TraceabilityPage)

    const scanLink = wrapper.get('[data-router-link]')
    const target = scanLink.attributes('data-to')

    expect(target).toContain('"path":"/barcode/scans"')
    expect(target).toContain('"sourceDocumentId":"LOT-001"')
    expect(target).toContain('"scannedValue":"LOT-001"')
    expect(target).not.toContain('sourceWorkflow')
  })

  it('initializes traceability filters from an inventory batch or serial route', () => {
    routeState.query = { batchOrSerial: 'SN-001', mode: 'batch' }

    mountMesPage(TraceabilityPage)

    expect(mesSpies.traceabilityFilters).toEqual(
      expect.objectContaining({
        batchOrSerial: 'SN-001',
        materialLotId: 'SN-001',
        mode: 'batch',
      }),
    )
  })
})
