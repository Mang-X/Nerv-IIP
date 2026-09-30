import type { BusinessConsoleMaterialDeliveryStatus } from '@nerv-iip/api-client'
import type { StatusTone } from '@nerv-iip/ui'

export const materialDeliveryStatuses: Record<
  BusinessConsoleMaterialDeliveryStatus,
  { label: string; tone: StatusTone }
> = {
  green: { label: '可控', tone: 'success' },
  yellow: { label: '紧张', tone: 'warning' },
  red: { label: '已晚', tone: 'danger' },
}
const sourceLabels: Record<string, string> = {
  'supply-insufficient': '供应不足',
  'demand-due-source-missing': '需求交期来源缺失',
  'plan-not-selected': '未选择 APS 方案',
  'production-suggestion-not-linked': '未关联生产建议',
  'production-demand-due-source-missing': '生产需求交期来源缺失',
  'scheduling-source-missing': '排程来源缺失',
  'latest-start-missing': '缺少最晚开工界限',
  'expected-start-missing': '缺少实际排程开始',
  'evaluation-after-latest-start': '评价时点已过最晚开工',
  'arrival-after-latest-start': '预计到达晚于最晚开工',
  'start-after-latest-start': '实际排程开始晚于最晚开工',
  'at-latest-start-boundary': '处于最晚开工界限',
  'work-order-not-linked': '未关联工单',
  'work-order-not-found': '工单来源缺失',
  'problem-snapshot-missing': '方案输入快照缺失',
  'order-not-in-plan': '工单不在所选方案中',
  'remaining-route-missing': '剩余工艺来源缺失',
  'net-requirement-identity-unknown': '净需求身份未知',
  unscheduled: '未排程',
  scheduled: '已排程',
  completed: '已完成',
  sales: '销售订单',
  'sales-order': '销售订单',
  forecast: '预测',
  mps: '主计划',
  preview: '预览',
  generated: '已生成',
  released: '已发布',
  superseded: '已被替代',
  revoked: '已撤回',
  Open: '待处理',
  Accepted: '已接受',
  Rejected: '已忽略',
  Superseded: '已被替代',
  Draft: '草稿',
  Released: '已发布',
  Revoked: '已撤回',
}
export function materialDeliverySourceLabel(value?: string) {
  return value ? (sourceLabels[value] ?? value) : '—'
}
/** 时间事实统一以 UTC 展示，日期事实仍保留来源原日期；title 保留原始值。 */
export function materialDeliveryUtc(value?: string | null) {
  return value ? `${new Date(value).toISOString().slice(0, 19).replace('T', ' ')} UTC` : '—'
}
