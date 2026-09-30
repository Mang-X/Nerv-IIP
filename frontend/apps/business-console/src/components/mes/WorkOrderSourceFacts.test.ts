import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import WorkOrderSourceFacts from './WorkOrderSourceFacts.vue'

const state = vi.hoisted(() => ({
  items: [] as Array<Record<string, unknown>>,
  error: null as unknown,
}))
vi.mock('@pinia/colada', async (original) => ({
  ...(await original<typeof import('@pinia/colada')>()),
  useQuery: () => ({
    data: ref({ success: true, data: { items: state.items } }),
    error: ref(state.error),
    isPending: ref(false),
    refetch: vi.fn(),
  }),
}))
function render(source: Record<string, unknown> | null) {
  const pinia = createPinia()
  useAuthStore(pinia).$patch({
    principal: {
      principalId: 'planner',
      principalType: 'user',
      organizationId: 'org',
      environmentId: 'prod',
      loginName: 'planner',
      permissionCodes: ['business.planning.demands.read'],
    },
  })
  return mount(WorkOrderSourceFacts, {
    props: { source, organizationId: 'org', environmentId: 'prod' },
    global: { plugins: [pinia] },
  })
}
describe('工单来源事实', () => {
  it('页内显示返回的建议和需求，只把精确关联的销售需求展示为销售来源', () => {
    state.items = [
      {
        sourceReference: 'SO-20260930-012',
        demandType: 'sales-order',
        sourceLineReference: '20',
        customerCode: 'CUST-002',
        skuCode: 'FG-1000',
        quantity: 120,
        uomCode: 'PCS',
      },
      {
        sourceReference: 'SO-20260930-0120',
        demandType: 'sales-order',
        customerCode: 'OTHER-CUSTOMER',
      },
    ]
    const wrapper = render({
      sourceSystem: 'DemandPlanning',
      sourceDocumentType: 'PlanningSuggestion',
      sourceDocumentId: 'PS-20260930-009',
      sourceDemandReference: 'SO-20260930-012',
    })
    expect(wrapper.text()).toContain('PS-20260930-009')
    expect(wrapper.text()).toContain('SO-20260930-012')
    expect(wrapper.text()).toContain('销售订单')
    expect(wrapper.text()).toContain('CUST-002')
    expect(wrapper.text()).toContain('120 PCS')
    expect(wrapper.text()).not.toContain('OTHER-CUSTOMER')
    expect(wrapper.find('a').exists()).toBe(false)
  })
  it('编号像销售单但没有关联事实时，不推断销售来源', () => {
    state.items = []
    const wrapper = render({
      sourceDocumentId: 'PLAN-035',
      sourceDemandReference: 'SO-20260930-012',
    })
    expect(wrapper.text()).toContain('未取得对应需求来源')
    expect(wrapper.text()).not.toContain('销售订单')
  })
  it('无来源的急单如实显示未记录来源', () => {
    expect(render(null).text()).toContain('未记录来源计划或需求')
  })
})
