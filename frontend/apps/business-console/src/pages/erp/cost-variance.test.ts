import type { BusinessConsoleErpWorkOrderCostVarianceResponse } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, shallowRef } from 'vue'
import CostVariancePage from './finance/cost-variance.vue'

const state = vi.hoisted(() => ({ current: undefined as unknown }))
vi.mock('@/composables/useBusinessErp', () => ({
  useErpWorkOrderCostVariance: () => state.current,
}))

function fixture(): BusinessConsoleErpWorkOrderCostVarianceResponse {
  return {
    workOrderId: 'WO-202609-0186',
    currencyCode: 'CNY',
    laborCostBasis: 'actualOperation',
    laborVarianceStatus: 'available',
    unavailableReason: null,
    actualLaborHours: 12,
    actualLaborCost: 600,
    standardLaborHours: 10,
    standardLaborCost: 500,
    laborEfficiencyVarianceHours: 2,
    laborEfficiencyVarianceAmount: 100,
    laborEfficiencyVarianceDirection: 'unfavorable',
    laborRateVarianceStatus: 'notApplicable',
    laborRateVarianceReason: 'actual_payroll_rate_not_modeled',
    totalAccumulatedCost: 2600,
    capitalizedCost: 2400,
    capitalizationVarianceAmount: 200,
    actualMachineHours: null,
    machineCostStatus: 'notApplicable',
    machineCostUnavailableReason: 'machine_overhead_not_applicable',
    machineCurrencyCode: null,
    appliedFixedMachineOverhead: null,
    appliedVariableMachineOverhead: null,
    appliedMachineOverheadTotal: null,
    machineOverheadPageNumber: 1,
    machineOverheadPageSize: 10,
    totalMachineOverheadOperations: 0,
    machineOverheadOperations: [],
    pageNumber: 1,
    pageSize: 10,
    totalOperations: 12,
    operations: [
      {
        operationTaskId: 'OP-0186-10',
        workCenterId: 'WC-CNC-01',
        settlementRevision: 2,
        status: 'available',
        actualLaborHours: 12,
        actualLaborCost: 600,
        standardLaborHours: 10,
        standardLaborCost: 500,
        laborEfficiencyVarianceHours: 2,
        laborEfficiencyVarianceAmount: 100,
        laborEfficiencyVarianceDirection: 'unfavorable',
        currencyCode: 'CNY',
        workCenterCostRateId: 'RATE-CNC-09',
        rateRevision: 3,
        hourlyRate: 50,
        rateBasisAtUtc: '2026-09-12T08:00:00Z',
        coveredReports: [
          {
            reportNo: 'RPT-0186-11',
            goodQuantity: 100,
            scrapQuantity: 2,
            reworkQuantity: 1,
            uomCode: '件',
            theoreticalRatePerHour: 10,
            reportedAtUtc: '2026-09-12T09:00:00Z',
            isReversal: false,
          },
        ],
      },
      {
        operationTaskId: 'OP-0186-20',
        workCenterId: 'WC-CNC-02',
        settlementRevision: 4,
        status: 'available',
        currencyCode: 'CNY',
        workCenterCostRateId: 'RATE-CNC-08',
        rateRevision: 7,
        hourlyRate: 60,
        coveredReports: [
          {
            reportNo: 'REV-0186-11',
            isReversal: true,
            reversedReportNo: 'RPT-0186-11',
            goodQuantity: -100,
            scrapQuantity: -2,
            reworkQuantity: -1,
            uomCode: '件',
            theoreticalRatePerHour: 10,
          },
        ],
      },
    ],
  }
}
function createState() {
  return {
    ready: shallowRef(true),
    workOrder: reactive({ id: 'WO-202609-0186', page: 1, pageSize: 10 }),
    workOrderData: shallowRef<BusinessConsoleErpWorkOrderCostVarianceResponse | undefined>(
      fixture(),
    ),
    workOrderPending: shallowRef(false),
    workOrderError: shallowRef<Error | undefined>(),
    refreshWorkOrder: vi.fn(),
  }
}
let data: ReturnType<typeof createState>
const render = () =>
  mount(CostVariancePage, {
    global: {
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        RouterLink: { template: '<a><slot /></a>' },
      },
    },
  })

// DomainInvariant / PublicContract: #2279 验收与 #2278 工单差异公开契约。
describe('工单成本差异', () => {
  beforeEach(() => {
    data = createState()
    state.current = data
  })
  it.each([
    ['unfavorable', 100, '不利'],
    ['favorable', -100, '有利'],
    ['neutral', 0, '无差异'],
  ] as const)('区分效率方向 %s 与独立资本化差额', async (direction, amount, label) => {
    data.workOrderData.value = {
      ...fixture(),
      laborEfficiencyVarianceDirection: direction,
      laborEfficiencyVarianceAmount: amount,
    }
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).toContain(`效率方向：${label}`)
    expect(wrapper.text()).toContain(`CNY ${amount.toFixed(2)}`)
    expect(wrapper.text()).toContain('CNY 200.00')
    expect(wrapper.text()).toContain('实际人工工时')
    expect(wrapper.text()).toContain('12 小时')
    expect(wrapper.text()).toContain('10 小时')
    expect(wrapper.text()).toContain('CNY 600.00')
    expect(wrapper.text()).toContain('CNY 500.00')
  })
  it('无标准基准仍显示实际，机器未启用不冒充零金额', async () => {
    data.workOrderData.value = {
      ...fixture(),
      laborVarianceStatus: 'unavailable',
      unavailableReason: 'invalid_theoretical_rate',
      standardLaborHours: null,
      standardLaborCost: null,
      laborEfficiencyVarianceHours: null,
      laborEfficiencyVarianceAmount: null,
      laborEfficiencyVarianceDirection: null,
      operations: [],
    }
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).toContain('理论产出速率未维护或无效，暂无标准基准')
    expect(wrapper.text()).toContain('CNY 600.00')
    expect(wrapper.text()).toContain('未启用机器成本')
    expect(wrapper.text()).toContain('机器工时：—')
    expect(wrapper.text()).not.toContain('CNY 0.00')
    expect(wrapper.text()).toContain('未建立实际工资费率')
  })
  it('追溯不同费率版本、覆盖报工与冲销关系，分页不改工单汇总', async () => {
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).toContain('结算版本 2 · 费率版本 3')
    expect(wrapper.text()).toContain('结算版本 4 · 费率版本 7')
    expect(wrapper.text()).toContain('RATE-CNC-09 / CNY 50.00')
    expect(wrapper.text()).toContain('RPT-0186-11 · 报工')
    expect(wrapper.text()).toContain('REV-0186-11 · 冲销')
    expect(wrapper.text()).toContain('原报工：RPT-0186-11')
    const table = wrapper.findComponent({ name: 'NvDataTable' })
    expect(table.props('totalItems')).toBe(12)
    table.vm.$emit('update:page', 2)
    expect(data.workOrder.page).toBe(2)
    expect(wrapper.text()).toContain('CNY 2,600.00')
  })
  it('冲销后刷新清除旧结算与金额，等待重新结算', async () => {
    const wrapper = render()
    await flushPromises()
    data.workOrderData.value = {
      ...fixture(),
      laborVarianceStatus: 'unavailable',
      unavailableReason: 'operation_not_settled',
      actualLaborHours: null,
      actualLaborCost: null,
      standardLaborHours: null,
      standardLaborCost: null,
      laborEfficiencyVarianceAmount: null,
      laborEfficiencyVarianceHours: null,
      laborEfficiencyVarianceDirection: null,
      totalOperations: 0,
      operations: [],
    }
    await flushPromises()
    expect(wrapper.text()).toContain('暂无有效人工结算；冲销后请重新结算')
    expect(wrapper.text()).not.toContain('RATE-CNC-09')
    expect(wrapper.text()).not.toContain('CNY 600.00')
  })
  it('查询重置分页，加载和失败期间不显示旧成本', async () => {
    const wrapper = render()
    await flushPromises()
    data.workOrder.page = 2
    await wrapper.get('input[aria-label="工单编号"]').setValue(' WO-202609-0190 ')
    await wrapper.get('form').trigger('submit')
    expect(data.workOrder.id).toBe('WO-202609-0190')
    expect(data.workOrder.page).toBe(1)
    expect(data.refreshWorkOrder).toHaveBeenCalledOnce()
    data.workOrderPending.value = true
    await flushPromises()
    expect(wrapper.text()).not.toContain('CNY 600.00')
    data.workOrderPending.value = false
    data.workOrderError.value = new Error('failed')
    await flushPromises()
    expect(wrapper.text()).not.toContain('CNY 600.00')
  })
})
