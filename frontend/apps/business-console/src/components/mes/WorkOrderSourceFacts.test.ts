import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import WorkOrderSourceFacts from './WorkOrderSourceFacts.vue'

const state = vi.hoisted(() => ({
  items: [] as Array<Record<string, unknown>>,
  error: null as unknown,
  query: undefined as undefined | (() => Promise<unknown>),
  fetch: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  listBusinessConsolePlanningDemands: (...args: unknown[]) => state.fetch(...args),
}))
vi.mock('@pinia/colada', async (original) => ({
  ...(await original<typeof import('@pinia/colada')>()),
  useQuery: (options: () => { query: () => Promise<unknown> }) => {
    state.query = options().query
    return {
      data: ref({ success: true, data: { items: state.items } }),
      error: ref(state.error),
      isPending: ref(false),
      refetch: vi.fn(),
    }
  },
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
  it('非销售需求即使引用像销售单，也显示需求来源', () => {
    state.items = [{ sourceReference: 'SO-20260930-012', demandType: 'forecast', quantity: 120 }]
    const wrapper = render({ sourceDemandReference: 'SO-20260930-012' })
    expect(wrapper.text()).toContain('需求来源')
    expect(wrapper.text()).not.toContain('销售订单')
  })
  it('读取第二页的真实关联，不让第一页的100条相似引用遮蔽来源', async () => {
    state.fetch.mockReset()
    state.fetch
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: Array.from({ length: 100 }, (_, index) => ({
              sourceReference: `SO-20260930-012${index}`,
              demandType: 'sales-order',
            })),
          },
        },
      })
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                sourceReference: 'SO-20260930-012',
                demandType: 'sales-order',
                customerCode: 'CUST-LATE',
              },
            ],
          },
        },
      })
    render({ sourceDemandReference: 'SO-20260930-012' })
    const result = (await state.query!()) as { data: { items: Array<{ sourceReference: string }> } }
    expect(result.data.items).toEqual([
      { sourceReference: 'SO-20260930-012', demandType: 'sales-order', customerCode: 'CUST-LATE' },
    ])
    expect(state.fetch.mock.calls[1][0].query.skip).toBe(100)
  })
  it('无来源的急单如实显示未记录来源', () => {
    expect(render(null).text()).toContain('未记录来源计划或需求')
  })
})
