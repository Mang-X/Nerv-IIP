export function isNonEmpty(value: string) {
  return value.trim().length > 0
}
export function inOptions(options: readonly { value: string }[], value: string) {
  return options.some((option) => option.value === value)
}
// 数字输入框的 v-model 可能回传 number；统一收成可 trim 的字符串。
function asText(value: string | number): string {
  return value == null ? '' : String(value)
}
// 精度可空；填了则必须是 >= 0 的整数。
export function isPrecisionValid(value: string | number) {
  if (asText(value).trim() === '') return true
  const n = Number(value)
  return Number.isInteger(n) && n >= 0
}
export function precisionNumber(value: string | number): number | undefined {
  return asText(value).trim() === '' ? undefined : Number(value)
}
// 换算系数必填，必须是 > 0 的有限数。
export function isFactorValid(value: string | number) {
  if (asText(value).trim() === '') return false
  const n = Number(value)
  return Number.isFinite(n) && n > 0
}
// 偏移量可空；填了则必须是有限数（可负）。
export function isOffsetValid(value: string | number) {
  if (asText(value).trim() === '') return true
  return Number.isFinite(Number(value))
}
export function offsetNumber(value: string | number): number | undefined {
  return asText(value).trim() === '' ? undefined : Number(value)
}
