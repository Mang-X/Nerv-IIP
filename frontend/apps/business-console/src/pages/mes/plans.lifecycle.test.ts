import { mount } from '@vue/test-utils'
import { computed, reactive, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import PlansPage from './plans.vue'

const state = vi.hoisted(() => ({
  productionPlans: [] as Array<Record<string, unknown>>,
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  useMesProductionPlans: () => ({
    filters: reactive({ organizationId: 'org-1', environmentId: 'env-1', skip: 0, take: 20 }),
    productionPlans: computed(() => state.productionPlans),
    productionPlansError: ref(undefined),
    productionPlansPending: ref(false),
    productionPlansTotal: computed(() => state.productionPlans.length),
    refreshProductionPlans: vi.fn(),
  }),
}))

vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({ resolveSkuLabel: () => '活塞杆组件' }),
}))

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: ref(1), pageSize: ref(20) }),
}))

vi.mock('@/utils/notify', () => ({
  inlineErrorMessage: () => '',
  notifyOperationFailure: vi.fn(),
  notifySuccess: vi.fn(),
}))

function plan(overrides: Record<string, unknown> = {}) {
  return {
    productionPlanId: 'PLAN-202609-001',
    sourceSystem: 'APS',
    sourceDocumentType: 'SchedulePlan',
    sourceDocumentId: 'SP-202609-001',
    sourceDemandReference: null,
    skuId: 'SKU-PISTON-001',
    plannedQuantity: 30,
    uomCode: 'pcs',
    status: 'started',
    readinessStatus: 'Ready',
    blockingReasons: [],
    plannedStartUtc: '2026-09-27T08:00:00Z',
    plannedEndUtc: '2026-09-27T16:00:00Z',
    ...overrides,
  }
}

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvPageHeader: { template: '<header><slot name="actions" /></header>' },
  NvToolbar: { template: '<div><slot name="filters" /><slot name="actions" /></div>' },
  NvDataTable: {
    props: ['rows'],
    template: `
      <table><tbody>
        <tr v-for="row in rows" :key="row.productionPlanId">
          <td data-column="status"><slot name="cell-status" :row="row" /></td>
          <td data-column="actions"><slot name="cell-actions" :row="row" /></td>
        </tr>
      </tbody></table>
    `,
  },
  NvButton: {
    inheritAttrs: false,
    props: ['disabled', 'type'],
    template:
      '<button v-bind="$attrs" :type="type || \'button\'" :disabled="disabled"><slot /></button>',
  },
  NvStatusBadge: { props: ['label'], template: '<span>{{ label }}</span>' },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  NvSelectValue: { template: '<span />' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<div><slot /></div>' },
}

describe('MES 生产计划生命周期入口', () => {
  it.each([
    ['created', '已转工单'],
    ['released', '已转工单'],
    ['started', '已转工单'],
    ['completed', '已完工'],
  ])('%s 状态的已转工单不提供再次转工单入口', (status, expectedLabel) => {
    state.productionPlans = [plan({ status })]
    const wrapper = mount(PlansPage, { global: { stubs } })
    const row = wrapper.find('tbody tr')

    expect(row.get('[data-column="status"]').text()).toBe(expectedLabel)
    expect(wrapper.text()).not.toContain('可转工单')
    expect(row.find('button').exists()).toBe(false)
  })
})
