import type { Component } from 'vue'

export type TimelineTone = 'brand' | 'success' | 'warning' | 'danger' | 'neutral'

export interface TimelineItem {
  /** Stable key + slot name for custom body content: `#<key>`. */
  key?: string
  /** Primary line (e.g. the event). */
  title?: string
  /** Meta line shown beside the title (e.g. a timestamp or operator). */
  label?: string
  /** Secondary detail under the title. */
  description?: string
  /** Node color. */
  tone?: TimelineTone
  /** Custom node glyph (overrides the dot). */
  icon?: Component
  /** Filled dot (default) or ringed/hollow. */
  dotType?: 'solid' | 'hollow'
}
