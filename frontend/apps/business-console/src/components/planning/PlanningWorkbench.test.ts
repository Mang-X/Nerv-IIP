import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import PlanningWorkbench from './PlanningWorkbench.vue'

vi.mock('@/composables/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({ byReference: { value: new Map() } }),
}))
vi.mock('@/components/urgency/OrderUrgencyBadge.vue', () => ({
  default: { template: '<span data-testid="order-urgency">未计算</span>' },
}))

const routerPush = vi.hoisted(() => vi.fn())

vi.mock('@/composables/useBusinessPlanning', async () => {
  const { reactive, shallowRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
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
      runMrp: vi.fn(),
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

vi.mock('@/utils/notify', () => ({
  notifyError: vi.fn(),
  notifySuccess: vi.fn(),
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
    NvPageHeader: Shell,
    NvSectionCard: Shell,
    NvSectionCards: Shell,
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

  it('renders MRP exception suggestions as non-acceptance workbench rows', () => {
    const wrapper = mount(PlanningWorkbench)

    expect(wrapper.text()).toContain('延期调整')
    expect(wrapper.text()).toContain('异常待处理')
    expect(wrapper.findAll('button').filter((button) => button.text() === '接受')).toHaveLength(2)
  })
})
