import type { BusinessConsoleMachineOverheadReadStatus } from '@nerv-iip/api-client'

export const machineStatusLabels: Record<BusinessConsoleMachineOverheadReadStatus, string> = {
  available: '可用',
  notApplicable: '不适用',
  unavailable: '不可用',
}

const reasons: Record<string, string> = {
  operation_not_settled: '缺少有效机器结算，请核对机器事实及费率；冲销后需重新结算',
  machine_overhead_not_applicable: '没有适用的机器制造费用',
  machine_overhead_rate_not_configured: '尚未配置机器制造费用费率',
  accounting_period_not_found: '会计期间不存在',
  abnormal_downtime_pending: '异常停机费用待处理',
  superseded_reconciliation: '已被新版本替代，仅供历史追溯',
  currency_conflict: '币种不一致，无法合计',
  numeric_overflow: '金额或工时超出可计算范围',
  work_order_cost_not_found: '尚无工单成本记录',
  work_order_not_completed: '工单尚未完成',
  reconciliation_not_recorded: '尚未登记月度对账',
  machine_overhead_rate_changed: '费率已变更，请重新对账',
  active_settlement_currency_conflict: '有效结算币种不一致，请核对后重新对账',
  active_settlement_changed: '有效结算已变化，请重新对账',
}

export function machineReason(reason: string | null) {
  return reason ? (reasons[reason] ?? '核算条件未满足，请联系财务核对') : ''
}

export function machineAmount(value: number | null, currency: string | null) {
  if (value === null || currency === null) return '—'
  return `${currency} ${value.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 6 })}`
}

export function machineHours(value: number | null) {
  return value === null
    ? '—'
    : `${value.toLocaleString('zh-CN', { maximumFractionDigits: 6 })} 小时`
}

export function allocationDifference(value: number, currency: string) {
  return `${value > 0 ? '未分配 +' : value < 0 ? '多分配 ' : '无差异 '}${machineAmount(value, currency)}`
}
