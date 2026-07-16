/**
 * 收货质检门禁状态（镜像后端 WMS `InboundQualityGateStatuses`，纯 TS）。
 *
 * 后端权威定义：backend/services/Business/Wms/.../InboundOrder.cs
 *   pending / passed / conditional-release / rejected / not-required
 * 放行上架判据 `IsReleasedForPutaway = passed || conditional-release`——本文件
 * 逐条镜像，保证「待检单据不出现上架引导」在移动端与后端同口径。
 */

export const RECEIVING_QUALITY_GATE_STATUS = {
  pending: 'pending',
  passed: 'passed',
  conditionalRelease: 'conditional-release',
  rejected: 'rejected',
  notRequired: 'not-required',
} as const

export type ReceivingQualityGateStatus =
  (typeof RECEIVING_QUALITY_GATE_STATUS)[keyof typeof RECEIVING_QUALITY_GATE_STATUS]

const LABELS: Record<string, string> = {
  pending: '待检',
  passed: '合格',
  'conditional-release': '有条件放行',
  rejected: '不合格',
  'not-required': '免检',
}

/** 门禁状态码 → 中文标签；未知/空码回退「待检」（保守，宁可拦上架也不误放行）。 */
export function receivingQualityGateStatusLabel(status: string | null | undefined): string {
  if (!status) return '待检'
  return LABELS[status.toLowerCase()] ?? '待检'
}

/** 单行是否已放行上架（镜像后端：passed 或 conditional-release）。 */
export function isReleasedForPutaway(status: string | null | undefined): boolean {
  const s = status?.toLowerCase()
  return (
    s === RECEIVING_QUALITY_GATE_STATUS.passed ||
    s === RECEIVING_QUALITY_GATE_STATUS.conditionalRelease
  )
}

/** 单行是否需要质检（非 not-required 即需检）。 */
export function requiresQualityInspection(status: string | null | undefined): boolean {
  return status?.toLowerCase() !== RECEIVING_QUALITY_GATE_STATUS.notRequired
}

/**
 * 一张单多行门禁状态 → 单据级汇总状态（用于列表状态标）。
 * 优先级：不合格 > 待检 > 有条件放行 > 合格 > 免检——即先暴露最需要处置的状态。
 * 无行返回空串（未收货，无状态标）。
 */
export function aggregateReceivingGateStatus(
  statuses: ReadonlyArray<string | null | undefined>,
): string {
  const norm = statuses.map((s) => s?.toLowerCase()).filter(Boolean) as string[]
  if (norm.length === 0) return ''
  if (norm.includes(RECEIVING_QUALITY_GATE_STATUS.rejected))
    return RECEIVING_QUALITY_GATE_STATUS.rejected
  if (norm.includes(RECEIVING_QUALITY_GATE_STATUS.pending))
    return RECEIVING_QUALITY_GATE_STATUS.pending
  if (norm.includes(RECEIVING_QUALITY_GATE_STATUS.conditionalRelease))
    return RECEIVING_QUALITY_GATE_STATUS.conditionalRelease
  if (norm.includes(RECEIVING_QUALITY_GATE_STATUS.passed))
    return RECEIVING_QUALITY_GATE_STATUS.passed
  return RECEIVING_QUALITY_GATE_STATUS.notRequired
}

/**
 * 整单是否可上架：至少一行且每行都放行或免检，且无任何一行待检/不合格。
 * 满足「待检单据不出现上架引导」。
 */
export function orderReleasedForPutaway(
  statuses: ReadonlyArray<string | null | undefined>,
): boolean {
  const norm = statuses.map((s) => s?.toLowerCase()).filter(Boolean) as string[]
  if (norm.length === 0) return false
  return norm.every(
    (s) =>
      s === RECEIVING_QUALITY_GATE_STATUS.passed ||
      s === RECEIVING_QUALITY_GATE_STATUS.conditionalRelease ||
      s === RECEIVING_QUALITY_GATE_STATUS.notRequired,
  )
}
