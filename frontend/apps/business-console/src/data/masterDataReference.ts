/**
 * 基础数据 · 前端受控值（字典 CodeSet）常量 —— Phase 1 数据源。
 *
 * 背景：后端 ReferenceData 暂无「按 CodeSet 列出」端点，也无生产种子（见
 * docs/architecture/master-data-module-product-design.md §5、§7.4 issue #346）。
 * 因此本文件是 Phase 1 的受控值唯一来源，物料表单等下拉从这里取选项。
 * Phase 2 后端字典就绪后，将由 `listReferenceDataByCodeSet(codeSet)` 实时拉取替换，
 * 届时本文件作为兜底/默认即可。CodeSet 命名与 §5.1 对齐。
 */

export interface RefOption {
  /** 提交给后端的码值（自由字符串，后端当前不校验）。 */
  value: string
  /** 展示给用户的中文名。 */
  label: string
}

/** 产品/物料分类（CodeSet: product-category，工厂可维护）。 */
export const PRODUCT_CATEGORY_OPTIONS: RefOption[] = [
  { value: 'electronic', label: '电子料' },
  { value: 'mechanical', label: '机械件' },
  { value: 'plastic', label: '塑胶件' },
  { value: 'hardware', label: '五金件' },
  { value: 'chemical', label: '化学品' },
  { value: 'assembly', label: '组装件' },
]

/** 物料类型（CodeSet: material-type，平台预置，决定单据可用性）。 */
export const MATERIAL_TYPE_OPTIONS: RefOption[] = [
  { value: 'finished-goods', label: '成品' },
  { value: 'semi-finished', label: '半成品' },
  { value: 'raw-material', label: '原材料' },
  { value: 'packaging', label: '包装物' },
  { value: 'consumable', label: '辅料/消耗品' },
  { value: 'spare-part', label: '备品备件' },
  { value: 'tooling', label: '工装/刀具' },
]

/** 批次追踪策略（CodeSet: batch-tracking-policy，系统枚举）。 */
export const BATCH_TRACKING_OPTIONS: RefOption[] = [
  { value: 'none', label: '不管理' },
  { value: 'optional', label: '可选记录' },
  { value: 'mandatory', label: '强制批次' },
]

/** 序列号追踪策略（CodeSet: serial-tracking-policy，系统枚举）。 */
export const SERIAL_TRACKING_OPTIONS: RefOption[] = [
  { value: 'none', label: '不管理' },
  { value: 'on-receipt', label: '入库赋序' },
  { value: 'on-production', label: '生产赋序' },
  { value: 'on-shipment', label: '出货赋序' },
]

/** 保质期策略（CodeSet: shelf-life-policy，系统枚举）。 */
export const SHELF_LIFE_OPTIONS: RefOption[] = [
  { value: 'none', label: '无保质期' },
  { value: 'fifo', label: '先进先出' },
  { value: 'fefo', label: '先到期先出' },
  { value: 'expiry-controlled', label: '到期管控' },
]

/** 仓储条件（CodeSet: storage-condition，平台预置+工厂可维护）。 */
export const STORAGE_CONDITION_OPTIONS: RefOption[] = [
  { value: 'ambient', label: '常温' },
  { value: 'cold-2-8', label: '冷藏（2-8℃）' },
  { value: 'frozen', label: '冷冻' },
  { value: 'dry', label: '干燥/防潮' },
  { value: 'esd', label: '防静电' },
  { value: 'hazardous', label: '危化品' },
]

/** 条码规则（CodeSet: barcode-rule，工厂可维护）。 */
export const BARCODE_RULE_OPTIONS: RefOption[] = [
  { value: 'code128-internal', label: '内部条码（Code128）' },
  { value: 'gs1-128', label: 'GS1-128' },
  { value: 'qr-traceability', label: '追溯二维码' },
  { value: 'customer-spec', label: '客户指定' },
]

/** 计量单位（CodeSet: uom，§5.2 种子的常用子集）。 */
export const UOM_OPTIONS: RefOption[] = [
  { value: 'PCS', label: '个/件' },
  { value: 'SET', label: '套' },
  { value: 'BOX', label: '箱' },
  { value: 'KG', label: '千克' },
  { value: 'G', label: '克' },
  { value: 'M', label: '米' },
  { value: 'L', label: '升' },
]

/** 业务伙伴角色（CodeSet: partner-type，系统枚举）。 */
export const PARTNER_TYPE_OPTIONS: RefOption[] = [
  { value: 'customer', label: '客户' },
  { value: 'supplier', label: '供应商' },
  { value: 'carrier', label: '承运商' },
]

/** 把码值映射为中文名（找不到则原样返回）。 */
export function refLabel(options: RefOption[], value: string | undefined | null): string {
  if (!value) return '无'
  return options.find((o) => o.value === value)?.label ?? value
}
