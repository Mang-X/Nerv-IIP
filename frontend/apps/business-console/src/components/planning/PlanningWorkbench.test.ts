import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

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
  // 需求池刷新后"某类需求整类消失"要能在用例里复现 → 把 demands 的 ref 交出来供测试改写。
  demandsRef: null as { value: Array<Record<string, unknown>> } | null,
  resetDemands: () => {},
  runMrp: vi.fn(async () => undefined),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
  toastWarning: vi.fn(),
  // #1306 异步跟踪状态：mock 工厂里包成 reactive，测试直接改字段驱动 watch。
  activeMrpRun: {
    runId: '',
    status: '' as string,
    failureReason: '',
    suggestionCount: null as number | null,
  },
}))

vi.mock('@/composables/useBusinessPlanning', async () => {
  const { reactive, shallowRef } = await vi.importActual<typeof import('vue')>('vue')
  planningSpies.activeMrpRun = reactive(planningSpies.activeMrpRun)
  const DEFAULT_DEMANDS = [
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
    // 第二条走预测来源：需求池筛选（关键字 / 类型）要能把两条真的分开。
    {
      demandSourceId: 'demand-002',
      sourceReference: 'FC-2026-08-A',
      sourceLineReference: '20',
      customerCode: 'CUST-002',
      sourceVersion: 1,
      sourceStatus: 'active',
      demandType: 'forecast',
      skuCode: 'SKU-FG-2000',
      uomCode: 'pcs',
      siteCode: 'SITE-01',
      quantity: 8,
      dueDate: '2026-08-20',
    },
  ]
  const demandsRef = shallowRef([...DEFAULT_DEMANDS])
  planningSpies.demandsRef = demandsRef as unknown as {
    value: Array<Record<string, unknown>>
  }
  planningSpies.resetDemands = () => {
    demandsRef.value = [...DEFAULT_DEMANDS]
  }
  return {
    SUGGESTION_REJECT_REASON_MAX_LENGTH: 128,
    useBusinessPlanning: () => ({
      activeMrpRun: planningSpies.activeMrpRun,
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
      demands: demandsRef,
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
  const { defineComponent, h, inject, provide } = await vi.importActual<typeof import('vue')>('vue')
  // 下拉要能真的选中一项（需求池类型筛选用例靠它）：Root 负责回传值，Item 渲染成可点按钮。
  const SELECT_SETTER = Symbol.for('nv-select-setter')
  const Select = defineComponent({
    props: { modelValue: { type: String, default: '' } },
    emits: ['update:modelValue'],
    setup(_props, { emit, slots }) {
      provide(SELECT_SETTER, (value: string) => emit('update:modelValue', value))
      return () => h('div', slots.default?.())
    },
  })
  const SelectItem = defineComponent({
    props: { value: { type: String, default: '' } },
    setup(props, { slots }) {
      const setValue = inject<(value: string) => void>(SELECT_SETTER, () => {})
      return () =>
        h(
          'button',
          {
            type: 'button',
            'data-select-value': props.value,
            onClick: () => setValue(props.value),
          },
          slots.default?.(),
        )
    },
  })
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

  // 工具条要真能收关键字并把 filters / actions 插槽渲染出来，否则筛选用例测不到东西。
  const Toolbar = defineComponent({
    props: {
      search: { type: String, default: '' },
      searchLabel: { type: String, default: '搜索' },
    },
    emits: ['update:search'],
    template:
      '<div><input :aria-label="searchLabel" :value="search" @input="$emit(\'update:search\', $event.target.value)" /><slot name="filters" /><slot name="actions" /></div>',
  })

  return {
    toast: {
      error: (...args: unknown[]) => planningSpies.toastError(...args),
      success: (...args: unknown[]) => planningSpies.toastSuccess(...args),
      warning: (...args: unknown[]) => planningSpies.toastWarning(...args),
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
    NvSelect: Select,
    NvSelectContent: Shell,
    NvSelectItem: SelectItem,
    NvSelectTrigger: Shell,
    NvSelectValue: Shell,
    Spinner: Shell,
    NvStatusBadge: defineComponent({ props: ['label'], template: '<span>{{ label }}</span>' }),
    NvTabs: Shell,
    NvTabsContent: Shell,
    NvTabsList: Shell,
    NvTabsTrigger: Shell,
    NvToolbar: Toolbar,
  }
})

describe('PlanningWorkbench', () => {
  // 计划建议行的「对该单排产」按权限码显隐，组件因此要读 auth store（MAN-694 / #1262）。
  beforeEach(() => {
    setActivePinia(createPinia())
    planningSpies.runMrp = vi.fn(async () => undefined)
    planningSpies.toastError.mockReset()
    planningSpies.toastSuccess.mockReset()
    planningSpies.toastWarning.mockReset()
    planningSpies.activeMrpRun.runId = ''
    planningSpies.activeMrpRun.status = ''
    planningSpies.activeMrpRun.failureReason = ''
    planningSpies.activeMrpRun.suggestionCount = null
    planningSpies.resetDemands()
  })

  it('drills a sales-order demand into the ERP order search without copying order facts', async () => {
    const wrapper = mount(PlanningWorkbench)

    await wrapper.get('[aria-label="查看销售订单 SO-DEMO-001"]').trigger('click')

    expect(routerPush).toHaveBeenCalledWith({
      path: '/erp/sales/orders',
      query: { keyword: 'SO-DEMO-001' },
    })
  })

  // GH#1292 第 5 项：需求池此前没有任何查找手段，几百条需求只能肉眼扫。
  // 读面整表返回、不带关键字参数，所以筛选在前端做——这组用例锁住它真的筛得动。
  describe('需求池搜索与筛选', () => {
    it('关键字命中来源单号 / 物料 / 客户，未命中的行不再渲染', async () => {
      const wrapper = mount(PlanningWorkbench)
      expect(wrapper.text()).toContain('SO-DEMO-001')
      expect(wrapper.text()).toContain('FC-2026-08-A')

      await wrapper.get('[aria-label="需求池关键字"]').setValue('fc-2026')

      expect(wrapper.text()).toContain('FC-2026-08-A')
      expect(wrapper.text()).not.toContain('SO-DEMO-001')
      // 页签同步显「筛出数/总数」，别让人以为需求池整个缩水了。
      expect(wrapper.text()).toContain('需求池 (1/2)')
    })

    it('关键字也能按物料编码命中', async () => {
      const wrapper = mount(PlanningWorkbench)

      await wrapper.get('[aria-label="需求池关键字"]').setValue('SKU-FG-1000')

      expect(wrapper.text()).toContain('SO-DEMO-001')
      expect(wrapper.text()).not.toContain('FC-2026-08-A')
    })

    // 刷新后已选类型整类消失时，下拉不能留一个不在选项里的值（会显示为空白，
    // 表格又按它筛成空，用户看不出发生了什么）。
    it('刷新后已选类型不复存在时回落全部类型并说明原因', async () => {
      const wrapper = mount(PlanningWorkbench)
      // 用「销售订单」这一类：它只出现在需求池筛选下拉里，不会和新建需求表单的类型下拉撞选择器。
      await wrapper.get('[data-select-value="sales-order"]').trigger('click')
      expect(wrapper.text()).toContain('SO-DEMO-001')
      expect(wrapper.text()).not.toContain('FC-2026-08-A')

      // 销售订单类需求被消化完，刷新后整类消失。
      planningSpies.demandsRef!.value = planningSpies.demandsRef!.value.filter(
        (demand) => demand.demandType !== 'sales-order',
      )
      await nextTick()
      await nextTick()

      expect(wrapper.text()).toContain('已不在当前需求池中，已切回全部类型')
      // 回落后剩下的那条照常显示，不是筛成空白。
      expect(wrapper.text()).toContain('FC-2026-08-A')
    })

    it('全都筛没了时给的是「换个条件」而不是「当前范围没有需求」', async () => {
      const wrapper = mount(PlanningWorkbench)

      await wrapper.get('[aria-label="需求池关键字"]').setValue('查无此单')

      expect(wrapper.text()).not.toContain('SO-DEMO-001')
      expect(wrapper.text()).toContain('需求池 (0/2)')
    })
  })

  it('renders backend net requirement explanation instead of recalculating MRP in the browser', () => {
    const wrapper = mount(PlanningWorkbench)

    expect(wrapper.text()).toContain('净需求公式')
    expect(wrapper.text()).toContain('10 - 6 - 0 = 4')
    expect(wrapper.text()).toContain('需求来源')
    expect(wrapper.text()).toContain('组件毛需求')
    // #1418 顺带项：scrap/yield 英文码说人话——公式只保留算式，比率以中文百分比呈现。
    expect(wrapper.text()).toContain('废品率 10%')
    expect(wrapper.text()).toContain('良率 80%')
    expect(wrapper.text()).toContain('废品率 / 良率已计入组件毛需求')
    expect(wrapper.text()).not.toContain('scrap/yield')
    expect(wrapper.text()).toContain('27.5 - 0 - 0 = 27.5')
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

  // #1306 异步任务模式：提交=受理，弹框全程可关闭，后台跑完由 watch 统一 toast。
  it('RunMrp 提交即受理并提示后台计算中', async () => {
    const wrapper = mount(PlanningWorkbench)

    await wrapper.findAll('form')[0]!.trigger('submit')
    await Promise.resolve()

    expect(planningSpies.runMrp).toHaveBeenCalled()
    expect(planningSpies.toastSuccess).toHaveBeenCalledWith(
      'MRP 已受理，正在后台计算，完成后自动刷新。',
    )
    expect(planningSpies.toastError).not.toHaveBeenCalled()
  })

  it('轮询到完成态时 toast 建议数并收起弹框', async () => {
    const wrapper = mount(PlanningWorkbench)

    planningSpies.activeMrpRun.runId = 'run-async-1'
    planningSpies.activeMrpRun.status = 'completed'
    planningSpies.activeMrpRun.suggestionCount = 5
    await wrapper.vm.$nextTick()

    expect(planningSpies.toastSuccess).toHaveBeenCalledWith('MRP 计算完成，共生成 5 条计划建议。')
  })

  it('轮询到失败态时把 failureReason 走分层透传上屏', async () => {
    const wrapper = mount(PlanningWorkbench)

    planningSpies.activeMrpRun.runId = 'run-async-1'
    planningSpies.activeMrpRun.failureReason = 'MRP 计算失败：上游库存快照不可用。'
    planningSpies.activeMrpRun.status = 'failed'
    await wrapper.vm.$nextTick()

    // 后端前缀被去重：不出现「MRP 计算失败：MRP 计算失败：…」的叠层。
    expect(planningSpies.toastError).toHaveBeenCalledWith('MRP 计算失败：上游库存快照不可用。')
  })

  it('轮询超时只提醒去运行列表回看，不按失败处理', async () => {
    const wrapper = mount(PlanningWorkbench)

    planningSpies.activeMrpRun.runId = 'run-async-1'
    planningSpies.activeMrpRun.status = 'polling-timeout'
    await wrapper.vm.$nextTick()

    expect(planningSpies.toastWarning).toHaveBeenCalledWith(
      'MRP 仍在后台计算，可稍后在「MRP 运行」列表查看结果。',
    )
    expect(planningSpies.toastError).not.toHaveBeenCalled()
  })
})
