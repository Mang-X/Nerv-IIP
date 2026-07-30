/**
 * 排程窗口（horizon）的**单一事实来源**。
 *
 * 以前排产工作台把窗口写死成「现在起 7 天」（`pages/scheduling.vue` 里一句
 * `setDate(getDate() + 7)`）：插单、加急、单笔改期这些真实调度场景要么排不进窗口、
 * 要么被迫连带整批重排。窗口现在由用户指定，解析/校验逻辑收在这里，让弹窗、
 * 工作台共用同一份口径，也让它可以脱离组件被单测覆盖。
 *
 * 边界一律在这里下结论（起止倒置、跨度过长、格式非法），组件只负责显示 message。
 */

/** 快捷天数（覆盖插单当天 → 月度重排）。 */
export const SCHEDULING_HORIZON_PRESET_DAYS = [1, 3, 7, 14, 30] as const

/** 沿用改造前的默认窗口，保证既有批量排产行为不变。 */
export const DEFAULT_SCHEDULING_HORIZON_DAYS = 7

/**
 * 窗口跨度上限。后端不限制跨度，但窗口越长求解规模越大；这里给一个人类可解释的
 * 上限，避免误填 `2027-01-01` 之类的年跨度把排程引擎拖死。
 */
export const MAX_SCHEDULING_HORIZON_DAYS = 180

export type SchedulingHorizonMode = 'preset' | 'custom'

export interface SchedulingHorizonInput {
  mode: SchedulingHorizonMode
  /** preset 模式：自「现在」起的天数。 */
  days: number
  /** custom 模式：本地时间起点（`<input type="datetime-local">` 的值）。 */
  startLocal: string
  /** custom 模式：本地时间终点。 */
  endLocal: string
}

export type ResolvedSchedulingHorizon =
  | { ok: true; horizonStartUtc: string; horizonEndUtc: string }
  | { ok: false; message: string }

function pad(value: number) {
  return String(value).padStart(2, '0')
}

/** Date → `<input type="datetime-local">` 的本地值（不带时区后缀）。 */
export function toLocalInputValue(date: Date): string {
  if (Number.isNaN(date.getTime())) return ''
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  )
}

/** `<input type="datetime-local">` 的本地值 → Date；空/非法一律 undefined（不猜）。 */
export function fromLocalInputValue(value: string | null | undefined): Date | undefined {
  const trimmed = value?.trim()
  if (!trimmed) return undefined
  const date = new Date(trimmed)
  return Number.isNaN(date.getTime()) ? undefined : date
}

/** 起点对齐到整点：与改造前 `setMinutes(0, 0, 0)` 的行为一致。 */
export function floorToHour(date: Date): Date {
  const floored = new Date(date)
  floored.setMinutes(0, 0, 0)
  return floored
}

export function addDays(date: Date, days: number): Date {
  const next = new Date(date)
  next.setDate(next.getDate() + days)
  return next
}

/** 建一份默认窗口输入：preset 模式 + 默认天数，custom 字段预填等价区间。 */
export function createSchedulingHorizonInput(
  now: Date = new Date(),
  days: number = DEFAULT_SCHEDULING_HORIZON_DAYS,
): SchedulingHorizonInput {
  const start = floorToHour(now)
  return {
    mode: 'preset',
    days,
    startLocal: toLocalInputValue(start),
    endLocal: toLocalInputValue(addDays(start, days)),
  }
}

/**
 * 把窗口输入解析为后端要的 `horizonStartUtc` / `horizonEndUtc`。
 *
 * 失败不抛异常：调用方要用 message 直接渲染在表单上（提交按钮据此禁用）。
 */
export function resolveSchedulingHorizon(
  input: SchedulingHorizonInput,
  now: Date = new Date(),
): ResolvedSchedulingHorizon {
  if (input.mode === 'preset') {
    const days = Number(input.days)
    if (!Number.isFinite(days) || days <= 0) {
      return { ok: false, message: '请选择排程窗口天数。' }
    }
    if (days > MAX_SCHEDULING_HORIZON_DAYS) {
      return { ok: false, message: `排程窗口最长 ${MAX_SCHEDULING_HORIZON_DAYS} 天。` }
    }
    const start = floorToHour(now)
    return {
      ok: true,
      horizonStartUtc: start.toISOString(),
      horizonEndUtc: addDays(start, days).toISOString(),
    }
  }

  const start = fromLocalInputValue(input.startLocal)
  const end = fromLocalInputValue(input.endLocal)
  if (!start || !end) {
    return { ok: false, message: '请填写完整的排程窗口起止时间。' }
  }
  if (end.getTime() <= start.getTime()) {
    return { ok: false, message: '排程窗口结束时间必须晚于开始时间。' }
  }
  const spanDays = (end.getTime() - start.getTime()) / 86_400_000
  if (spanDays > MAX_SCHEDULING_HORIZON_DAYS) {
    return { ok: false, message: `排程窗口最长 ${MAX_SCHEDULING_HORIZON_DAYS} 天。` }
  }
  return { ok: true, horizonStartUtc: start.toISOString(), horizonEndUtc: end.toISOString() }
}

/** 把已解析的窗口写成一句人读文案（提交前让用户确认排到哪一天）。 */
export function describeSchedulingHorizon(resolved: ResolvedSchedulingHorizon): string {
  if (!resolved.ok) return resolved.message
  return `${new Date(resolved.horizonStartUtc).toLocaleString()} 至 ${new Date(
    resolved.horizonEndUtc,
  ).toLocaleString()}`
}
