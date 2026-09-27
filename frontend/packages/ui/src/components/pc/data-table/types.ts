export type NvDataTableAlign = 'start' | 'center' | 'end'

export type NvDataTableDensity = 'comfortable' | 'compact'

export interface NvDataTableFilterOption {
  label: string
  value: string
}

export interface NvDataTableColumn<T = Record<string, unknown>> {
  /** Field key — also the cell slot name (`#cell-<key>`) and default accessor. */
  key: string
  /** Column header label. */
  header: string
  /** Optional accessible tooltip for business guidance that belongs on the column header. */
  headerTitle?: string
  align?: NvDataTableAlign
  /** Enable click-to-sort on this column. */
  sortable?: boolean
  /**
   * Enable column filtering:
   *  - `'text'`  → substring match against the cell's string value.
   *  - `'enum'`  → multi-select among distinct values (or `filterOptions`).
   */
  filter?: 'text' | 'enum'
  /** Explicit enum options; defaults to the distinct values found in `rows`. */
  filterOptions?: NvDataTableFilterOption[]
  /**
   * Column width declaration: a Tailwind width class (`w-40`), a CSS dimension
   * (`120px`), or a Tailwind min-width class (`min-w-[22rem]`). Columns are laid
   * out with `table-layout: fixed`, so the declaration holds: the column is
   * never squeezed narrower to fit its content, and never widened by it. When
   * the declared widths leave spare room, that surplus is shared out — size a
   * column to the content it holds, and leave room for the undeclared columns.
   */
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

export interface NvDataTableSort {
  key: string
  direction: 'asc' | 'desc'
}

/** Active per-column filter state, keyed by column key. */
export type NvDataTableFilters = Record<string, string | string[]>
