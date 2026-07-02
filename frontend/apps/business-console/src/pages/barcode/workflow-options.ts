export const BARCODE_SCAN_WORKFLOW_OPTIONS = [
  { value: 'production.report', label: '生产报工' },
  { value: 'wms.receiving', label: '仓储收货' },
  { value: 'inventory.receipt', label: '库存入库' },
  { value: 'inventory.issue', label: '库存出库' },
  { value: 'inventory.adjustment', label: '库存调整' },
  { value: 'inventory.count', label: '库存盘点' },
  { value: 'quality.inspection', label: '质量检验' },
] as const

export type BarcodeScanWorkflow = typeof BARCODE_SCAN_WORKFLOW_OPTIONS[number]['value']

export function isBarcodeScanWorkflow(value?: string | null): value is BarcodeScanWorkflow {
  return typeof value === 'string' && BARCODE_SCAN_WORKFLOW_OPTIONS.some((option) => option.value === value)
}

export function barcodeScanWorkflowLabel(value?: string | null) {
  if (!value) return '未标注'
  return BARCODE_SCAN_WORKFLOW_OPTIONS.find((option) => option.value === value)?.label ?? value
}
