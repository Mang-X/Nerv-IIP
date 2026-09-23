import type {
  BusinessConsoleErpWorkOrderCostVarianceResponse,
  BusinessConsoleErpMachineOverheadReconciliationListResponse,
} from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, shallowRef } from 'vue'
import MachineOverheadPage from './finance/machine-overhead.vue'

const state = vi.hoisted(() => ({ current: undefined as unknown }))
vi.mock('@/composables/useBusinessErp', () => ({ useErpMachineOverhead: () => state.current }))
vi.mock('@/composables/useBusinessMes', async () => {
  const { reactive, shallowRef } = await import('vue')
  return {
    useMesWorkOrders: () => ({
      filters: reactive({}),
      workOrders: shallowRef([]),
      workOrdersPending: shallowRef(false),
      workOrdersTotal: shallowRef(0),
    }),
  }
})

function orderFixture(): BusinessConsoleErpWorkOrderCostVarianceResponse {
  return {
    workOrderId: 'WO-202609-0186',
    actualMachineHours: 120,
    machineCostStatus: 'available',
    machineCostUnavailableReason: null,
    machineCurrencyCode: 'CNY',
    appliedFixedMachineOverhead: 12000,
    appliedVariableMachineOverhead: 3600,
    appliedMachineOverheadTotal: 15600,
    machineOverheadPageNumber: 1,
    machineOverheadPageSize: 10,
    totalMachineOverheadOperations: 21,
    machineOverheadOperations: [
      {
        operationTaskId: 'OP-0186-10',
        workCenterId: 'WC-CNC-01',
        settlementId: 'SET-0186-2',
        settlementRevision: 2,
        status: 'available',
        unavailableReason: null,
        actualMachineHours: 120,
        appliedFixedMachineOverhead: 12000,
        appliedVariableMachineOverhead: 3600,
        appliedMachineOverheadTotal: 15600,
        accountingPeriodCode: '2026-09',
        currencyCode: 'CNY',
        workCenterMachineOverheadRateId: 'RATE-CNC-09',
        rateRevision: 3,
        completedAtUtc: '2026-09-12T10:00:00Z',
        sourceEventId: 'EVT-0186-2',
        deviceAssetId: 'CNC-01',
      },
    ],
  }
}
function monthFixture(
  applied = 15600,
): BusinessConsoleErpMachineOverheadReconciliationListResponse {
  return {
    accountingPeriodCode: '2026-09',
    pageNumber: 1,
    pageSize: 10,
    totalCount: 12,
    accountingPeriodStatus: 'open',
    reconciliationStatus: 'available',
    reconciliationUnavailableReason: null,
    items: [
      {
        id: 'REC-CNC-09',
        workCenterId: 'WC-CNC-01',
        accountingPeriodCode: '2026-09',
        revision: 2,
        rateRevision: 3,
        currencyCode: 'CNY',
        actualFixedOverheadAmount: 12000,
        actualVariableOverheadAmount: 3600,
        actualTotalOverheadAmount: 15600,
        appliedMachineTicks: 4320000000000,
        appliedMachineHours: 120,
        appliedFixedAmount: applied - 3600,
        appliedVariableAmount: 3600,
        appliedTotalAmount: applied,
        appliedRoundingDifferenceAmount: 0,
        underOverAppliedFixedAmount: 15600 - applied,
        underOverAppliedVariableAmount: 0,
        underOverAppliedTotalAmount: 15600 - applied,
        unallocatedFixedOverheadAmount: Math.max(15600 - applied, 0),
        overAppliedFixedOverheadAmount: Math.max(applied - 15600, 0),
        abnormalDowntimeTicks: 0,
        abnormalDowntimeHours: 0,
        abnormalDowntimeDisposition: 'None',
        isReadyForClose: true,
        reconciliationStatus: 'available',
        unavailableReason: null,
        recordedBy: '财务核算员',
        sourceReference: 'FIN-202609-CNC',
        reason: '月末费用核对',
        recordedAtUtc: '2026-09-30T08:00:00Z',
      },
    ],
  }
}
function createState() {
  return {
    ready: shallowRef(true),
    workOrder: reactive({ id: 'WO-202609-0186', page: 1, pageSize: 10 }),
    monthly: reactive({ period: '2026-09', workCenterId: '', page: 1, pageSize: 10 }),
    workOrderData: shallowRef<BusinessConsoleErpWorkOrderCostVarianceResponse | undefined>(
      orderFixture(),
    ),
    monthlyData: shallowRef<
      BusinessConsoleErpMachineOverheadReconciliationListResponse | undefined
    >(monthFixture()),
    workOrderPending: shallowRef(false),
    monthlyPending: shallowRef(false),
    workOrderError: shallowRef<Error | undefined>(undefined),
    monthlyError: shallowRef<Error | undefined>(undefined),
    refreshWorkOrder: vi.fn(),
    refreshMonthly: vi.fn(),
  }
}
let data: ReturnType<typeof createState>
const render = () =>
  mount(MachineOverheadPage, {
    global: {
      stubs: { BusinessLayout: { template: '<main><slot /></main>' }, DirectoryPicker: true },
    },
  })

// PublicContract / DomainInvariant: #2386 的状态与金额呈现验收；#2385 的公开读契约。
describe('机器制造费用财务读面', () => {
  beforeEach(() => {
    data = createState()
    state.current = data
  })

  it('呈现预定分配与冻结费率、结算来源，保持两套服务端分页', async () => {
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).toContain('不代表已核定实际机器成本')
    expect(wrapper.text()).toContain('CNY 15,600.00')
    expect(wrapper.text()).toContain('120 小时')
    expect(wrapper.text()).toContain('SET-0186-2 / 2')
    expect(wrapper.text()).toContain('费率版本 3')
    expect(wrapper.text()).toContain('EVT-0186-2')
    const tables = wrapper.findAllComponents({ name: 'NvDataTable' })
    expect(tables[0].props('totalItems')).toBe(21)
    expect(tables[1].props('totalItems')).toBe(12)
    tables[0].vm.$emit('update:page', 2)
    tables[1].vm.$emit('update:page', 3)
    expect(data.workOrder.page).toBe(2)
    expect(data.monthly.page).toBe(3)
  })

  it.each([
    [12000, '未分配 +CNY 3,600.00'],
    [18000, '多分配 CNY -2,400.00'],
    [15600, '无差异 CNY 0.00'],
  ])('低负荷、高负荷和无差异按实际池减已分配显示：%s', async (applied, expected) => {
    data.monthlyData.value = monthFixture(applied as number)
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).toContain(expected)
    expect(wrapper.text()).toContain('实际池 − 已分配额')
    const monthlyRow = wrapper.findAll('section')[1].findAll('tbody tr')[0]
    expect(monthlyRow.text()).toContain('WC-CNC-01')
    const actualPool = monthlyRow.findAll('td')[2]
    expect(actualPool.text()).toContain('固定 CNY 12,000.00')
    expect(actualPool.text()).toContain('变动 CNY 3,600.00')
    expect(actualPool.text()).toContain('合计 CNY 15,600.00')
  })

  it('明确零仍是可用金额和工时', async () => {
    data.workOrderData.value = {
      ...orderFixture(),
      actualMachineHours: 0,
      appliedFixedMachineOverhead: 0,
      appliedVariableMachineOverhead: 0,
      appliedMachineOverheadTotal: 0,
      machineOverheadOperations: [],
    }
    const wrapper = render()
    await flushPromises()
    const orderSection = wrapper.findAll('section')[0]
    for (const [label, value] of [
      ['机器实绩工时', '0 小时'],
      ['固定预定分配', 'CNY 0.00'],
      ['变动预定分配', 'CNY 0.00'],
      ['预定分配合计', 'CNY 0.00'],
    ]) {
      const metricLabel = orderSection.findAll('p').find((element) => element.text() === label)
      expect(metricLabel?.element.parentElement?.textContent).toContain(value)
    }
    expect(wrapper.find('[role="status"]').text()).toContain('可用')
  })

  it.each([
    ['notApplicable', 'machine_overhead_not_applicable', '不适用'],
    ['unavailable', 'operation_not_settled', '缺少有效机器结算'],
    ['unavailable', 'machine_overhead_rate_not_configured', '尚未配置机器制造费用费率'],
  ] as const)('区分 %s 及其原因 %s', async (status, reason, expected) => {
    data.workOrderData.value = {
      ...orderFixture(),
      machineCostStatus: status,
      machineCostUnavailableReason: reason,
      actualMachineHours: null,
      appliedFixedMachineOverhead: null,
      appliedVariableMachineOverhead: null,
      appliedMachineOverheadTotal: null,
      machineOverheadOperations: [],
    }
    const wrapper = render()
    await flushPromises()
    const section = wrapper.find('section')
    expect(section.text()).toContain(expected)
    expect(section.text()).not.toContain('CNY 0.00')
    expect(section.text()).not.toContain('15,600')
  })

  it('冲销后有效结算消失时移除旧金额，并标明月度旧版本不可用', async () => {
    const wrapper = render()
    await flushPromises()
    data.workOrderData.value = {
      ...orderFixture(),
      machineCostStatus: 'unavailable',
      machineCostUnavailableReason: 'operation_not_settled',
      actualMachineHours: null,
      appliedFixedMachineOverhead: null,
      appliedVariableMachineOverhead: null,
      appliedMachineOverheadTotal: null,
      totalMachineOverheadOperations: 0,
      machineOverheadOperations: [],
    }
    const month = monthFixture()
    month.items[0].reconciliationStatus = 'unavailable'
    month.items[0].unavailableReason = 'superseded_reconciliation'
    data.monthlyData.value = month
    await flushPromises()
    expect(wrapper.find('section').text()).not.toContain('15,600')
    expect(wrapper.text()).toContain('冲销后需重新结算')
    expect(wrapper.text()).toContain('已被新版本替代')
    expect(wrapper.text()).toContain('此记录不可作为当前关账依据')
  })

  it('刷新失败时不继续展示缓存金额', async () => {
    const wrapper = render()
    data.workOrderError.value = new Error('network')
    data.monthlyError.value = new Error('network')
    await flushPromises()
    expect(wrapper.text()).not.toContain('15,600')
    expect(wrapper.text()).toContain('工单费用读取失败')
    expect(wrapper.text()).toContain('月度差异读取失败')
  })
})
