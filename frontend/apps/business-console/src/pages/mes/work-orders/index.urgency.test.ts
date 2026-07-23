import { mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import WorkOrdersListPage from './index.vue'

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

const workOrders = vi.hoisted(() => ({ items: [] as Array<Record<string, unknown>> }))
vi.mock('@/composables/useBusinessMes', () => ({
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
    refreshWorkOrders: vi.fn(),
    workOrders: ref(workOrders.items),
    workOrdersError: ref(undefined),
    workOrdersPending: ref(false),
    workOrdersTotal: ref(workOrders.items.length),
  }),
}))

function mountList() {
  return mount(WorkOrdersListPage, {
    global: {
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        NvPageHeader: { template: '<header><slot name="actions" /></header>' },
        NvToolbar: { template: '<div><slot name="filters" /><slot name="actions" /></div>' },
        NvDataTable: {
          props: ['rows', 'columns'],
          template:
            '<div><template v-for="(row, i) in rows" :key="i"><slot name="cell-urgency" :row="row" /></template></div>',
        },
        NvStatusBadge: { props: ['value', 'label'], template: '<span>{{ label ?? value }}</span>' },
        NvButton: { template: '<button><slot /></button>' },
        NvSelect: { template: '<div><slot /></div>' },
        NvSelectTrigger: { template: '<button><slot /></button>' },
        NvSelectContent: { template: '<div><slot /></div>' },
        NvSelectItem: { props: ['value'], template: '<div><slot /></div>' },
        SelectValue: { template: '<span />' },
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
})
