import { mount } from '@vue/test-utils'
import type { BusinessConsoleMesProductionReportRow } from '@nerv-iip/api-client'
import { createPinia } from 'pinia'
import { shallowRef } from 'vue'
import { beforeEach, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import ProductionReportsPage from './production-reports.vue'

const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i

// 报工列表 mock:同页只放三行,故意让"已冲销原单"(B)与其冲销行、"冲销行"(C)与其原单**不在同页**,
// 以验证已冲销判定/互链读服务端逐行字段(reversalReportNo / reversedReportNo)与跨页点击定位。
interface ReportFixture extends BusinessConsoleMesProductionReportRow {
  productionReportId: string
  reportNo: string
  workOrderId: string
  operationTaskId: string
  goodQuantity: number
  scrapQuantity: number
  reworkQuantity: number
  reportedAtUtc: string
  workOrderStatus: string
  reportedByDisplayName?: string | null
}

const rows: ReportFixture[] = [
  // A 可冲销原单(工单在制、无冲销互链字段)
  {
    reportedBy: 'user-admin',
    reportedByDisplayName: '王建国',
    productionReportId: 'pr-a',
    reportNo: 'PRPT-000001',
    workOrderId: 'WO-1',
    operationTaskId: 'WO-1-OP-10',
    goodQuantity: 12,
    scrapQuantity: 0,
    reworkQuantity: 0,
    reportedAtUtc: '2026-07-12T02:00:00Z',
    workOrderStatus: 'started',
    operationActualLaborHours: 1.25,
    operationActualMachineHours: 0.5,
  },
  // B 已冲销原单:reversalReportNo 指向冲销它的负向记录(该记录不在本页),服务端逐行反查得出
  {
    productionReportId: 'pr-b',
    reportNo: 'PRPT-000002',
    workOrderId: 'WO-2',
    operationTaskId: 'WO-2-OP-10',
    goodQuantity: 8,
    scrapQuantity: 0,
    reworkQuantity: 0,
    reportedAtUtc: '2026-07-12T01:00:00Z',
    workOrderStatus: 'started',
    reversalReportNo: 'PRPT-000900',
    operationActualLaborHours: 0,
    operationActualMachineHours: 0,
  },
  // C 冲销记录行:reversedReportNo 指向被冲销的原单(该原单不在本页)
  {
    productionReportId: 'pr-c',
    reportNo: 'PRPT-000901',
    workOrderId: 'WO-3',
    operationTaskId: 'WO-3-OP-10',
    goodQuantity: -5,
    scrapQuantity: 0,
    reworkQuantity: 0,
    reportedAtUtc: '2026-07-12T03:00:00Z',
    workOrderStatus: 'started',
    reversedReportNo: 'PRPT-000899',
    reversalReason: '误报工',
    operationActualLaborHours: null,
    operationActualMachineHours: null,
  },
]

// 按对方报工单号 keyword 过滤时,服务端应返回对方那一行(模拟跨页定位后端返回)。
const counterpartRows: Record<string, (typeof rows)[number][]> = {
  // B(PRPT-000002)的冲销负向记录,平时不在本页
  'PRPT-000900': [
    {
      productionReportId: 'pr-rev-b',
      reportNo: 'PRPT-000900',
      workOrderId: 'WO-2',
      operationTaskId: 'WO-2-OP-10',
      goodQuantity: -8,
      scrapQuantity: 0,
      reworkQuantity: 0,
      reportedAtUtc: '2026-07-12T01:30:00Z',
      workOrderStatus: 'started',
      reversedReportNo: 'PRPT-000002',
      reversalReason: '误报工',
      operationActualLaborHours: 0,
      operationActualMachineHours: 0,
    },
  ],
  // C(PRPT-000901)冲销的原单,平时不在本页
  'PRPT-000899': [
    {
      productionReportId: 'pr-orig-c',
      reportNo: 'PRPT-000899',
      workOrderId: 'WO-3',
      operationTaskId: 'WO-3-OP-10',
      goodQuantity: 5,
      scrapQuantity: 0,
      reworkQuantity: 0,
      reportedAtUtc: '2026-07-12T02:30:00Z',
      workOrderStatus: 'started',
      reversalReportNo: 'PRPT-000901',
      operationActualLaborHours: null,
      operationActualMachineHours: null,
    },
  ],
}

// filters / pending 在 mock 工厂里用真实 reactive/shallowRef 创建,供测试可控与断言。
const mesState = vi.hoisted(() => ({
  reverseProductionReport: vi.fn(
    async (
      _reportNo: string,
      _body: { reason: string; reversedAtUtc?: string; idempotencyKey?: string },
    ) => ({
      data: { reportNo: 'PRPT-000902' },
    }),
  ),
  filters: undefined as unknown as Record<string, string | number>,
  pending: undefined as unknown as { value: boolean },
  detail: undefined as unknown as { value: unknown },
  detailPending: undefined as unknown as { value: boolean },
  detailError: undefined as unknown as { value: unknown },
  activateReverseDetail: vi.fn(),
  deactivateReverseDetail: vi.fn(),
  guardRows: null as Array<Record<string, unknown>> | null,
  candidateRows: [] as Array<Record<string, unknown>>,
  workOrderRows: undefined as unknown as { value: Array<Record<string, unknown>> },
  promoteCandidate: vi.fn(
    async (_candidateId: string, _workOrderId: string, _taskId: string) => {},
  ),
  catalogResolved: true,
}))

vi.mock('@/composables/master-data/useMasterDataDisplayNames', () => ({
  useMasterDataDisplayNames: () => ({
    resolveDevice: () => (mesState.catalogResolved ? '五轴加工中心' : undefined),
    resolveUser: (id?: string | null) =>
      ({ 'user-operator': '王建国', 'user-detail-operator': '李俊华' } as Record<string, string>)[
        id ?? ''
      ],
  }),
}))

vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveSkuLabel: (value?: string | null) => {
      if (!mesState.catalogResolved) return '未指定物料'
      return value?.startsWith('019fbb41-') ? '轴承钢' : (value ?? '未指定物料')
    },
  }),
}))

const routerState = vi.hoisted(() => ({
  push: vi.fn(),
  query: {} as Record<string, string | string[]>,
}))
vi.mock('vue-router', () => ({
  useRouter: () => routerState,
  useRoute: () => routerState,
  RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
}))

vi.mock('@/composables/mes/useProductionReportSerialOptions', () => ({
  useProductionReportSerialOptions: () => ({
    serialPolicy: shallowRef('none'),
    labelTemplates: shallowRef([]),
    serialOptionsReady: shallowRef(true),
    serialOptionsPending: shallowRef(false),
    refreshSerialOptions: vi.fn(),
  }),
}))

vi.mock('@/composables/mes', async () => {
  const { reactive, shallowRef, computed } = await import('vue')
  mesState.filters = reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skip: 0,
    take: 100,
    keyword: '',
  })
  mesState.pending = shallowRef(false)
  mesState.detail = shallowRef()
  mesState.workOrderRows = shallowRef([])
  mesState.detailPending = shallowRef(false)
  mesState.detailError = shallowRef()
  return {
    makeIdempotencyKey: (prefix: string) => `${prefix}-test`,
    useMesProductionReporting: () => ({
      recordProductionReport: vi.fn(),
      restoreProductionReport: vi.fn(),
      readProductionPrintStatus: vi.fn(),
      recordProductionReportError: shallowRef(undefined),
      recordProductionReportPending: shallowRef(false),
      reportScopeMessage: shallowRef(''),
      reportScopePending: shallowRef(false),
      reportScopeReady: shallowRef(true),
      refreshProductionReportState: vi.fn(),
    }),
    useMesProductionMaterialLots: () => ({
      materialsReadPermission: shallowRef(false),
      materialLotsPending: shallowRef(false),
      materialLotsError: shallowRef(undefined),
      availableMaterialLots: shallowRef([]),
      refreshMaterialLots: vi.fn(),
    }),
    useMesScrapReasonCodes: () => ({
      qualityInspectionRecordsReadPermission: shallowRef(false),
      scrapReasonCodesPending: shallowRef(false),
      scrapReasonCodesError: shallowRef(undefined),
      scrapReasonCodes: shallowRef([]),
      refreshScrapReasonCodes: vi.fn(),
    }),
    useMesProductionReportDetail: (reportNo: () => string | null) => ({
      detail: computed(() =>
        reportNo()
          ? {
              report: { ...rows[0], reportNo: reportNo() },
              consumedMaterialLots: [],
              laborAllocations: [],
            }
          : undefined,
      ),
      detailError: shallowRef(undefined),
      detailPending: shallowRef(false),
      refresh: vi.fn(),
    }),
    useMesProductionReports: () => ({
      filters: mesState.filters,
      // 按 keyword 过滤时返回对方那一行(模拟服务端跨页返回),否则返回本页三行——让跨页定位的
      // watch(flush:'post')→ 滚动/高亮后半条链在测试里真实触发。
      productionReports: computed(() => {
        if (mesState.guardRows) return mesState.guardRows
        const keyword = String(mesState.filters.keyword ?? '')
        return keyword && counterpartRows[keyword] ? counterpartRows[keyword] : rows
      }),
      productionReportsError: shallowRef(undefined),
      productionReportsPending: shallowRef(false),
      productionReportsTotal: computed(() => rows.length),
      refreshProductionReports: vi.fn(),
      reverseProductionReport: mesState.reverseProductionReport,
      reverseProductionReportPending: mesState.pending,
      reverseProductionReportDetail: mesState.detail,
      reverseProductionReportDetailPending: mesState.detailPending,
      reverseProductionReportDetailError: mesState.detailError,
      activateReverseDetail: mesState.activateReverseDetail,
      deactivateReverseDetail: mesState.deactivateReverseDetail,
    }),
    // main 上并行合入的遥测报工候选队列(同页):这些冲销相关用例不驱动它,给个空队列桩即可。
    useMesWorkOrders: () => ({
      filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 50 }),
      workOrders: mesState.workOrderRows,
      workOrdersPending: shallowRef(false),
      workOrdersTotal: computed(() => mesState.workOrderRows.value.length),
    }),
    useMesTelemetryProductionReportCandidates: () => ({
      filters: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'pending-confirmation',
        skip: 0,
        take: 100,
      }),
      candidates: computed(() => mesState.candidateRows),
      total: computed(() => 0),
      pending: shallowRef(false),
      error: shallowRef(undefined),
      refresh: vi.fn(),
      promote: mesState.promoteCandidate,
      dismiss: vi.fn(async () => undefined),
      actionPending: computed(() => false),
    }),
  }
})

// DataTablePro 桩:逐行渲染 cell-reportNo 与 cell-actions 两个插槽,便于断言徽章/互链/操作按钮。
const tableStub = {
  props: ['columns', 'rows'],
  template: `
    <section data-testid="data-table">
      <span v-for="column in columns" :key="column.key">{{ column.header }}</span>
      <div v-for="row in rows" :key="row.productionReportId" data-testid="row">
        <span v-for="column in columns" :key="column.key" :data-column="column.key">
          {{ column.accessor ? column.accessor(row) : '' }}
        </span>
        <slot name="cell-reportNo" :row="row" />
        <slot name="cell-workOrderId" :row="row" />
        <slot name="cell-actualHours" :row="row" />
        <slot name="cell-actions" :row="row" />
      </div>
    </section>
  `,
}

function mountReports(permissionCodes: string[]) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'user-1',
      principalType: 'user',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      loginName: 'operator',
      permissionCodes,
    },
  })

  // NvUI 组件在测试里按其底层 Pro/reka 组件名 stub(Nv* 只是导出别名,VTU 按组件 name 匹配)。
  // attachTo document.body:页面跨页定位用 document.querySelector 找 [data-report-no] 行,游离 DOM 查不到。
  return mount(ProductionReportsPage, {
    attachTo: document.body,
    global: {
      plugins: [pinia],
      stubs: {
        // NvUI 组件按其底层组件名 stub。#787 已把 pro/ 重命名为 pc/ 且组件文件 XxxPro.vue → NvXxx.vue,
        // 故 __name 就是 Nv* 名;Tooltip 三件套原本只有 reka 名，#2879 给 barrel 补了 name 之后
        // 同样按 Nv 名解析，故这里的键全部写 Nv 名。
        BusinessLayout: { template: '<main><slot /></main>' },
        WorkOrderQuickView: true,
        PageHeader: { template: '<header><slot name="actions" /></header>' },
        NvDataTable: tableStub,
        NvButton: {
          props: ['disabled'],
          template: '<button :disabled="disabled"><slot /></button>',
        },
        NvStatusBadge: {
          props: ['label'],
          template: '<span data-testid="badge">{{ label }}</span>',
        },
        NvTooltipProvider: { template: '<div><slot /></div>' },
        NvTooltip: { template: '<div><slot /></div>' },
        NvTooltipTrigger: { template: '<div><slot /></div>' },
        NvTooltipContent: { template: '<div><slot /></div>' },
        // 冲销确认框内容不渲染(否则 reka Dialog 内层需 DialogRoot context);key 复用/关闭路径测试
        // 直接驱动 setup 方法(openReverse/submitReverse/onReverseOpenChange),不依赖弹窗 DOM。
        NvAlertDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
        NvAlertDialogContent: { template: '<div><slot /></div>' },
        NvAlertDialogHeader: { template: '<div><slot /></div>' },
        NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
        NvAlertDialogDescription: { template: '<p><slot /></p>' },
        NvAlertDialogFooter: { template: '<footer><slot /></footer>' },
        NvFieldGroup: { template: '<div><slot /></div>' },
        NvField: { template: '<div><slot /></div>' },
        NvFieldLabel: { template: '<label><slot /></label>' },
        // main 合入的候选队列 section 在主模板直接用 Select/Input,需 stub 避免缺 context 破坏整页渲染
        NvSelect: { template: '<div><slot /></div>' },
        NvSelectTrigger: { template: '<div><slot /></div>' },
        NvSelectContent: { template: '<div><slot /></div>' },
        NvSelectItem: { template: '<div><slot /></div>' },
        NvSelectValue: { template: '<span />' },
        NvInput: { template: '<input />' },
        // 选择器按「选项 + 选中值 + 禁用」三件事桩成原生 select，便于驱动选择并读出候选。
        NvEntityPicker: {
          props: ['modelValue', 'options', 'disabled', 'ariaLabel'],
          emits: ['update:modelValue'],
          template: `
            <select
              :aria-label="ariaLabel"
              :value="modelValue"
              :disabled="disabled"
              @change="$emit('update:modelValue', $event.target.value)"
            >
              <option value=""></option>
              <option v-for="o in options" :key="o.value" :value="o.value">{{ o.label }}</option>
            </select>
          `,
        },
        NvDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
        NvDialogContent: { template: '<section role="dialog"><slot /></section>' },
        NvDialogHeader: { template: '<header><slot /></header>' },
        NvDialogTitle: { template: '<h2><slot /></h2>' },
        NvDialogDescription: { template: '<p><slot /></p>' },
        NvDialogFooter: { template: '<footer><slot /></footer>' },
        NvSpinner: true,
      },
    },
  })
}

beforeEach(() => {
  routerState.query = {}
  mesState.reverseProductionReport.mockReset()
  mesState.reverseProductionReport.mockResolvedValue({ data: { reportNo: 'PRPT-000902' } })
  if (mesState.filters) {
    mesState.filters.keyword = ''
    mesState.filters.skip = 0
  }
  if (mesState.pending) mesState.pending.value = false
  if (mesState.detail) mesState.detail.value = undefined
  if (mesState.detailPending) mesState.detailPending.value = false
  if (mesState.detailError) mesState.detailError.value = undefined
  mesState.activateReverseDetail.mockClear()
  mesState.activateReverseDetail.mockImplementation((reportNo: string) => {
    if (
      (mesState.detail.value as { report?: { reportNo?: string } } | undefined)?.report
        ?.reportNo === reportNo
    )
      return
    const report = rows.find((row) => row.reportNo === reportNo) ?? { ...rows[0], reportNo }
    mesState.detail.value = { report, consumedMaterialLots: [] }
  })
  mesState.deactivateReverseDetail.mockClear()
  mesState.guardRows = null
  mesState.candidateRows = []
  if (mesState.workOrderRows) mesState.workOrderRows.value = []
  mesState.promoteCandidate.mockClear()
  mesState.catalogResolved = true
  // jsdom 未实现 scrollIntoView,定义为可断言的 mock(跨页定位滚动)
  Element.prototype.scrollIntoView = vi.fn()
  // attachTo 会把上一个 wrapper 的 DOM 留在 body,清掉避免跨用例串扰
  document.body.innerHTML = ''
})

export { UUID_PATTERN, rows, mesState, mountReports, routerState }
