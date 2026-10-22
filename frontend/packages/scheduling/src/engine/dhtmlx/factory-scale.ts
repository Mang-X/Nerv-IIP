import { factoryTimeInput, scheduleTime } from '../../model/factory-time'

/** DHTMLX 自定义时间单位：格线按工厂日历对齐，坐标与拖拽仍是实际时刻。 */
export function factoryUnit(timezone: string, unit: string) {
  const wallDate = (date: Date) => new Date(`${factoryTimeInput(date.toISOString(), timezone)}Z`)
  const instant = (wall: Date, original: Date) => {
    const nominal = wall.getTime()
    const offsetAt = (date: Date) => wallDate(date).getTime() - date.getTime()
    const estimate = new Date(nominal - offsetAt(original))
    return new Date(nominal - offsetAt(estimate))
  }
  return {
    start(date: Date) {
      if (unit === 'hour')
        return new Date(
          date.getTime() -
            wallDate(date).getUTCMinutes() * 60_000 -
            date.getSeconds() * 1000 -
            date.getMilliseconds(),
        )
      const wall = wallDate(date)
      wall.setUTCMilliseconds(0)
      wall.setUTCSeconds(0)
      wall.setUTCMinutes(0)
      if (unit !== 'hour') wall.setUTCHours(0)
      if (unit === 'month' || unit === 'year') wall.setUTCDate(1)
      if (unit === 'year') wall.setUTCMonth(0)
      return instant(wall, date)
    },
    add(date: Date, count: number) {
      if (unit === 'hour') return new Date(date.getTime() + count * 3_600_000)
      const wall = wallDate(date)
      if (unit === 'year') wall.setUTCFullYear(wall.getUTCFullYear() + count)
      else if (unit === 'month') wall.setUTCMonth(wall.getUTCMonth() + count)
      else wall.setUTCDate(wall.getUTCDate() + count)
      return instant(wall, date)
    },
    format(date: Date) {
      const options: Intl.DateTimeFormatOptions =
        unit === 'hour'
          ? { hour: '2-digit', minute: '2-digit' }
          : unit === 'year'
            ? { year: 'numeric' }
            : unit === 'month'
              ? { year: 'numeric', month: 'numeric' }
              : { month: 'numeric', day: 'numeric', weekday: 'short' }
      return `${timezone} · ${scheduleTime(date.toISOString(), timezone, options)}`
    },
  }
}
