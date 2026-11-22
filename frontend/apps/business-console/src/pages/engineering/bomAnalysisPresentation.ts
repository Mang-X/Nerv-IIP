import type {
  BusinessConsoleBomExplosionDiagnostic,
  BusinessConsoleBomExplosionNode,
  BusinessConsoleBomDiffLineItem,
  BusinessConsoleBomWhereUsedItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, StatusTone } from '@nerv-iip/ui'

export interface TreeRow extends BusinessConsoleBomExplosionNode {
  key: string
  depth: number
  hasChildren: boolean
}

export const treeColumns: NvDataTableColumn<TreeRow>[] = [
  { key: 'itemCode', header: '物料', cellClass: 'font-medium' },
  { key: 'requiredQuantity', header: '需求量', align: 'end', width: 'w-28' },
  { key: 'unitOfMeasureCode', header: '单位', width: 'w-24' },
  { key: 'bomCode', header: 'BOM 版本' },
  { key: 'flags', header: '属性', width: 'w-44' },
]
export const explosionColumns: NvDataTableColumn<TreeRow>[] = [
  { key: 'itemCode', header: '物料', cellClass: 'font-medium' },
  { key: 'itemName', header: '名称' },
  { key: 'level', header: '层级', align: 'end', width: 'w-20' },
  { key: 'lineQuantity', header: '行数量', align: 'end', width: 'w-24' },
  { key: 'requiredQuantity', header: '滚算需求', align: 'end', width: 'w-28' },
  { key: 'yield', header: '损耗/得率', align: 'end', width: 'w-28' },
  { key: 'alternates', header: '替代/位号' },
]
export const whereUsedColumns: NvDataTableColumn<BusinessConsoleBomWhereUsedItem>[] = [
  { key: 'parentItemCode', header: '父项', cellClass: 'font-medium' },
  { key: 'parentItemName', header: '名称' },
  { key: 'bomCode', header: 'BOM 版本' },
  { key: 'lineQuantity', header: '用量', align: 'end', width: 'w-24' },
  { key: 'unitOfMeasureCode', header: '单位', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
  { key: 'flags', header: '行属性', width: 'w-44' },
]
export const diffColumns: NvDataTableColumn<BusinessConsoleBomDiffLineItem>[] = [
  { key: 'changeType', header: '变化', width: 'w-24' },
  { key: 'itemCode', header: '物料', cellClass: 'font-medium' },
  { key: 'itemName', header: '名称' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-32' },
  { key: 'uom', header: '单位', width: 'w-28' },
  { key: 'rates', header: '损耗/得率', align: 'end', width: 'w-40' },
  { key: 'fields', header: '字段变化' },
]
export const diagnosticColumns: NvDataTableColumn<BusinessConsoleBomExplosionDiagnostic>[] = [
  { key: 'severity', header: '级别', width: 'w-24' },
  { key: 'itemCode', header: '物料', width: 'w-32' },
  { key: 'message', header: '诊断' },
  { key: 'path', header: '路径' },
]

export function flattenBom(
  node: BusinessConsoleBomExplosionNode | undefined,
  depth = 0,
  prefix = 'root',
): TreeRow[] {
  if (!node) return []
  const children = node.children ?? []
  return [
    {
      ...node,
      key: `${prefix}:${node.itemCode ?? 'node'}:${node.bomCode ?? ''}:${node.revision ?? ''}`,
      depth,
      hasChildren: children.length > 0,
    },
    ...children.flatMap((child, index) => flattenBom(child, depth + 1, `${prefix}.${index}`)),
  ]
}

export function formatQty(value?: number | null) {
  if (value === null || value === undefined) return '无'
  return Number(value).toLocaleString('zh-CN', { maximumFractionDigits: 6 })
}

export function formatRate(value?: number | null) {
  if (value === null || value === undefined) return '无'
  return Number(value).toLocaleString('zh-CN', { style: 'percent', maximumFractionDigits: 2 })
}

export function flagLabels(
  row: Pick<TreeRow, 'isPhantom' | 'backflush' | 'alternateGroup' | 'alternatePriority'>,
) {
  const labels: string[] = []
  if (row.isPhantom) labels.push('虚拟件')
  if (row.backflush) labels.push('倒冲')
  if (row.alternateGroup)
    labels.push(
      `替代组 ${row.alternateGroup}${row.alternatePriority ? `/${row.alternatePriority}` : ''}`,
    )
  return labels
}

export function diffChangeLabel(changeType?: string | null) {
  const normalized = (changeType ?? '').toLowerCase()
  if (normalized === 'added') return '新增'
  if (normalized === 'removed') return '删除'
  if (normalized === 'replaced') return '替换'
  if (normalized === 'changed') return '变更'
  return changeType || '变化'
}

export function diffChangeTone(changeType?: string | null): StatusTone {
  const normalized = (changeType ?? '').toLowerCase()
  if (normalized === 'added') return 'success'
  if (normalized === 'removed') return 'danger'
  if (normalized === 'replaced') return 'warning'
  return 'info'
}

export function diffItemLabel(row: BusinessConsoleBomDiffLineItem) {
  if (row.changeType?.toLowerCase() === 'replaced') {
    return `${row.oldItemCode || '无'} -> ${row.newItemCode || '无'}`
  }
  return row.newItemCode || row.oldItemCode || '无'
}

function formatText(value?: number | string | null) {
  if (value === null || value === undefined || value === '') return '无'
  return String(value)
}

export function formatQuantityDiff(row: BusinessConsoleBomDiffLineItem) {
  return `${formatQty(row.oldQuantity)} -> ${formatQty(row.newQuantity)}`
}

export function formatRateDiff(oldValue?: number | null, newValue?: number | null) {
  return `${formatRate(oldValue)} -> ${formatRate(newValue)}`
}

export function formatTextDiff(oldValue?: string | null, newValue?: string | null) {
  return `${formatText(oldValue)} -> ${formatText(newValue)}`
}

export function fieldChangeLabels(row: BusinessConsoleBomDiffLineItem) {
  const labels =
    row.fieldChanges?.map(
      (field) =>
        `${fieldLabel(field.fieldName)}: ${field.oldValue ?? '无'} -> ${field.newValue ?? '无'}`,
    ) ?? []
  return labels.length ? labels : ['无']
}

function fieldLabel(fieldName?: string | null) {
  const labels: Record<string, string> = {
    quantity: '数量',
    unitOfMeasureCode: '单位',
    scrapRate: '损耗',
    yieldRate: '得率',
    isPhantom: '虚拟件',
    alternateGroup: '替代组',
    alternatePriority: '替代优先级',
    substituteSkuCodes: '替代料',
    referenceDesignators: '位号',
    backflush: '倒冲',
  }
  return labels[fieldName ?? ''] ?? (fieldName || '字段')
}

export function diagnosticTone(severity?: string | null): StatusTone {
  return (severity ?? '').toLowerCase() === 'error' ? 'danger' : 'warning'
}

export function diagnosticLabel(severity?: string | null) {
  return (severity ?? '').toLowerCase() === 'error' ? '错误' : '警告'
}
