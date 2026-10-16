/** 工厂墙上时间只在表格输入边界转换；草稿始终保存带偏移的实际时刻。 */
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
