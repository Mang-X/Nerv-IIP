/**
 * 检验任务逐特性录结果 —— PDA（`business-pda` 检验任务执行页）与 Console（待检工作台，
 * console C3-1 / #801）共用的**同一套**特性结果编辑口径与校验，避免两端漂移（MAN-457 / #811）。
 *
 * 与设备点检的 measurements（`./measurements`，映射 Maintenance 点检端点）不同：
 * 检验任务提交到 Quality `CreateInspectionRecordFromTask`，其结果行结构是
 * `InspectionCharacteristicResult`（characteristicCode / observedValue / unitCode /
 * result / defectReason / defectQuantity / measuredValue，**不带**规格上下限——公差判定
 * 只作为录入端的即时提示，权威 pass/fail 由后端按检验计划规格计算）。
 *
 * 两类特性：
 *  - **计量特性（measured）**：录测量值 + 单位 + 可选上下限；越限即时红警示，派生 result；
 *  - **计数特性（count）**：直接判合格/不合格；不合格须选原因码，可填不良数。
 */

import { parseOptionalNumber, parseRequiredNumber } from './measurements'

export type CharacteristicResultKind = 'measured' | 'count'

/** 检验结果 code（对齐 Quality `InspectionCharacteristicResult.Result`）。 */
export type CharacteristicResult = 'pass' | 'fail'

/** 表单编辑中的特性结果行（数值字段以字符串/数字承载，便于 <input>/NumberKeyboard 双向绑定）。 */
export interface QualityCharacteristicDraftRow {
  kind: CharacteristicResultKind
  characteristicCode: string
  // 计量特性字段
  measuredValue: string | number
  uomCode: string
  lowerSpecLimit: string | number
  upperSpecLimit: string | number
  // 计数特性字段
  countResult: CharacteristicResult | ''
  defectReason: string
  defectQuantity: string | number
}

/** 提交到检验任务 facade 的单条特性结果（对齐 api-client `BusinessConsoleInspectionCharacteristicResult`）。 */
export interface QualityCharacteristicResultLine {
  characteristicCode: string
  observedValue: string
  unitCode: string | null
  result: CharacteristicResult
  defectReason: string | null
  defectQuantity: number | null
  measuredValue: number | null
}

/** 空白行工厂：新增一行时按类别给默认值。 */
export function createQualityCharacteristicDraft(
  kind: CharacteristicResultKind,
): QualityCharacteristicDraftRow {
  return {
    kind,
    characteristicCode: '',
    measuredValue: '',
    uomCode: '',
    lowerSpecLimit: '',
    upperSpecLimit: '',
    countResult: '',
    defectReason: '',
    defectQuantity: '',
  }
}

/**
 * 计量特性超差判定（录入时的即时红色警示）：需有合法测量值，且越过任一已填的规格限。
 * 仅当测量值可解析时判定——半填的行不误报为超差；非计量行恒为 false。
 */
export function characteristicRowOutOfTolerance(row: QualityCharacteristicDraftRow): boolean {
  if (row.kind !== 'measured') return false
  const measured = parseRequiredNumber(row.measuredValue)
  if (!measured.valid || measured.value === null) return false
  const lower = parseOptionalNumber(row.lowerSpecLimit)
  const upper = parseOptionalNumber(row.upperSpecLimit)
  if (lower.valid && lower.value !== null && measured.value < lower.value) return true
  if (upper.valid && upper.value !== null && measured.value > upper.value) return true
  return false
}

/**
 * 该行派生的 pass/fail（用于整单结论与提交 result）：
 *  - 计量：有合法测量值时，超差→fail、否则→pass；测量值未填/非法→null（未判定）；
 *  - 计数：直接取所选合格/不合格；未选→null。
 */
export function characteristicRowResult(
  row: QualityCharacteristicDraftRow,
): CharacteristicResult | null {
  if (row.kind === 'measured') {
    const measured = parseRequiredNumber(row.measuredValue)
    if (!measured.valid || measured.value === null) return null
    return characteristicRowOutOfTolerance(row) ? 'fail' : 'pass'
  }
  return row.countResult === '' ? null : row.countResult
}

/**
 * 单行是否有效（可提交）：特性码必填；
 *  - 计量：测量值必填且为有限数、单位必填；上下限可选，两者都填时下限不得大于上限；
 *  - 计数：须选合格/不合格；判不合格时原因码必填、不良数（如填）须为非负有限数。
 */
export function isQualityCharacteristicRowValid(row: QualityCharacteristicDraftRow): boolean {
  if (!String(row.characteristicCode ?? '').trim()) return false

  if (row.kind === 'measured') {
    const measured = parseRequiredNumber(row.measuredValue)
    const lower = parseOptionalNumber(row.lowerSpecLimit)
    const upper = parseOptionalNumber(row.upperSpecLimit)
    return (
      measured.valid &&
      Boolean(String(row.uomCode ?? '').trim()) &&
      lower.valid &&
      upper.valid &&
      (lower.value === null || upper.value === null || lower.value <= upper.value)
    )
  }

  // 计数特性
  if (row.countResult !== 'pass' && row.countResult !== 'fail') return false
  if (row.countResult === 'fail' && !String(row.defectReason ?? '').trim()) return false
  const defectQuantity = parseOptionalNumber(row.defectQuantity)
  return defectQuantity.valid && (defectQuantity.value === null || defectQuantity.value >= 0)
}

/** 整组是否可提交：至少一行，且每一行完整有效（每行都是一条显式特性结果，不存在"空行忽略"）。 */
export function qualityCharacteristicRowsValid(
  rows: readonly QualityCharacteristicDraftRow[],
): boolean {
  return rows.length > 0 && rows.every(isQualityCharacteristicRowValid)
}

/** 整单结论：任一行判不合格→fail，否则→pass（调用方须先经 `qualityCharacteristicRowsValid` 门禁）。 */
export function qualityInspectionOverallVerdict(
  rows: readonly QualityCharacteristicDraftRow[],
): CharacteristicResult {
  return rows.some((row) => characteristicRowResult(row) === 'fail') ? 'fail' : 'pass'
}

/** 归一为提交结构（调用方须先经 `qualityCharacteristicRowsValid` 门禁）。 */
export function toQualityCharacteristicResultLines(
  rows: readonly QualityCharacteristicDraftRow[],
): QualityCharacteristicResultLine[] {
  return rows.map((row) => {
    const characteristicCode = String(row.characteristicCode).trim()
    const result = characteristicRowResult(row) ?? 'pass'
    if (row.kind === 'measured') {
      const measured = parseRequiredNumber(row.measuredValue).value
      const uom = String(row.uomCode).trim()
      return {
        characteristicCode,
        observedValue: measured === null ? '' : String(measured),
        unitCode: uom === '' ? null : uom,
        result,
        defectReason: null,
        defectQuantity: null,
        measuredValue: measured,
      }
    }
    const defectReason = String(row.defectReason ?? '').trim()
    return {
      characteristicCode,
      observedValue: result === 'pass' ? '合格' : '不合格',
      unitCode: null,
      result,
      defectReason: result === 'fail' && defectReason !== '' ? defectReason : null,
      defectQuantity: result === 'fail' ? parseOptionalNumber(row.defectQuantity).value : null,
      measuredValue: null,
    }
  })
}
