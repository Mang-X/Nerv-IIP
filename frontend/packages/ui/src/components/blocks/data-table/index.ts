// Superseded by the Pro-layer NvDataTable (was DataTablePro); this legacy block is
// kept only for the transition and is removed at codemod closure (#789 / ADR 0020 §1.3).
export {
  /** @deprecated Superseded by `NvDataTable` (was DataTablePro) per ADR 0020; removed after codemod #789. */
  default as DataTable,
} from './DataTable.vue'
export {
  /** @deprecated Superseded by `NvDataTablePagination` per ADR 0020; removed after codemod #789. */
  default as DataTablePagination,
} from './DataTablePagination.vue'
export type { DataTableAlign, DataTableColumn, DataTableSort } from './types'
