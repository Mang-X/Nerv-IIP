export type DataTableProAlign = 'start' | 'center' | 'end'

export type DataTableProDensity = 'comfortable' | 'compact'

export interface DataTableProFilterOption {
  label: string
  value: string
}

export interface DataTableProColumn<T = Record<string, unknown>> {
  /** Field key — also the cell slot name (`#cell-<key>`) and default accessor. */
  key: string
  /** Column header label. */
  header: string
  align?: DataTableProAlign
  /** Enable click-to-sort on this column. */
  sortable?: boolean
  /**
   * Enable column filtering:
   *  - `'text'`  → substring match against the cell's string value.
   *  - `'enum'`  → multi-select among distinct values (or `filterOptions`).
   */
  filter?: 'text' | 'enum'
  /** Explicit enum options; defaults to the distinct values found in `rows`. */
  filterOptions?: DataTableProFilterOption[]
  /** Tailwind width class (e.g. `w-40`) or a CSS width string (e.g. `120px`). */
  width?: string
  headerClass?: string
  cellClass?: string
  /** Allow hiding via the column-settings menu. Default `true`. */
  hideable?: boolean
  /** Start hidden (still toggleable unless `hideable` is false). */
  defaultHidden?: boolean
  /** Value accessor; defaults to `row[key]`. Drives default render, sort, filter, search. */
  accessor?: (row: T) => unknown
}

export interface DataTableProSort {
  key: string
  direction: 'asc' | 'desc'
}

/** Active per-column filter state, keyed by column key. */
export type DataTableProFilters = Record<string, string | string[]>
