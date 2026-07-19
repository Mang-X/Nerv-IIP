/**
 * 批次效期三色口径（公共常量，纯 TS，UI 无关）。
 *
 * 与 console / PDA 同口径，避免两端漂移：
 *   剩余 > 90 天  → fresh    绿（正常）
 *   剩余 30–90 天 → near     黄（临期）
 *   剩余 0–<30 天 → critical 红（临近过期）
 *   已过期(<0)    → expired  红（已过期）
 *
 * 后端 `inventory/expiry-alerts`(#702) 只给单一 `isNearExpiry` 布尔阈值，三级
 * 展示口径属前端呈现层——集中在此，供 console 批次效期页（MAN-449）直接复用。
 * 各 app 只需把语义 tone 映射到自己的组件变体（success/warning/danger）。
 */

/** 剩余天数 > 此值视为正常（绿）。 */
export const EXPIRY_NEAR_THRESHOLD_DAYS = 90
/** 剩余天数 < 此值视为临近过期（红）；[30,90] 为临期（黄）。 */
export const EXPIRY_CRITICAL_THRESHOLD_DAYS = 30

export type ExpiryTone = 'fresh' | 'near' | 'critical' | 'expired'

export interface ExpiryAlertLike {
  expiryDate?: string | Date | null
  daysUntilExpiry?: number | null
  isExpired?: boolean | null
  isNearExpiry?: boolean | null
}

const MS_PER_DAY = 24 * 60 * 60 * 1000

/** 把 `YYYY-MM-DD` 或含时间的 ISO 串解析成「当天 UTC 零点」的毫秒；非法返回 null。 */
function toUtcDayStart(value: string | Date | null | undefined): number | null {
  if (value == null || value === '') return null
  if (value instanceof Date) {
    const t = value.getTime()
    return Number.isNaN(t)
      ? null
      : Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate())
  }
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  if (!m) return null
  const year = Number(m[1])
  const month = Number(m[2])
  const day = Number(m[3])
  if (month < 1 || month > 12 || day < 1 || day > 31) return null
  const timestamp = Date.UTC(year, month - 1, day)
  const parsed = new Date(timestamp)
  if (
    parsed.getUTCFullYear() !== year ||
    parsed.getUTCMonth() !== month - 1 ||
    parsed.getUTCDate() !== day
  ) {
    return null
  }
  return timestamp
}

/**
 * 距效期的整天数（正=未过期，0=当天，负=已过期）。以「日」为粒度按 UTC 零点比较，
 * 规避本地时区把同一天算差 1 天。无法解析返回 null。
 */
export function expiryDaysUntil(
  expiryDate: string | Date | null | undefined,
  asOf: string | Date = new Date(),
): number | null {
  const exp = toUtcDayStart(expiryDate)
  const base = toUtcDayStart(asOf)
  if (exp == null || base == null) return null
  return Math.round((exp - base) / MS_PER_DAY)
}

/** 剩余天数 → 三色语义 tone。null（无天数）时按调用方需要处理。 */
export function expiryTone(daysUntil: number): ExpiryTone {
  if (daysUntil < 0) return 'expired'
  if (daysUntil < EXPIRY_CRITICAL_THRESHOLD_DAYS) return 'critical'
  if (daysUntil <= EXPIRY_NEAR_THRESHOLD_DAYS) return 'near'
  return 'fresh'
}

/** 效期日期 → 三色语义 tone；无法解析返回 null（未知效期，调用方决定是否展示）。 */
export function expiryToneFromDate(
  expiryDate: string | Date | null | undefined,
  asOf: string | Date = new Date(),
): ExpiryTone | null {
  const days = expiryDaysUntil(expiryDate, asOf)
  return days == null ? null : expiryTone(days)
}

/**
 * 呈现层使用的效期 tone：优先采用后端已计算的事实，缺失时才按共享日期口径补齐。
 * 该结果只用于展示，不代表库存动作授权或阻断原因。
 */
export function expiryToneFromAlert(
  alert: ExpiryAlertLike,
  asOf: string | Date = new Date(),
): ExpiryTone | null {
  if (alert.isExpired === true) return 'expired'
  const calculatedTone =
    typeof alert.daysUntilExpiry === 'number'
      ? expiryTone(alert.daysUntilExpiry)
      : expiryToneFromDate(alert.expiryDate, asOf)
  // 服务端 near 标记与集中三色口径冲突时取更严重等级：既不把 <30 天降成黄色，
  // 也不把服务端已判定的近效期覆盖成绿色。
  if (alert.isNearExpiry === true && (calculatedTone == null || calculatedTone === 'fresh')) {
    return 'near'
  }
  return calculatedTone
}

/** 是否为「临期或更差」（黄/红）——收货时是否需要黄色提示的判据。 */
export function isNearOrExpired(tone: ExpiryTone | null): boolean {
  return tone === 'near' || tone === 'critical' || tone === 'expired'
}

export const EXPIRY_TONE_LABEL: Record<ExpiryTone, string> = {
  fresh: '正常',
  near: '临期',
  critical: '临近过期',
  expired: '已过期',
}

export function expiryToneLabel(tone: ExpiryTone | null | undefined): string {
  return tone ? EXPIRY_TONE_LABEL[tone] : '效期未知'
}
