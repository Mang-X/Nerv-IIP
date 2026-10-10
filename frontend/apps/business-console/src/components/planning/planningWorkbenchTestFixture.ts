import { vi } from 'vitest'

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
vi.mock('@/components/planning/PlanningMaterialDeliveryPanel.vue', () => ({
  default: { template: '<section />' },
}))
vi.mock('@/components/planning/PlanningForecastManagement.vue', () => ({
  default: { template: '<div data-testid="forecast-management" />' },
}))
// 物料选择器的取数与就地新增由 DirectoryPicker 自己的测试覆盖。
vi.mock('@/components/business/DirectoryPicker.vue', () => ({
  default: { props: ['modelValue'], template: '<input readonly :value="modelValue" />' },
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
  mpsBucketsRef: null as { value: Array<Record<string, unknown>> } | null,
  mpsFormRef: null as { quantity: number } | null,
  mrpRunsRef: null as { value: Array<Record<string, unknown>> } | null,
  peggingRef: null as { value: Array<Record<string, unknown>> } | null,
  suggestionFiltersRef: null as { status: string } | null,
  suggestionsRef: null as { value: Array<Record<string, unknown>> } | null,
  resetDemands: () => {},
  runMrp: vi.fn(async () => undefined),
  updateMpsBucket: vi.fn(async () => undefined),
  acceptSuggestion: vi.fn(),
  cancelDemand: vi.fn(),
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
      acceptSuggestion: planningSpies.acceptSuggestion,
      acceptSuggestionError: shallowRef(null),
      acceptSuggestionPending: shallowRef(false),
      createMpsBucket: vi.fn(),
      createMpsBucketError: shallowRef(null),
      createMpsBucketPending: shallowRef(false),
      createDemandError: shallowRef(null),
      createDemandPending: shallowRef(false),
      createOrUpdateDemand: vi.fn(),
      cancelDemand: planningSpies.cancelDemand,
      cancelDemandPending: shallowRef(false),
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
      mrpRuns: (planningSpies.mrpRunsRef = shallowRef([
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
      ])),
      mrpRunsError: shallowRef(null),
      mrpRunsPending: shallowRef(false),
      mpsBuckets: (planningSpies.mpsBucketsRef = shallowRef([])),
      mpsBucketsError: shallowRef(null),
      mpsBucketsPending: shallowRef(false),
      mpsForm: (planningSpies.mpsFormRef = reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: '',
        uomCode: '',
        siteCode: '',
        bucketDate: '2026-06-01',
        quantity: 0,
      })),
      releaseMpsBucket: vi.fn(),
      releaseMpsBucketError: shallowRef(null),
      releaseMpsBucketPending: shallowRef(false),
      reviewMpsBucket: vi.fn(),
      reviewMpsBucketError: shallowRef(null),
      reviewMpsBucketPending: shallowRef(false),
      updateMpsBucket: planningSpies.updateMpsBucket,
      updateMpsBucketError: shallowRef(null),
      updateMpsBucketPending: shallowRef(false),
      pegging: (planningSpies.peggingRef = shallowRef([
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
      ])),
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
      suggestionFilters: (planningSpies.suggestionFiltersRef = reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'open',
      })),
      suggestionTypeFilter: reactive({ type: 'all' }),
      suggestions: (planningSpies.suggestionsRef = shallowRef([
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
          // 故意用**种子实际写入**的 kebab 口径（WorldHistorySeedService：`business-mes`），
          // 不用前端接受建议时写的 `BusinessMes`。夹具原来用后者，于是这条用例一直绿，
          // 而真机上「对该单排产」一个按钮都不渲染（第五轮走查实测）。
          // 两个生产者写法不一，读侧必须都认——夹具就该盯着更容易漏的那一边。
          downstreamService: 'business-mes',
          downstreamDocumentType: 'work-order',
          downstreamDocumentId: 'WO-2026-0007',
        },
      ])),
      suggestionsError: shallowRef(null),
      suggestionsPending: shallowRef(false),
      downstreamStatuses: shallowRef({ 'suggestion-004': 'released' }),
    }),
  }
})

vi.mock('@/composables/useBusinessMasterData', async () => {
  const { shallowRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    useBusinessMasterDataResources: (resourceType: string) => ({
      filters: {},
      resources: shallowRef(
        resourceType === 'worker'
          ? [{ userId: 'user-emp-zhangwei', code: 'EMP-0001', displayName: '张伟' }]
          : [],
      ),
    }),
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
      rowClass: { type: Function, default: undefined },
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
                { class: `cell-${column.key} ${props.rowClass?.(row) ?? ''}` },
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
  const Input = defineComponent({
    props: ['modelValue', 'modelModifiers'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" @input="$emit(\'update:modelValue\', modelModifiers?.number ? Number($event.target.value) : $event.target.value)" />',
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
    NvInput: Input,
    NvMetricCard: Shell,
    NvPageHeader: Shell,
    NvSelect: Select,
    NvSelectContent: Shell,
    NvSelectItem: SelectItem,
    NvSelectTrigger: Shell,
    NvSelectValue: Shell,
    NvSpinner: Shell,
    NvStatusBadge: defineComponent({ props: ['label'], template: '<span>{{ label }}</span>' }),
    NvTabs: Shell,
    NvTabsContent: Shell,
    NvTabsList: Shell,
    NvTabsTrigger: Shell,
    NvToolbar: Toolbar,
  }
})

export { planningSpies, routerPush }
