import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import PlanningWorkbench from './PlanningWorkbench.vue'

vi.mock('@/composables/useBusinessPlanning', async () => {
  const { reactive, shallowRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    useBusinessPlanning: () => ({
      acceptSuggestion: vi.fn(),
      acceptSuggestionError: shallowRef(null),
      acceptSuggestionPending: shallowRef(false),
      createDemandError: shallowRef(null),
      createDemandPending: shallowRef(false),
      createOrUpdateDemand: vi.fn(),
      demandForm: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        demandType: 'sales-order',
        sourceReference: '',
        skuCode: '',
        uomCode: '',
        siteCode: '',
        quantity: 0,
        dueDate: '2026-06-01',
        idempotencyKey: '',
      }),
      demands: shallowRef([]),
      demandsError: shallowRef(null),
      demandsPending: shallowRef(false),
      mrpRuns: shallowRef([{
        runId: 'run-001',
        horizonStart: '2026-06-01',
        horizonEnd: '2026-06-30',
        status: 'Completed',
        demandCount: 1,
        availabilityCount: 1,
        suggestionCount: 1,
        hasInputDegradation: false,
        inputDegradationSources: [],
      }]),
      mrpRunsError: shallowRef(null),
      mrpRunsPending: shallowRef(false),
      pegging: shallowRef([{
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
      }]),
      peggingPending: shallowRef(false),
      refreshPlanning: vi.fn(),
      runMrp: vi.fn(),
      runMrpError: shallowRef(null),
      runMrpPending: shallowRef(false),
      runRequest: reactive({ organizationId: 'org-001', environmentId: 'env-dev', horizonStart: '2026-06-01', horizonEnd: '2026-06-30' }),
      runSelection: reactive({ runId: 'run-001' }),
      suggestionFilters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', status: 'open' }),
      suggestionTypeFilter: reactive({ type: 'all' }),
      suggestions: shallowRef([{
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
          primarySourceType: 'sales',
          formula: '10 - 8 + 0 + 2 - 0 = 4',
          degradationSources: [],
        },
      }]),
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
  useRouter: () => ({ push: vi.fn() }),
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
      return () => h('div', props.rows.flatMap((row: any) =>
        props.columns.map((column: any) => {
          const slot = slots[`cell-${column.key}`]
          return h('div', { class: `cell-${column.key}` }, slot ? slot({ row }) : String(row[column.key] ?? ''))
        }),
      ))
    },
  })

  return {
    ButtonPro: Button,
    DataTablePro: DataTable,
    DatePickerPro: Shell,
    DialogPro: Shell,
    DialogProContent: Shell,
    DialogProDescription: Shell,
    DialogProFooter: Shell,
    DialogProHeader: Shell,
    DialogProTitle: Shell,
    DialogProTrigger: Shell,
    FieldPro: Shell,
    FieldProGroup: Shell,
    FieldProLabel: Shell,
    InputPro: Shell,
    PageHeader: Shell,
    SectionCard: Shell,
    SectionCards: Shell,
    SelectPro: Shell,
    SelectProContent: Shell,
    SelectProItem: Shell,
    SelectProTrigger: Shell,
    SelectProValue: Shell,
    Spinner: Shell,
    StatusBadgePro: defineComponent({ props: ['label'], template: '<span>{{ label }}</span>' }),
    TabsPro: Shell,
    TabsProContent: Shell,
    TabsProList: Shell,
    TabsProTrigger: Shell,
  }
})

describe('PlanningWorkbench', () => {
  it('renders backend net requirement explanation instead of recalculating MRP in the browser', () => {
    const wrapper = mount(PlanningWorkbench)

    expect(wrapper.text()).toContain('净需求公式')
    expect(wrapper.text()).toContain('10 - 8 + 0 + 2 - 0 = 4')
    expect(wrapper.text()).toContain('销售来源')
    expect(wrapper.text()).toContain('SO-1001')
  })
})
