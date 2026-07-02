/**
 * 统一的操作反馈（通知）工具。
 *
 * 规则见 `frontend/DESIGN/patterns/feedback-and-notifications.md`：
 * - 操作结果（成功/失败，含网络/服务器错误）一律用 toast，**不**在页面或弹窗里留常驻文字。
 * - 字段级校验才用内联（红框 + 汇总），不走这里。
 */
import { toast } from '@nerv-iip/ui'

/** 从各种 error 形态里取出原始文本。 */
function rawMessage(error: unknown): string {
  if (error instanceof Error) return error.message
  if (typeof error === 'string') return error
  if (error && typeof error === 'object' && 'message' in error) {
    return String((error as { message: unknown }).message ?? '')
  }
  return ''
}

/**
 * 把后端/网络错误转成对用户友好的中文。
 * 绝不把 `downstream-invalid-response` / `502` 这类开发术语甩给用户。
 */
export function friendlyErrorMessage(error: unknown, fallback = '操作失败，请稍后重试。'): string {
  const raw = rawMessage(error)
  if (!raw) return fallback
  if (/downstream-invalid-response|\b502\b|bad ?gateway|\b503\b|service unavailable|\b500\b/i.test(raw)) {
    return '服务暂时不可用，请稍后重试。'
  }
  if (/failed to fetch|networkerror|network error|timeout|timed out|econn/i.test(raw)) {
    return '网络异常，请检查连接后重试。'
  }
  if (/\b401\b|unauthor/i.test(raw)) return '登录已过期，请重新登录。'
  if (/\b403\b|forbidden|permission/i.test(raw)) return '没有权限执行此操作。'
  if (/\b409\b|conflict|already exists|duplicat/i.test(raw)) return '编码或名称已存在，请更换后重试。'
  if (/system-managed|cannot be updated/i.test(raw)) return '该项由系统管理（平台固化），不可修改。'
  // 后端返回的可读中文业务校验信息（短文本）直接透传。
  if (/[一-龥]/.test(raw) && raw.length <= 60) return raw
  return fallback
}

/** 成功反馈。 */
export function notifySuccess(message: string): void {
  toast.success(message)
}

/** 失败反馈：toast.error（友好文案），不在页面留常驻错误条。 */
export function notifyError(error: unknown, fallback?: string): void {
  toast.error(friendlyErrorMessage(error, fallback))
}
