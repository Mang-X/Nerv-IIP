import type { RouteLocationRaw } from 'vue-router'
import type { SourceDocumentKind } from '@/composables/useSourceDocumentCatalog'

export const BARCODE_SCAN_WORKFLOW_OPTIONS = [
  { value: 'production.report', label: '生产报工' },
  { value: 'wms.receiving', label: '仓储收货' },
  { value: 'inventory.receipt', label: '库存入库' },
  { value: 'inventory.issue', label: '库存出库' },
  { value: 'inventory.adjustment', label: '库存调整' },
  { value: 'inventory.count', label: '库存盘点' },
  { value: 'quality.inspection', label: '质量检验' },
] as const

export type BarcodeScanWorkflow = (typeof BARCODE_SCAN_WORKFLOW_OPTIONS)[number]['value']

export function isBarcodeScanWorkflow(value?: string | null): value is BarcodeScanWorkflow {
  return (
    typeof value === 'string' &&
    BARCODE_SCAN_WORKFLOW_OPTIONS.some((option) => option.value === value)
  )
}

export function barcodeScanWorkflowLabel(value?: string | null) {
  if (!value) return '未标注'
  return BARCODE_SCAN_WORKFLOW_OPTIONS.find((option) => option.value === value)?.label ?? value
}

/**
 * 条码业务对象的口径：每类业务对象从哪个单据目录选、点进去到哪。打印批次、扫码补录和各页互链
 * 共用这一张表，同一个业务对象在哪儿点进去都到同一个地方。
 *
 * 生产报工记的是工单，不是报工单号：生产标签与扫码按行业惯例挂在生产订单（工单）上——
 * GS1 EPCIS 生产赋码事件引用的业务单据是生产订单，报工单只是工单下的一次数量过账。
 * 本系统报工时自动打印的标签批次也按工单登记，追溯页同样以工单为对象。
 */
const SOURCE_DOCUMENTS: Readonly<
  Record<string, { kind: SourceDocumentKind; route: (id: string) => RouteLocationRaw }>
> = {
  'work-order': { kind: 'mes-work-order', route: workOrderRoute },
  'production.report': { kind: 'mes-work-order', route: workOrderRoute },
  'wms.receiving': {
    kind: 'wms-inbound-order',
    route: (id) => ({ path: '/wms/inbound', query: { inboundOrderNo: id } }),
  },
  // 质量检验记被检验的那张单据，检验页按它带入。
  'quality.inspection': {
    kind: 'quality-inspection',
    route: (id) => ({ path: '/quality/inspections', query: { sourceDocumentId: id } }),
  },
}

function workOrderRoute(id: string): RouteLocationRaw {
  return `/mes/work-orders/${encodeURIComponent(id)}`
}

/** 该类业务对象的单据目录；没有可搜列表的类型返回 `undefined`，由页面退回自由输入。 */
export function barcodeSourceDocumentKind(type?: string | null): SourceDocumentKind | undefined {
  return type ? SOURCE_DOCUMENTS[type]?.kind : undefined
}

export function barcodeSourceDocumentRoute(
  type: string | null | undefined,
  id: string | null | undefined,
): RouteLocationRaw | undefined {
  const documentId = id?.trim()
  if (!type || !documentId) return undefined
  return SOURCE_DOCUMENTS[type]?.route(documentId)
}
