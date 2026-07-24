import type { BusinessConsoleOrderUrgency } from '@nerv-iip/api-client'
import type { StatusTone } from '@nerv-iip/ui'

// MAN-590 / #1061: seven presentation modes over the unified order-urgency V1 result.
// Switching a mode ONLY changes what the badge renders — it never recomputes or
// refetches the backend urgency result (the facade stays the single source of truth).
export type UrgencyDisplayMode =
  | 'level'
  | 'businessPriority'
  | 'dynamicUrgency'
  | 'executionRisk'
  | 'criticalRatio'
  | 'slack'
  | 'expectedDelay'

export interface UrgencyDisplayModeOption {
  value: UrgencyDisplayMode
  label: string
}

export const URGENCY_DISPLAY_MODES: readonly UrgencyDisplayModeOption[] = [
  { value: 'level', label: '统一等级' },
  { value: 'businessPriority', label: '业务优先级' },
  { value: 'dynamicUrgency', label: '动态紧迫度' },
  { value: 'executionRisk', label: '执行风险' },
  { value: 'criticalRatio', label: 'Critical Ratio' },
  { value: 'slack', label: 'Slack' },
  { value: 'expectedDelay', label: '预计延迟' },
]

export const DEFAULT_URGENCY_DISPLAY_MODE: UrgencyDisplayMode = 'level'

interface LevelPresentation {
  label: string
  tone: StatusTone
}

// Shared unified-level vocabulary; reused by the badge and every display mode so
// the tone always reflects the final unified risk (color = risk, label = mode).
export function urgencyLevelPresentation(level?: string | null): LevelPresentation {
  switch ((level ?? '').toLowerCase()) {
    case 'critical':
      return { label: '特急', tone: 'danger' }
    case 'urgent':
      return { label: '紧急', tone: 'danger' }
    case 'highrisk':
      return { label: '高风险', tone: 'warning' }
    case 'attention':
      return { label: '关注', tone: 'warning' }
    case 'normal':
      return { label: '正常', tone: 'success' }
    default:
      return { label: '未计算', tone: 'neutral' }
  }
}

function formatHours(value?: number | null): string {
  return value == null ? '—' : `${value}h`
}

function formatRatio(value?: number | null): string {
  return value == null ? '—' : String(value)
}

// Presentation for one display mode. `tone` is always derived from the unified
// level so a mode switch never changes the color semantics, only the label.
export function formatUrgencyDisplay(
  urgency: BusinessConsoleOrderUrgency | undefined,
  mode: UrgencyDisplayMode,
): LevelPresentation {
  const tone = urgencyLevelPresentation(urgency?.level).tone
  if (!urgency) return { label: '未计算', tone: 'neutral' }

  switch (mode) {
    case 'businessPriority':
      return { label: (urgency.businessPriority?.level ?? 'p2').toUpperCase(), tone }
    case 'dynamicUrgency':
      return { label: urgencyLevelPresentation(urgency.timeCriticality?.level).label, tone }
    case 'executionRisk':
      return { label: urgencyLevelPresentation(urgency.executionRisk?.level).label, tone }
    case 'criticalRatio':
      return { label: `CR ${formatRatio(urgency.timeCriticality?.criticalRatio)}`, tone }
    case 'slack':
      return { label: `Slack ${formatHours(urgency.timeCriticality?.slackHours)}`, tone }
    case 'expectedDelay':
      return { label: `延误 ${formatHours(urgency.timeCriticality?.expectedDelayHours)}`, tone }
    case 'level':
    default:
      return { label: urgencyLevelPresentation(urgency.level).label, tone }
  }
}

const URGENCY_LEVEL_RANK: Record<string, number> = {
  critical: 5,
  urgent: 4,
  highrisk: 3,
  attention: 2,
  normal: 1,
}

function levelRank(level?: string | null): number {
  return URGENCY_LEVEL_RANK[(level ?? '').toLowerCase()] ?? 0
}

function timestamp(value?: string | null): number | undefined {
  if (!value) return undefined
  const parsed = new Date(value).getTime()
  return Number.isNaN(parsed) ? undefined : parsed
}

// Default ordering, independent of the selected display mode (a row keeps its
// position regardless of which fact the badge is currently showing):
//   unified level (desc) → CR (asc) → expected delay (desc) → due (asc) → waiting
//   time (desc, when the fact exists).
// Rows without an urgency result sort last. Comparisons over a missing fact are
// skipped so the next tiebreaker decides, never forcing a spurious order.
export function compareOrderUrgency(
  a: BusinessConsoleOrderUrgency | undefined,
  b: BusinessConsoleOrderUrgency | undefined,
): number {
  if (!a && !b) return 0
  if (!a) return 1
  if (!b) return -1

  const rankDelta = levelRank(b.level) - levelRank(a.level)
  if (rankDelta !== 0) return rankDelta

  const crDelta = compareNumeric(
    a.timeCriticality?.criticalRatio,
    b.timeCriticality?.criticalRatio,
    'asc',
  )
  if (crDelta !== 0) return crDelta

  const delayDelta = compareNumeric(
    a.timeCriticality?.expectedDelayHours,
    b.timeCriticality?.expectedDelayHours,
    'desc',
  )
  if (delayDelta !== 0) return delayDelta

  const dueDelta = compareNumeric(
    timestamp(a.timeCriticality?.dueUtc),
    timestamp(b.timeCriticality?.dueUtc),
    'asc',
  )
  if (dueDelta !== 0) return dueDelta

  // Waiting time is not part of the V1 contract yet; remainingCycleHours is the
  // closest available fact ("still waiting to finish"), longer waits first.
  return compareNumeric(
    a.timeCriticality?.remainingCycleHours,
    b.timeCriticality?.remainingCycleHours,
    'desc',
  )
}

function compareNumeric(
  a: number | null | undefined,
  b: number | null | undefined,
  direction: 'asc' | 'desc',
): number {
  if (a == null && b == null) return 0
  if (a == null) return 1
  if (b == null) return -1
  const delta = direction === 'asc' ? a - b : b - a
  return delta === 0 ? 0 : delta < 0 ? -1 : 1
}

// Stable urgency ordering of arbitrary host rows: reorders by the shared urgency
// comparator while preserving the incoming order for equal-urgency rows.
export function orderRowsByUrgency<T>(
  rows: readonly T[],
  referenceOf: (row: T) => string | null | undefined,
  byReference: ReadonlyMap<string, BusinessConsoleOrderUrgency>,
): T[] {
  return rows
    .map((row, index) => ({ row, index }))
    .sort((left, right) => {
      const leftUrgency = urgencyFor(left.row, referenceOf, byReference)
      const rightUrgency = urgencyFor(right.row, referenceOf, byReference)
      const delta = compareOrderUrgency(leftUrgency, rightUrgency)
      return delta === 0 ? left.index - right.index : delta
    })
    .map((entry) => entry.row)
}

function urgencyFor<T>(
  row: T,
  referenceOf: (row: T) => string | null | undefined,
  byReference: ReadonlyMap<string, BusinessConsoleOrderUrgency>,
): BusinessConsoleOrderUrgency | undefined {
  const reference = referenceOf(row)?.trim()
  return reference ? byReference.get(reference) : undefined
}
