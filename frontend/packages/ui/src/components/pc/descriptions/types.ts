export interface DescriptionItem {
  /** Field label. */
  label: string
  /** Field value; omit and use the `#<key>` slot for custom rendering. */
  value?: string | number | null
  /** How many columns this item spans (clamped to `columns`). */
  span?: number
  /** Slot name for a custom value cell: `#<key>`. */
  key?: string
}
