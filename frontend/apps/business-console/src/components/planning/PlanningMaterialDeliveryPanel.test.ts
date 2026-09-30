import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, shallowRef } from 'vue'
import type { BusinessConsoleMaterialDeliveriesResponse } from '@nerv-iip/api-client'
import PlanningMaterialDeliveryPanel from './PlanningMaterialDeliveryPanel.vue'

const state = vi.hoisted(() => ({ data: {} as BusinessConsoleMaterialDeliveriesResponse }))
vi.mock('@/composables/useBusinessPlanningMaterialDeliveries', () => ({
  useBusinessPlanningMaterialDeliveries: () => ({
    selection: reactive({ runId: '', planId: '' }),
    planPage: shallowRef(1),
    plans: shallowRef([
      { planId: 'plan-1', generatedAtUtc: '2026-10-01T08:00:00Z', status: 'Draft' },
    ]),
    plansTotal: shallowRef(1),
    plansPending: shallowRef(false),
    deliveries: shallowRef(state.data),
    deliveriesPending: shallowRef(false),
    deliveriesError: shallowRef(null),
    plansError: shallowRef(null),
    refresh: vi.fn(),
  }),
}))

// DomainInvariant: #4097。错把拆批建议数量当缺口、重算状态或隐去缺失事实都会失败。
describe('物料交付页内事实', () => {
  beforeEach(() => {
    state.data = {
      runId: 'run-1',
      evaluatedAtUtc: '2026-10-01T08:00:00Z',
      items: [
        {
          netRequirementReference: 'net-1',
          skuCode: 'RM-100',
          uomCode: 'kg',
          netRequirementQuantity: 30,
          latestProcurementDate: '2026-09-25',
          latestProcurementUtc: '2026-09-25T00:00:00Z',
          expectedArrivalDate: null,
          expectedArrivalUtc: null,
          expectedStartUtc: null,
          latestStartUtc: '2026-10-08T08:00:00Z',
          coveredQuantity: 12,
          uncoveredQuantity: 18,
          status: 'yellow',
          reasons: ['supply-insufficient', 'expected-start-missing'],
          demandSources: [
            {
              sourceReference: 'SO-1001',
              sourceLineReference: '10',
              sourceType: 'sales-order',
              grossDemandQuantity: 18,
            },
            { sourceReference: 'SO-1002', sourceType: 'sales-order', grossDemandQuantity: 12 },
          ],
          suggestionSources: [
            { suggestionId: 's-1', quantity: 12 },
            { suggestionId: 's-2', quantity: 12 },
            { suggestionId: 's-3', quantity: 6 },
          ],
          schedulingSources: [
            {
              workOrderId: 'WO-1001',
              status: 'unscheduled',
              operations: [
                {
                  operationId: 'OP-10',
                  earliestStartUtc: '2026-10-02T08:00:00Z',
                  assignmentStartUtc: null,
                  assignmentStatus: 'unscheduled',
                  remainingMinutes: 90,
                },
              ],
            },
          ],
        },
      ],
      unknownRequirementSuggestions: [
        {
          skuCode: 'RM-OLD',
          reason: 'net-requirement-identity-unknown',
          releaseDate: '2026-09-26',
          suggestionSource: { suggestionId: 'old-1', quantity: 8 },
          rawNetRequirementSource: { netRequirementQuantity: 24 },
        },
      ],
    }
  })
  const render = () =>
    mount(PlanningMaterialDeliveryPanel, {
      props: {
        runs: [
          {
            runId: 'run-1',
            status: 'Completed',
            horizonStart: '2026-10-01',
            horizonEnd: '2026-10-31',
          },
        ],
        skuLabel: (code) => code ?? '—',
      },
    })

  it('同一净需求只展示一次缺口，并在原行展开多个销售与拆批来源', async () => {
    const wrapper = render()
    expect(wrapper.findAll('[data-net-requirement-quantity]').map((x) => x.text())).toEqual([
      '30 kg',
    ])
    expect(wrapper.text()).toContain('SO-1001')
    expect(wrapper.text()).toContain('SO-1002')
    const details = wrapper.find('details')
    expect(details.exists()).toBe(true)
    await details.find('summary').trigger('click')
    expect(details.text()).toContain('WO-1001')
    expect(details.text()).toContain('输入最早开工')
    expect(details.text()).toContain('实际排程开始')
    expect(wrapper.text()).toContain('净需求身份未知')
    expect(wrapper.findAll('[data-net-requirement-quantity]')).toHaveLength(1)
  })

  it('保留后端三色和空日期，不把输入约束当作实际开工', async () => {
    const wrapper = render()
    expect(wrapper.text()).toContain('紧张')
    expect(wrapper.text()).toContain('供应不足')
    expect(wrapper.text()).toContain('缺少实际排程开始')
    expect(wrapper.find('[data-expected-start]').text()).toBe('—')
    expect(wrapper.find('[data-expected-arrival]').text()).toBe('—')
    state.data.items![0]!.status = 'red'
    const red = render()
    await flushPromises()
    expect(red.text()).toContain('已晚')
  })
})
