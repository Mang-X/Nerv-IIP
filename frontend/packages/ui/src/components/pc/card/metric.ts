/**
 * Shared vocabulary for the metric-card family (NvMetricCard / NvMetricRing /
 * NvMetricStrip). Tone → token-class maps mirror NvStatusBadge so a metric's
 * accent reads the same as the status badges in the tables it summarises.
 */

/** Which structured bottom-zone a metric card renders. */
export type NvMetricVariant =
  | 'default'
  | 'icon'
  | 'sparkline'
  | 'target'
  | 'breakdown'
  | 'bars'
  | 'alert'
  | 'facets'

/** Semantic tone shared across the metric family (danger → destructive tokens). */
export type NvMetricTone = 'brand' | 'success' | 'warning' | 'danger' | 'neutral'

export type NvMetricDeltaDirection = 'up' | 'down' | 'flat'

export interface NvMetricDelta {
  /** Pre-formatted change, e.g. `8.2%`, `+5`, `0.4pt`. */
  value: string
  direction?: NvMetricDeltaDirection
  /**
   * Force the chip's semantic tone. Default mapping: up → success,
   * down → danger, flat → neutral. Override when an up-tick is bad
   * (e.g. 超期工单 +2 should read destructive while keeping the up arrow).
   */
  tone?: Extract<NvMetricTone, 'success' | 'danger' | 'neutral'>
}

/** One slice of a `breakdown` card — a share of the headline total. */
export interface NvMetricSegment {
  label: string
  value: number
  tone?: NvMetricTone
}

/** One dimension chip of a `facets` card. */
export interface NvMetricFacet {
  label: string
  value: string | number
  /** `danger`/`warning` tint the chip to flag an at-risk dimension. */
  tone?: NvMetricTone
}

/** A pill (e.g. 需处理 / 正常) shown top-right of an `alert` card. */
export interface NvMetricStatus {
  label: string
  tone: NvMetricTone
}

/** Footer call-to-action on an `alert` card — renders a link when `href` is set, else emits `action`. */
export interface NvMetricAction {
  label: string
  href?: string
}

/** One constituent factor row beside the gauge in NvMetricRing. */
export interface NvMetricFactor {
  label: string
  value: string | number
}

/** One cell of an NvMetricStrip. */
export interface NvMetricStripCell {
  label: string
  value: string | number
  unit?: string
  /** Emphasise the value with a tone (e.g. 超期数用 danger). */
  valueTone?: NvMetricTone
  /** Sub-line under the value: a delta or a short note. */
  meta?: string
  metaTone?: NvMetricDeltaDirection | 'neutral'
}

/** tone → tinted-surface classes (background + strong text), mirrors NvStatusBadge. */
export const metricToneTint: Record<NvMetricTone, string> = {
  brand: 'bg-brand/10 text-brand-strong',
  success: 'bg-success/10 text-success-strong',
  warning: 'bg-warning/15 text-warning-strong',
  danger: 'bg-destructive/10 text-destructive-strong',
  neutral: 'bg-muted text-muted-foreground',
}

/** tone → strong text colour only. */
export const metricToneText: Record<NvMetricTone, string> = {
  brand: 'text-brand-strong',
  success: 'text-success-strong',
  warning: 'text-warning-strong',
  danger: 'text-destructive-strong',
  neutral: 'text-muted-foreground',
}

/** tone → solid fill (progress bars, segments, bars, swatches). */
export const metricToneFill: Record<NvMetricTone, string> = {
  brand: 'bg-brand',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-destructive',
  neutral: 'bg-muted-foreground/40',
}

/**
 * tone → gauge stroke colour (CSS var for the SVG ring arc). Canonical `--nv-*`
 * tokens, not the one-cycle `--brand`/`--success`/`--warning` aliases theme.css
 * marks for removal; `--destructive`/`--muted-foreground` have no `--nv-` form
 * (same split NvStatusDot uses).
 */
export const metricToneStroke: Record<NvMetricTone, string> = {
  brand: 'var(--nv-brand)',
  success: 'var(--nv-success)',
  warning: 'var(--nv-warning)',
  danger: 'var(--destructive)',
  neutral: 'var(--muted-foreground)',
}

/** Resolve a delta's semantic tone from its (optional) override + direction. */
export function resolveDeltaTone(delta: NvMetricDelta): NvMetricTone {
  if (delta.tone) return delta.tone
  if (delta.direction === 'up') return 'success'
  if (delta.direction === 'down') return 'danger'
  return 'neutral'
}
