import type { NvMetricSegment } from '@nerv-iip/ui'

/**
 * 分页列表的「构成」指标卡：卡片主数值取筛选总数（与页头计数同口径），
 * 但状态分段只能按已取回的行统计。差额补一段中性「尚未加载」，使
 * 分段之和恒等于主数值——这是 NvMetricCard `breakdown` 的语义前提，
 * 否则分段条的百分比会按一个用户看不见的分母绘制。
 * 结果集一页装得下时（演示与多数现场场景）不会出现补段。
 */
export function pagedBreakdownSegments(
  total: number,
  segments: NvMetricSegment[],
): NvMetricSegment[] {
  const counted = segments.reduce((sum, segment) => sum + segment.value, 0)
  const rest = Math.max(0, total - counted)
  if (rest <= 0) return segments
  return [...segments, { key: 'not-loaded', label: '尚未加载', value: rest, tone: 'neutral' }]
}
