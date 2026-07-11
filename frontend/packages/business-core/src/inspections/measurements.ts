/**
 * 点检测量值 —— PDA（`business-pda` 点检页）与 Console（`business-console` 点检记录页）
 * 共用的**同一套**编辑口径与校验，避免两端漂移（MAN-439 / #793）。
 *
 * payload 结构对齐 api-client 的 `MaintenanceInspectionMeasurementInput`
 * （characteristicCode / measuredValue / uomCode / lowerSpecLimit? / upperSpecLimit?）。
 * 表单行的数值字段以 `string | number` 承载（<input> 绑定），提交前经此处归一。
 */

/** 表单编辑中的测量值行（数值字段以字符串/数字承载，便于 <input v-model> 双向绑定）。 */
export interface MeasurementDraftRow {
  characteristicCode: string
  measuredValue: string | number
  uomCode: string
  lowerSpecLimit: string | number
  upperSpecLimit: string | number
}

/** 提交到点检 facade 的单条测量值（下/上限缺省为 null）。 */
export interface MeasurementPayloadLine {
  characteristicCode: string
  measuredValue: number
  uomCode: string
  lowerSpecLimit: number | null
  upperSpecLimit: number | null
}

/** 数值解析结果：`valid` 表示格式合法，`value` 为归一后的数字（可空时为 null）。 */
export interface ParsedNumber {
  valid: boolean
  value: number | null
}

/** 空白行工厂：新增一行时用。 */
export function createMeasurementDraft(): MeasurementDraftRow {
  return {
    characteristicCode: '',
    measuredValue: '',
    uomCode: '',
    lowerSpecLimit: '',
    upperSpecLimit: '',
  }
}

/** 可空数值：空白视为合法的 null；非空须为有限数。 */
export function parseOptionalNumber(value: string | number | null | undefined): ParsedNumber {
  const trimmed = String(value ?? '').trim()
  if (!trimmed) return { valid: true, value: null }
  const numeric = Number(trimmed)
  return { valid: Number.isFinite(numeric), value: Number.isFinite(numeric) ? numeric : null }
}

/** 必填数值：空白即非法；非空须为有限数。 */
export function parseRequiredNumber(value: string | number | null | undefined): ParsedNumber {
  const trimmed = String(value ?? '').trim()
  if (!trimmed) return { valid: false, value: null }
  const numeric = Number(trimmed)
  return { valid: Number.isFinite(numeric), value: Number.isFinite(numeric) ? numeric : null }
}

/** 该行是否已有任意输入（用于判定"空行可忽略" vs "填了就必须完整"）。 */
export function hasMeasurementInput(row: MeasurementDraftRow): boolean {
  return Boolean(
    String(row.characteristicCode ?? '').trim() ||
      String(row.measuredValue ?? '').trim() ||
      String(row.uomCode ?? '').trim() ||
      String(row.lowerSpecLimit ?? '').trim() ||
      String(row.upperSpecLimit ?? '').trim(),
  )
}

/** 单行是否有效：特性 + 数值（必填）+ 单位必填；上下限可选，两者都填时下限不得大于上限。 */
export function isMeasurementRowValid(row: MeasurementDraftRow): boolean {
  const measuredValue = parseRequiredNumber(row.measuredValue)
  const lowerSpecLimit = parseOptionalNumber(row.lowerSpecLimit)
  const upperSpecLimit = parseOptionalNumber(row.upperSpecLimit)
  return (
    Boolean(String(row.characteristicCode ?? '').trim()) &&
    measuredValue.valid &&
    Boolean(String(row.uomCode ?? '').trim()) &&
    lowerSpecLimit.valid &&
    upperSpecLimit.valid &&
    (lowerSpecLimit.value === null ||
      upperSpecLimit.value === null ||
      lowerSpecLimit.value <= upperSpecLimit.value)
  )
}

/** 整组是否可提交：每一行要么为空（忽略），要么完整有效。 */
export function measurementRowsValid(rows: readonly MeasurementDraftRow[]): boolean {
  return rows.every((row) => !hasMeasurementInput(row) || isMeasurementRowValid(row))
}

/**
 * 超差判定（用于录入时的即时红色警示）：需有合法测量值，且越过任一已填的规格限。
 * 仅当测量值可解析时判定——半填的行不误报为超差。
 */
export function measurementOutOfTolerance(row: MeasurementDraftRow): boolean {
  const measured = parseRequiredNumber(row.measuredValue)
  if (!measured.valid || measured.value === null) return false
  const lower = parseOptionalNumber(row.lowerSpecLimit)
  const upper = parseOptionalNumber(row.upperSpecLimit)
  if (lower.valid && lower.value !== null && measured.value < lower.value) return true
  if (upper.valid && upper.value !== null && measured.value > upper.value) return true
  return false
}

/** 只保留有输入的行并归一为提交结构（调用方须先经 `measurementRowsValid` 门禁）。 */
export function toMeasurementPayload(
  rows: readonly MeasurementDraftRow[],
): MeasurementPayloadLine[] {
  return rows.filter(hasMeasurementInput).map((row) => ({
    characteristicCode: String(row.characteristicCode).trim(),
    measuredValue: parseRequiredNumber(row.measuredValue).value as number,
    uomCode: String(row.uomCode).trim(),
    lowerSpecLimit: parseOptionalNumber(row.lowerSpecLimit).value,
    upperSpecLimit: parseOptionalNumber(row.upperSpecLimit).value,
  }))
}
