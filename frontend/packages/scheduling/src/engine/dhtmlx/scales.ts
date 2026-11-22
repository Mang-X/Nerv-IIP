import { factoryTimeInput } from '../../model/factory-time'
import { factoryUnit } from './factory-scale'
import type { ScheduleModel, TimeScale } from '../../model/types'
import { type SchedulingEngineOptions } from '../engine'
import type { DhxGantt } from './dhtmlx-types'

const WEEKDAY = ['周日', '周一', '周二', '周三', '周四', '周五', '周六']
const fmtDayLong = (d: Date) => `${d.getMonth() + 1}月${d.getDate()}日 ${WEEKDAY[d.getDay()]}`
const fmtHour = (d: Date) => `${String(d.getHours()).padStart(2, '0')}:00`
// 班次带(资源排产板):8h 三班制 — 夜班 00-08 / 早班 08-16 / 中班 16-24。
const shiftLabel = (d: Date) => {
  const h = d.getHours()
  return h < 8 ? '夜班 00–08' : h < 16 ? '早班 08–16' : '中班 16–24'
}
const shiftCss = (d: Date) =>
  d.getHours() < 8 || d.getHours() >= 16 ? 'nerv-shift nerv-shift-dim' : 'nerv-shift'
const fmtDayShort = (d: Date) => `${d.getDate()} ${WEEKDAY[d.getDay()].slice(1)}`
const fmtMonth = (d: Date) => `${d.getFullYear()}年${d.getMonth() + 1}月`

const SCALE_CONFIG: Record<Exclude<TimeScale, 'auto'>, Array<Record<string, unknown>>> = {
  hour: [
    { unit: 'day', step: 1, format: fmtDayLong },
    { unit: 'hour', step: 2, format: fmtHour },
  ],
  day: [
    { unit: 'month', step: 1, format: fmtMonth },
    { unit: 'day', step: 1, format: fmtDayShort },
  ],
  week: [
    { unit: 'month', step: 1, format: fmtMonth },
    { unit: 'day', step: 7, format: fmtDayShort },
  ],
  month: [
    { unit: 'year', step: 1, format: (d: Date) => `${d.getFullYear()}年` },
    { unit: 'month', step: 1, format: (d: Date) => `${d.getMonth() + 1}月` },
  ],
}

/** 当前刻度配置。资源排产板在小时刻度下插入「班次带」(日期 / 班次 / 小时)。 */
export function scalesFor(
  scale: Exclude<TimeScale, 'auto'>,
  view: SchedulingEngineOptions['view'],
  model?: ScheduleModel,
  gantt?: DhxGantt,
): Array<Record<string, unknown>> {
  const base = SCALE_CONFIG[scale]
  if (model?.timezones?.length && gantt?.date) {
    const dates = gantt.date as unknown as Record<string, unknown>
    const factoryRows =
      view === 'resource' && scale === 'hour'
        ? [base[0], { unit: 'hour', step: 8, shift: true }, base[1]]
        : base
    const rows = model.timezones.flatMap((timezone, index) =>
      factoryRows.map((row) => {
        const unit = String(row.unit)
        const key = `factory_${index}_${unit}`
        const calendar = factoryUnit(timezone, unit)
        dates[`${key}_start`] = calendar.start
        dates[`add_${key}`] = calendar.add
        return {
          ...row,
          unit: key,
          format:
            'shift' in row
              ? (date: Date) => {
                  const hour = Number(factoryTimeInput(date.toISOString(), timezone).slice(11, 13))
                  return `${timezone} · ${hour < 8 ? '夜班 00–08' : hour < 16 ? '早班 08–16' : '中班 16–24'}`
                }
              : calendar.format,
        }
      }),
    )
    gantt.config.scale_height = rows.length * 25
    gantt.config.min_column_width = 140
    return rows
  }
  if (view === 'resource' && scale === 'hour') {
    return [base[0], { unit: 'hour', step: 8, format: shiftLabel, css: shiftCss }, base[1]]
  }
  return base
}
