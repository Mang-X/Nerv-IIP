/**
 * 标签模板「数据项」与后端变量清单（`{version:1, variables:[{name,label,type,required,maxLength}]}`）
 * 之间的互转。页面只给用户看中文数据项，变量名只在这里进出。
 */

/** 平台能给标签填值的数据项。变量名必须与模板文件里的占位名一致。 */
export const LABEL_DATA_ITEMS: ReadonlyArray<{ name: string; label: string }> = [
  { name: 'skuCode', label: '物料编码' },
  { name: 'skuName', label: '物料名称' },
  { name: 'lotNo', label: '批次号' },
  { name: 'serialNo', label: '序列号' },
  { name: 'serialPrefix', label: '序列号前缀' },
  { name: 'gtin', label: '商品条码' },
  { name: 'quantity', label: '数量' },
  { name: 'uomCode', label: '计量单位' },
  { name: 'supplierCode', label: '供应商编码' },
  { name: 'customerCode', label: '客户编码' },
  { name: 'workOrderNo', label: '工单号' },
  { name: 'workCenterCode', label: '工作中心编码' },
  { name: 'workCenterName', label: '工作中心名称' },
  { name: 'siteCode', label: '厂区' },
  { name: 'sourceDocumentId', label: '来源单号' },
  { name: 'productionDate', label: '生产日期' },
  { name: 'expiryDate', label: '有效期' },
  { name: 'printedOn', label: '打印日期' },
]

export const OTHER_DATA_ITEM_LABEL = '其他数据项'
export const DEFAULT_MAX_LENGTH = 200

export interface LabelVariableRow {
  name: string
  label: string
  required: boolean
  maxLength: string
}

const catalogLabelByName = new Map(LABEL_DATA_ITEMS.map((item) => [item.name, item.label]))

export function catalogLabel(name: string) {
  return catalogLabelByName.get(name)
}

export function emptyVariableRow(): LabelVariableRow {
  return { name: '', label: '', required: true, maxLength: String(DEFAULT_MAX_LENGTH) }
}

/** 一行在界面上显示成什么：自填名称 → 目录中文名 → 「其他数据项」。永不回落到变量名。 */
export function rowDisplayLabel(name: string, label?: string | null) {
  return label?.trim() || catalogLabel(name) || OTHER_DATA_ITEM_LABEL
}

/** 读已存的变量清单。旧表单写过的 `{fields:[...]}` 也认，按目录补中文名。 */
export function parseVariableRows(value?: string | null): LabelVariableRow[] {
  if (!value) return []
  let parsed: unknown
  try {
    parsed = JSON.parse(value)
  } catch {
    return []
  }
  if (!parsed || typeof parsed !== 'object') return []
  const { variables, fields } = parsed as { variables?: unknown; fields?: unknown }
  if (Array.isArray(variables)) {
    return variables.flatMap((variable) => {
      if (!variable || typeof variable !== 'object') return []
      const entry = variable as {
        name?: unknown
        label?: unknown
        required?: unknown
        maxLength?: unknown
      }
      if (typeof entry.name !== 'string' || !entry.name.trim()) return []
      return [
        {
          name: entry.name.trim(),
          label: typeof entry.label === 'string' ? entry.label.trim() : '',
          required: entry.required !== false,
          maxLength:
            typeof entry.maxLength === 'number'
              ? String(entry.maxLength)
              : String(DEFAULT_MAX_LENGTH),
        },
      ]
    })
  }
  if (Array.isArray(fields)) {
    return fields
      .filter((field): field is string => typeof field === 'string' && field.trim().length > 0)
      .map((field) => ({
        ...emptyVariableRow(),
        name: field.trim(),
        label: catalogLabel(field.trim()) ?? '',
      }))
  }
  return []
}

/** 行校验：返回第一条中文错误；没有错误返回空串。 */
export function variableRowsError(rows: LabelVariableRow[]) {
  if (rows.length === 0) return '请至少添加一个数据项。'
  const seen = new Set<string>()
  for (const [index, row] of rows.entries()) {
    if (!row.name) return `第 ${index + 1} 行：请选择数据项。`
    if (seen.has(row.name)) return `第 ${index + 1} 行：数据项与前面重复。`
    seen.add(row.name)
    const maxLength = Number(row.maxLength)
    if (!Number.isInteger(maxLength) || maxLength <= 0) {
      return `第 ${index + 1} 行：最大长度需为正整数。`
    }
  }
  return ''
}

/** 拼成后端认的变量清单。 */
export function serializeVariableRows(rows: LabelVariableRow[]) {
  return JSON.stringify({
    version: 1,
    variables: rows.map((row) => ({
      name: row.name,
      ...(row.label.trim() ? { label: row.label.trim() } : {}),
      type: 'string',
      required: row.required,
      maxLength: Number(row.maxLength),
    })),
  })
}

/** 列表「字段说明」列：只出中文。 */
export function variableSummary(value?: string | null) {
  const rows = parseVariableRows(value)
  if (rows.length === 0) return '未配置字段'
  return rows.map((row) => rowDisplayLabel(row.name, row.label)).join('、')
}
