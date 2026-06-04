export type DataTableAlign = 'start' | 'center' | 'end'

export interface DataTableColumn<T = Record<string, unknown>> {
  /** Field key — also the cell slot name (`#cell-<key>`) and default accessor. */
  key: string
  /** Column header label. */
  header: string
  align?: DataTableAlign
  /** Enable click-to-sort on this column. */
  sortable?: boolean
  /** Tailwind width class (e.g. `w-40`) or a CSS width string (e.g. `120px`). */
  width?: string
  headerClass?: string
  cellClass?: string
  /** Value accessor; defaults to `row[key]`. Used for default rendering + sorting. */
  accessor?: (row: T) => unknown
}

export interface DataTableSort {
  key: string
  direction: 'asc' | 'desc'
}
