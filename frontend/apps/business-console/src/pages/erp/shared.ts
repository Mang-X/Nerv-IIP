import type { ComputedRef } from 'vue'

export function formatAmount(value?: number | null, currency = 'CNY') {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value ?? 0)
}

export function formatDate(value?: string | null) {
  if (!value) return '-'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '-' : d.toLocaleDateString('zh-CN')
}

export function formatDateTime(value?: string | null) {
  if (!value) return '-'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '-' : d.toLocaleString('zh-CN')
}

export function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(value ?? 0)
}

export function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}

export function firstQueryParam(value: unknown) {
  if (Array.isArray(value)) return value[0] ? String(value[0]) : undefined
  return value ? String(value) : undefined
}

/**
 * 选择器（NvEntityPicker / EntityMultiPicker）的校验红框：这些控件把触发按钮包在一层
 * 容器里，`data-invalid` 只有 NvInput 认，所以统一用同一条边框类描红，视觉与输入框一致。
 * 传入 undefined 而不是空串，避免在未出错时留下空 class 属性。
 */
export function pickerInvalidClass(invalid: boolean) {
  return invalid ? '[&>button]:border-destructive' : undefined
}

export function unwrapRef<T>(value: T | ComputedRef<T>): T {
  return typeof value === 'object' && value !== null && 'value' in value ? value.value : value
}
