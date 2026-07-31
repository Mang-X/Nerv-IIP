// 时间刻度的事实源:'auto' 解析成什么刻度、该刻度下单元格多粗,只有这一份答案。
// 引擎(画图)与图例(讲图)都从这里取——否则图例会承诺图面画不出来的东西。

import type { TimeScale } from './types'

export type ResolvedTimeScale = Exclude<TimeScale, 'auto'>

/**
 * 把 `'auto'` 解析成真实刻度:按计划期跨度决定。
 * 引擎 `DhtmlxEngine.resolveScale()` 直接调用本函数,图例也调用本函数,不许各算各的。
 */
export function resolveTimeScale(
  scale: TimeScale | undefined,
  horizon?: { startUtc?: string; endUtc?: string },
): ResolvedTimeScale {
  if (scale && scale !== 'auto') return scale
  if (!horizon?.startUtc || !horizon?.endUtc) return 'day'
  const days = (Date.parse(horizon.endUtc) - Date.parse(horizon.startUtc)) / 86_400_000
  if (!Number.isFinite(days)) return 'day'
  if (days <= 2) return 'hour'
  if (days <= 14) return 'day'
  if (days <= 90) return 'week'
  return 'month'
}

/**
 * 某个班次窗口的起点在当前刻度下**能不能真的画出一条班次边界竖线**。
 *
 * 引擎是逐格判定的:`timeline_cell_class` 拿到每个单元格的起点时间,只有它与某个班次窗口
 * 起点**完全相等**才加 `nerv-shift-start`(见 DhtmlxEngine)。所以能不能看见完全取决于
 * 当前刻度的单元格粒度:
 * - **班次级(hour)**:2 小时一格,只有落在偶数整点的班次起点才是格子起点;
 * - **日级(day)**:一格是本地零点到零点,08:00 / 16:00 起的班次落不到格子起点上 → 0 条线。
 *   即便有零点起班的班次,那条线也与日边界重合,讲不出任何班次信息;
 * - **周 / 月级**:一格 7 天 / 1 个月,班次边界更无从落点。
 *
 * 走查台账 #41:日级视图下图例仍列「班次边界」,而图面一条线也没有——图例承诺了图面
 * 没有的语义。图例调用本函数按实际渲染推导,不再只看「后端有没有带日历」。
 */
export function shiftBoundaryRendersAt(startUtc: string, scale: ResolvedTimeScale): boolean {
  if (scale !== 'hour') return false
  const at = new Date(startUtc)
  if (Number.isNaN(at.getTime())) return false
  // 引擎逐格回调拿到的是**本地时间**的单元格起点,所以这里也按本地时间分量判定。
  return (
    at.getMinutes() === 0 &&
    at.getSeconds() === 0 &&
    at.getMilliseconds() === 0 &&
    at.getHours() % 2 === 0
  )
}
