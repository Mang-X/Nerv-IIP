import { machineAmount, machineHours } from './machineOverhead'

export const costAmount = (value: number | null | undefined, currency: string | null | undefined) =>
  machineAmount(value ?? null, currency ?? null)
export const costHours = (value: number | null | undefined) => machineHours(value ?? null)

const directions: Record<string, string> = {
  favorable: '有利',
  unfavorable: '不利',
  neutral: '无差异',
}
export const efficiencyDirection = (direction: string | null | undefined) =>
  direction ? (directions[direction] ?? '方向待核对') : '暂无差异基准'

const reasons: Record<string, string> = {
  work_order_cost_not_found: '尚无工单成本记录',
  work_order_not_completed: '工单尚未完成，暂无最终差异',
  operation_not_settled: '暂无有效人工结算；冲销后请重新结算',
  missing_output_basis: '缺少覆盖产出，暂无标准基准',
  missing_report_snapshot: '缺少覆盖报工记录，暂无标准基准',
  invalid_theoretical_rate: '理论产出速率未维护或无效，暂无标准基准',
  conflicting_theoretical_rate: '覆盖报工的理论产出速率不一致',
  conflicting_uom: '覆盖报工的计量单位不一致',
  invalid_report_quantity: '报工数量无效，请核对报工记录',
  report_scope_conflict: '报工所属工序或工作中心不一致',
  numeric_scale_out_of_range: '报工数值精度超出核算范围',
  missing_reversed_report_snapshot: '缺少被冲销的原报工记录',
  conflicting_reversal_snapshot: '冲销记录与原报工不一致',
  negative_net_good_quantity: '冲销后的净良品数量为负',
  currency_conflict: '币种不一致，无法合计',
  numeric_overflow: '金额或工时超出核算范围',
  actual_payroll_rate_not_modeled: '未建立实际工资费率，人工费率差异不适用',
}
export const laborReason = (reason: string | null | undefined) =>
  reason ? (reasons[reason] ?? '核算条件未满足，请联系财务核对') : ''
