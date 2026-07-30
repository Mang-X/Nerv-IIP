import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import PlanningWorkbench from './PlanningWorkbench.vue'
import { useAuthStore } from '@/stores/auth'

vi.mock('@/composables/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({ byReference: { value: new Map() }, refresh: vi.fn() }),
}))
// 图表面板依赖真实 NvBarChart（unovis），工作台测试只关心装配，桩掉即可。
vi.mock('@/components/planning/PlanningTimePhasedPanel.vue', () => ({
  default: {
    props: [
      'demands',
      'mpsBuckets',
      'suggestions',
      'suggestionRunId',
      'suggestionRunLabel',
      'pending',
      'errorMessage',
      'skuLabel',
    ],
    template: '<div data-testid="time-phased-panel" :data-run-id="suggestionRunId" />',
  },
}))
vi.mock('@/components/planning/PlanningRunSuggestionChart.vue', () => ({
  default: {
    props: ['run', 'suggestions', 'pending'],
    template: '<div data-testid="run-suggestion-chart" :data-run-id="run?.runId" />',
  },
}))
vi.mock('@/components/urgency/OrderUrgencyBadge.vue', () => ({
  default: {
    props: ['orderReference', 'mode', 'urgency'],
    template:
      '<span data-testid="order-urgency" :data-ref="orderReference" :data-mode="mode">未计算</span>',
  },
}))

const routerPush = vi.hoisted(() => vi.fn())
const planningSpies = vi.hoisted(() => ({
  runMrp: vi.fn(async () => undefined),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}))

vi.mock('@/composables/useBusinessPlanning', async () => {
  const { reactive, shallowRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    SUGGESTION_REJECT_REASON_MAX_LENGTH: 128,
    useBusinessPlanning: () => ({
      acceptSuggestion: vi.fn(),
      acceptSuggestionError: shallowRef(null),
      acceptSuggestionPending: shallowRef(false),
      createMpsBucket: vi.fn(),
      createMpsBucketError: shallowRef(null),
      createMpsBucketPending: shallowRef(false),
      createDemandError: shallowRef(null),
      createDemandPending: shallowRef(false),
      createOrUpdateDemand: vi.fn(),
      demandForm: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        demandType: 'forecast',
        sourceReference: '',
        skuCode: '',
        uomCode: '',
        siteCode: '',
        quantity: 0,
        dueDate: '2026-06-01',
        idempotencyKey: '',
      }),
      demands: shallowRef([
        {
          demandSourceId: 'demand-001',
          sourceReference: 'SO-DEMO-001',
          sourceLineReference: '10',
          customerCode: 'CUST-001',
          sourceVersion: 3,
          sourceStatus: 'active',
          demandType: 'sales-order',
          skuCode: 'SKU-FG-1000',
          uomCode: 'pcs',
          siteCode: 'SITE-01',
          quantity: 2,
          dueDate: '2026-08-15',
        },
      ]),
      demandsError: shallowRef(null),
      demandsPending: shallowRef(false),
      mrpRuns: shallowRef([
        {
          runId: 'run-001',
          horizonStart: '2026-06-01',
          horizonEnd: '2026-06-30',
          status: 'Completed',
          demandCount: 1,
          availabilityCount: 1,
          suggestionCount: 1,
          hasInputDegradation: false,
          inputDegradationSources: [],
        },
      ]),
      mrpRunsError: shallowRef(null),
      mrpRunsPending: shallowRef(false),
      mpsBuckets: shallowRef([]),
      mpsBucketsError: shallowRef(null),
      mpsBucketsPending: shallowRef(false),
      mpsForm: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: '',
        uomCode: '',
        siteCode: '',
        bucketDate: '2026-06-01',
        quantity: 0,
      }),
      releaseMpsBucket: vi.fn(),
      releaseMpsBucketError: shallowRef(null),
      releaseMpsBucketPending: shallowRef(false),
      reviewMpsBucket: vi.fn(),
      reviewMpsBucketError: shallowRef(null),
      reviewMpsBucketPending: shallowRef(false),
      pegging: shallowRef([
        {
          suggestionId: 'suggestion-001',
          peggingType: 'demand',
          demandSourceReference: 'SO-1001',
          sourceType: 'sales',
          parentSkuCode: 'FG-SHOCK',
          componentSkuCode: null,
          quantity: 10,
          grossDemandQuantity: 10,
          productionVersionReference: 'PV-FG',
          manufacturingBomReference: 'MBOM-FG:001',
          routingReference: 'ROUTING-FG',
        },
      ]),
      peggingPending: shallowRef(false),
      refreshPlanning: vi.fn(),
      rejectSuggestion: vi.fn(),
      rejectSuggestionError: shallowRef(null),
      rejectSuggestionPending: shallowRef(false),
      runMrp: planningSpies.runMrp,
      runMrpError: shallowRef(null),
      runMrpPending: shallowRef(false),
      runRequest: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        horizonStart: '2026-06-01',
        horizonEnd: '2026-06-30',
      }),
      runSelection: reactive({ runId: 'run-001' }),
      suggestionFilters: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'open',
      }),
      suggestionTypeFilter: reactive({ type: 'all' }),
      suggestions: shallowRef([
        {
          suggestionId: 'suggestion-001',
          runId: 'run-001',
          suggestionType: 'planned-work-order',
          skuCode: 'FG-SHOCK',
          uomCode: 'pcs',
          siteCode: 'SITE-01',
          quantity: 4,
          requiredDate: '2026-06-01',
          status: 'Open',
          reasonCode: 'net-requirement',
          netRequirementExplanation: {
            grossDemandQuantity: 10,
            onHandQuantity: 8,
            reservedQuantity: 0,
            availableToNetQuantity: 6,
            scheduledReceiptQuantity: 0,
            safetyStockQuantity: 2,
            netRequirementQuantity: 4,
            plannedQuantity: 4,
            scrapRate: 0,
            yieldRate: 1,
            primarySourceType: 'demand',
            formula: '10 - 6 - 0 = 4',
            degradationSources: [],
          },
        },
        {
          suggestionId: 'suggestion-002',
          runId: 'run-001',
          suggestionType: 'planned-purchase',
          skuCode: 'RM-SHOCK',
          uomCode: 'pcs',
          siteCode: 'SITE-01',
          quantity: 27.5,
          requiredDate: '2026-06-01',
          status: 'Open',
          reasonCode: 'component-net-requirement',
          netRequirementExplanation: {
            grossDemandQuantity: 27.5,
            onHandQuantity: 0,
            reservedQuantity: 0,
            availableToNetQuantity: 0,
            scheduledReceiptQuantity: 0,
            safetyStockQuantity: 0,
            netRequirementQuantity: 27.5,
            plannedQuantity: 27.5,
            scrapRate: 0.1,
            yieldRate: 0.8,
            primarySourceType: 'component',
            formula: '27.5 - 0 - 0 = 27.5; scrap/yield 0.1/0.8',
            degradationSources: [],
          },
        },
        {
          suggestionId: 'suggestion-003',
          runId: 'run-001',
          suggestionType: 'reschedule-out',
          skuCode: 'FG-SHOCK',
          uomCode: 'pcs',
          siteCode: 'SITE-01',
          quantity: 8,
          requiredDate: '2026-06-20',
          status: 'Open',
          reasonCode: 'scheduled-receipt-early',
          netRequirementExplanation: null,
        },
        {
          // 已接受并承接成 MES 工单的生产建议：这一行才有可排的单（MAN-694 / #1262）。
          suggestionId: 'suggestion-004',
          runId: 'run-001',
          suggestionType: 'planned-work-order',
          skuCode: 'FG-SHOCK',
          uomCode: 'pcs',
          siteCode: 'SITE-01',
          quantity: 4,
          requiredDate: '2026-06-05',
          status: 'Accepted',
          reasonCode: 'net-requirement',
          netRequirementExplanation: null,
          downstreamService: 'BusinessMes',
          downstreamDocumentType: 'WorkOrder',
          downstreamDocumentId: 'WO-2026-0007',
        },
      ]),
      suggestionsError: shallowRef(null),
      suggestionsPending: shallowRef(false),
    }),
  }
})

vi.mock('@/composables/useBusinessMasterData', async () => {
  const { shallowRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    useBusinessMasterDataResources: () => ({ resources: shallowRef([]) }),
    useBusinessSkus: () => ({ skus: shallowRef([]) }),
  }
})

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: routerPush }),
}))

// 反馈走真实分层透传（notifyOperationFailure / inlineErrorMessage），只把 toast 换成 spy。
vi.mock('@/utils/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/notify')>()),
  notifyError: vi.fn(),
}))

vi.mock('@nerv-iip/ui', async () => {
  const { defineComponent, h } = await vi.importActual<typeof import('vue')>('vue')
  const Shell = defineComponent({ template: '<div><slot /><slot name="actions" /></div>' })
  const Button = defineComponent({
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  })
  const DataTable = defineComponent({
    props: {
      columns: { type: Array, default: () => [] },
      rows: { type: Array, default: () => [] },
    },
    setup(props, { slots }) {
      return () =>
        h(
          'div',
          props.rows.flatMap((row: any) =>
            props.columns.map((column: any) => {
              const slot = slots[`cell-${column.key}`]
              return h(
                'div',
                { class: `cell-${column.key}` },
                slot ? slot({ row }) : String(row[column.key] ?? ''),
              )
            }),
          ),
        )
    },
  })

  return {
    toast: {
      error: (...args: unknown[]) => planningSpies.toastError(...args),
      success: (...args: unknown[]) => planningSpies.toastSuccess(...args),
    },
    NvButton: Button,
    NvDataTable: DataTable,
    NvDatePicker: Shell,
    NvDialog: Shell,
    NvDialogContent: Shell,
    NvDialogDescription: Shell,
    NvDialogFooter: Shell,
    NvDialogHeader: Shell,
    NvDialogTitle: Shell,
    NvDialogTrigger: Shell,
    NvField: Shell,
    NvFieldGroup: Shell,
    NvFieldLabel: Shell,
    NvInput: Shell,
    NvMetricCard: Shell,
    NvPageHeader: Shell,
    NvSelect: Shell,
    NvSelectContent: Shell,
    NvSelectItem: Shell,
    NvSelectTrigger: Shell,
    NvSelectValue: Shell,
    Spinner: Shell,
    NvStatusBadge: defineComponent({ props: ['label'], template: '<span>{{ label }}</span>' }),
    NvTabs: Shell,
    NvTabsContent: Shell,
    NvTabsList: Shell,
    NvTabsTrigger: Shell,
  }
})

describe('PlanningWorkbench', () => {
  // 计划建议行的「对该单排产」按权限码显隐，组件因此要读 auth store（MAN-694 / #1262）。
  beforeEach(() => {
    setActivePinia(createPinia())
    planningSpies.runMrp = vi.fn(async () => undefined)
    planningSpies.toastError.mockReset()
    planningSpies.toastSuccess.mockReset()
  })

  it('drills a sales-order demand into the ERP order search without copying order facts', async () => {
    const wrapper = mount(PlanningWorkbench)

    await wrapper.get('[aria-label="查看销售订单 SO-DEMO-001"]').trigger('click')

    expect(routerPush).toHaveBeenCalledWith({
      path: '/erp/sales/orders',
      query: { keyword: 'SO-DEMO-001' },
    })
  })

  it('renders backend net requirement explanation instead of recalculating MRP in the browser', () => {
    const wrapper = mount(PlanningWorkbench)

    expect(wrapper.text()).toContain('净需求公式')
    expect(wrapper.text()).toContain('10 - 6 - 0 = 4')
    expect(wrapper.text()).toContain('需求来源')
    expect(wrapper.text()).toContain('组件毛需求')
    expect(wrapper.text()).toContain('scrap/yield 已计入组件毛需求')
    expect(wrapper.text()).toContain('SO-1001')
  })

  it('maps the demand source reference into the shared urgency badge', () => {
    const wrapper = mount(PlanningWorkbench)

    const badge = wrapper.find('[data-testid="order-urgency"]')
    expect(badge.exists()).toBe(true)
    expect(badge.attributes('data-ref')).toBe('SO-DEMO-001')
    expect(badge.attributes('data-mode')).toBe('level')
  })

  it('mounts the time-phased panel and run distribution chart scoped to a single run', () => {
    const wrapper = mount(PlanningWorkbench)

    // 时段视图建议序列锁定选中的运行（跨运行求和会重复计数）。
    expect(wrapper.get('[data-testid="time-phased-panel"]').attributes('data-run-id')).toBe(
      'run-001',
    )
    expect(wrapper.get('[data-testid="run-suggestion-chart"]').attributes('data-run-id')).toBe(
      'run-001',
    )
  })

  it('renders MRP exception suggestions as non-acceptance workbench rows', () => {
    const wrapper = mount(PlanningWorkbench)

    expect(wrapper.text()).toContain('延期调整')
    expect(wrapper.text()).toContain('异常待处理')
    expect(wrapper.findAll('button').filter((button) => button.text() === '接受')).toHaveLength(2)
    // 拒绝对所有 Open 建议可用（含异常类），3 条 Open 行各一个。
    expect(wrapper.findAll('button').filter((button) => button.text() === '拒绝')).toHaveLength(3)
  })

  it('已承接成 MES 工单的建议行给出「对该单排产」入口（MAN-694 / #1262）', () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.scheduling.plans.manage'] },
    } as never)
    const wrapper = mount(PlanningWorkbench)

    const entries = wrapper.findAll('[data-testid="planning-suggestion-schedule-single"]')
    expect(entries).toHaveLength(1)
    expect(entries[0]!.attributes('title')).toContain('WO-2026-0007')
    expect(entries[0]!.attributes('disabled')).toBeUndefined()
  })

  it('没有排产管理权限时入口禁用并说明原因，而不是直接消失', () => {
    const wrapper = mount(PlanningWorkbench)

    const entry = wrapper.get('[data-testid="planning-suggestion-schedule-single"]')
    expect(entry.attributes('disabled')).toBeDefined()
    expect(entry.attributes('title')).toContain('没有排产管理权限')
  })

  it('未承接工单的建议行不给排产入口，Open 行仍只是「接受 / 拒绝」', () => {
    const wrapper = mount(PlanningWorkbench)

    // 3 条 Open 行走接受/拒绝分支，不出现排产入口，也不出现「未承接工单」说明。
    expect(wrapper.findAll('[data-testid="planning-suggestion-schedule-single"]')).toHaveLength(1)
    expect(wrapper.text()).not.toContain('未承接工单，暂不能排产')
  })

  // MAN-700 / #1289：RunMrp 500 曾把英文 `Internal Server Error` 常驻在页面上，
  // 且 submitMrpRun 没有 try/catch，异常逃逸后弹框永远关不掉。
  it('RunMrp 500 只走 toast 人话，英文原文既不上屏也不留常驻错误条', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    planningSpies.runMrp = vi.fn(async () => {
      throw { title: 'Internal Server Error', status: 500 }
    })
    const wrapper = mount(PlanningWorkbench)

    // 未捕获就会变成 unhandled rejection（vitest 直接判错），本用例同时守住「弹框不卡死」。
    await wrapper.findAll('form')[0]!.trigger('submit')
    await Promise.resolve()

    expect(planningSpies.toastError).toHaveBeenCalledWith('运行 MRP 失败，请稍后重试。')
    expect(planningSpies.toastError).not.toHaveBeenCalledWith(
      expect.stringContaining('Internal Server'),
    )
    expect(wrapper.text()).not.toContain('Internal Server Error')
    consoleError.mockRestore()
  })

  it('RunMrp 被后端按领域理由拒绝时原样透传那句中文', async () => {
    planningSpies.runMrp = vi.fn(async () => {
      throw { status: 400, detail: '计划期内没有已发布的主计划行' }
    })
    const wrapper = mount(PlanningWorkbench)

    await wrapper.findAll('form')[0]!.trigger('submit')
    await Promise.resolve()

    expect(planningSpies.toastError).toHaveBeenCalledWith(
      '运行 MRP 失败：计划期内没有已发布的主计划行',
    )
  })
})
