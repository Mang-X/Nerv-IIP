const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const TECHNICAL_USER_PATTERN = /^user-emp-/i

/**
 * 只允许人读编码/名称上屏。系统 UUID 与 IAM 技术账号没有业务辨识价值，缺人读值时显中性占位。
 */
export function readFaceText(value?: string | null, fallback = '—') {
  const text = value?.trim()
  if (!text || UUID_PATTERN.test(text) || TECHNICAL_USER_PATTERN.test(text)) return fallback
  return text
}
