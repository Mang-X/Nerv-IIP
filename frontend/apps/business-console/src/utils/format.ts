/**
 * 业务前端通用格式化工具。
 * 统一时间显示：后端返回的是 ISO/DateTimeOffset（如 2026-06-08T05:01:33.4122550Z），
 * 直接展示又长又含时区后缀；本工具统一成本地「YYYY-MM-DD HH:mm」。
 */

function pad2(value: number): string {
  return String(value).padStart(2, '0')
}

/** 格式化为本地「YYYY-MM-DD HH:mm」；空值返回「无」，非日期串原样返回（如「本次录入」）。 */
export function formatDateTime(value?: string | null): string {
  if (!value) return '无'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())} ${pad2(date.getHours())}:${pad2(date.getMinutes())}`
}

/** 仅日期「YYYY-MM-DD」。 */
export function formatDate(value?: string | null): string {
  if (!value) return '无'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`
}

/** 本地今天「YYYY-MM-DD」，用作表单生效日 / 有效期起的默认值。 */
export function today(): string {
  const date = new Date()
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`
}
