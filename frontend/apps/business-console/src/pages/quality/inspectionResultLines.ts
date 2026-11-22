import type { BusinessConsoleInspectionCharacteristicResult } from '@nerv-iip/api-client'

// `<input type="number">` 的 v-model 回写的是 **number**，不是 string——测量值/缺陷数量这两个
// 数字输入位因此会把行上的字段从 '' 变成 1210 这样的数值。旧代码把它们一律当 string 处理，
// `toMeasuredNumber` 上来就 `.trim()`，一录入测量值就抛 TypeError，校验计算整条挂掉：
// 按钮永远算不出「可提交」，页面也不报错——这正是 #79「填满了还是灰的、还没有红字」的第二个真因。
// 这里把数字位的类型说实话，取值一律经 `toMeasuredNumber` / `toOptionalNumber` 归一。
export type NumericFieldValue = string | number

export function emptyLine() {
  return {
    characteristicCode: '',
    result: 'passed',
    observedValue: '',
    // 计量型（variable）特性提交契约要求数值 measuredValue，缺失后端直接拒（#1326）；
    // characteristicType 从方案特性清单带出，驱动「数值输入 vs 文本输入」的切换。
    measuredValue: '' as NumericFieldValue,
    characteristicType: '',
    unitCode: '',
    defectReason: '',
    defectQuantity: '' as NumericFieldValue,
    characteristicName: '',
    specification: '',
  }
}

export function formatSpecification(characteristic: {
  nominalValue?: number | null
  lowerSpecLimit?: number | null
  upperSpecLimit?: number | null
  unitCode?: string | null
}) {
  const unit = characteristic.unitCode ? ` ${characteristic.unitCode}` : ''
  if (characteristic.lowerSpecLimit != null || characteristic.upperSpecLimit != null) {
    return `${characteristic.lowerSpecLimit ?? '—'}–${characteristic.upperSpecLimit ?? '—'}${unit}`
  }
  return characteristic.nominalValue == null ? '' : `目标 ${characteristic.nominalValue}${unit}`
}

// 计量型（variable）特性：录数值测量值、提交 measuredValue；计数型（attribute）录文本实测值。
export function isVariableLine(line: { characteristicType: string }) {
  return line.characteristicType.trim().toLowerCase() === 'variable'
}
// 测量值必须显式录入：空串不许被 Number('') === 0 吞成合法的 0（区别于 toOptionalNumber）。
// 数字输入位会直接回写 number，所以这里先认类型再谈 trim。
export function toMeasuredNumber(value: NumericFieldValue) {
  if (typeof value === 'number') return Number.isFinite(value) ? value : undefined
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}

export function toCharacteristicResults(
  lines: readonly ReturnType<typeof emptyLine>[],
): BusinessConsoleInspectionCharacteristicResult[] {
  return lines.map((line) => {
    // 计量型特性必须带数值 measuredValue（后端按方案规格判定），observedValue 同步为其字符串形式；
    // 计数型保持 observedValue 文本、不带 measuredValue（#1326）。
    const measuredValue = isVariableLine(line) ? toMeasuredNumber(line.measuredValue) : undefined
    return {
      characteristicCode: line.characteristicCode.trim(),
      result: line.result.trim(),
      observedValue:
        measuredValue !== undefined ? String(measuredValue) : line.observedValue.trim(),
      measuredValue,
      unitCode: optionalText(line.unitCode),
      defectReason: optionalText(line.defectReason),
      defectQuantity: toOptionalNumber(line.defectQuantity),
    }
  })
}
export function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
export function splitCsv(value: string) {
  const values = value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)
  return values.length ? values : undefined
}
export function toOptionalNumber(value: NumericFieldValue) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
export function hasRequiredDefectContext(line: {
  result: string
  defectReason: string
  defectQuantity: NumericFieldValue
}) {
  if (line.result === 'passed') return true
  if (!isNonEmpty(line.defectReason)) return false
  return line.result !== 'conditional-release' || (toOptionalNumber(line.defectQuantity) ?? 0) > 0
}

export function isNonEmpty(value: string) {
  return value.trim().length > 0
}
