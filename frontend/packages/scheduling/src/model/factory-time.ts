import type { SchedulingTimeContextItem } from '@nerv-iip/api-client'
import type { ScheduleModel } from './types'

/** 工厂墙上时间只在输入边界转换；草稿始终保存带偏移的实际时刻。 */
export function factoryTimeInput(instant: string, timezone: string): string {
  if (!timezone || !Number.isFinite(Date.parse(instant))) return ''
  try {
    const parts = new Intl.DateTimeFormat('sv-SE', {
      timeZone: timezone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
    }).formatToParts(new Date(instant))
    const get = (type: Intl.DateTimeFormatPartTypes) =>
      parts.find((part) => part.type === type)?.value
    return `${get('year')}-${get('month')}-${get('day')}T${get('hour')}:${get('minute')}:${get('second')}`
  } catch (error) {
    if (error instanceof RangeError) return ''
    throw error
  }
}

export function factoryTimeInstant(
  wall: string,
  timezone: string,
  original: string,
): string | undefined {
  const normalized = /^\d{4}-\d\d-\d\dT\d\d:\d\d$/.test(wall) ? `${wall}:00` : wall
  if (!/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d$/.test(normalized)) return undefined
  // 同值保留原始偏移/毫秒，也避免 DST 重叠区间的无操作保存被重新解释。
  if (timezone && normalized === factoryTimeInput(original, timezone)) return original
  const nominal = Date.parse(`${normalized}Z`)
  if (!Number.isFinite(nominal)) return undefined
  const candidates = new Set<number>()
  // 前后一天的偏移覆盖 DST 切换两侧；只有唯一可回显的实际时刻才接受。
  for (const delta of [-86400000, 0, 86400000]) {
    const probe = nominal + delta
    const displayed = factoryTimeInput(new Date(probe).toISOString(), timezone)
    if (!displayed) return undefined
    const offset = Date.parse(`${displayed}Z`) - probe
    const candidate = nominal - offset
    if (factoryTimeInput(new Date(candidate).toISOString(), timezone) === normalized)
      candidates.add(candidate)
  }
  const originalMillis = Date.parse(original)
  const milliseconds = Number.isFinite(originalMillis) ? ((originalMillis % 1000) + 1000) % 1000 : 0
  return candidates.size === 1
    ? new Date([...candidates][0] + milliseconds).toISOString()
    : undefined
}

/** 未传时区的通用调用者保留原行为；APS 显式传空值时不猜时区。 */
export function scheduleTime(
  instant: string,
  timezone?: string,
  options?: Intl.DateTimeFormatOptions,
) {
  if (timezone === '') return '工厂时区未解析'
  const date = new Date(instant)
  if (!Number.isFinite(date.getTime())) return '—'
  return date.toLocaleString('zh-CN', { hourCycle: 'h23', ...options, timeZone: timezone })
}

export function withFactoryTimeContext(
  model: ScheduleModel,
  context?: SchedulingTimeContextItem[],
): ScheduleModel {
  const byWorkCenter = new Map(context?.map((item) => [item.workCenterCode, item]))
  const tasks = model.tasks.map((task) => {
    const item = byWorkCenter.get(task.workCenterId)
    const timezone =
      item?.timezone && !item.unresolvedReason && factoryTimeInput(task.startUtc, item.timezone)
        ? item.timezone
        : ''
    return {
      ...task,
      timezone,
      factoryTimeLabel: timezone
        ? `${item!.plantName ?? item!.plantCode} · ${timezone}`
        : item?.unresolvedReason || '工厂时区尚未读取，保留当前草案',
    }
  })
  // 工单汇总以所属工序的时区表达；跨时区汇总不伪造单一工厂时间。
  for (const task of tasks.filter((task) => task.type === 'order')) {
    const children = tasks.filter(
      (child) => child.type === 'operation' && child.orderId === task.orderId && !child.blockKind,
    )
    const zones = new Set(children.map((child) => child.timezone))
    if (zones.size === 1 && children.length) {
      task.timezone = children[0].timezone
      task.factoryTimeLabel = children[0].factoryTimeLabel
    } else if (zones.size > 1) task.factoryTimeLabel = '跨工厂时区，请查看各工序时间'
  }
  return {
    ...model,
    tasks,
    timezones: [
      ...new Set(
        tasks
          .filter((task) => task.type === 'operation' && !task.blockKind)
          .map((task) => task.timezone)
          .filter((zone): zone is string => !!zone),
      ),
    ].sort(),
  }
}
