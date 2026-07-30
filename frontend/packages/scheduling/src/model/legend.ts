// 图例事实源:图例只讲图上真实出现过的语义。
// 每一项都由当前 ScheduleModel + 视图推导,而不是写死一张"全量色板"——
// 后端没带日历就不谈班次,方案里没有换型窗口就不列换型图例。

import type { ScheduleModel } from './types'

export type BlockKind = 'maintenance' | 'downtime' | 'lineChange' | 'changeover'

export interface SchedulingLegendSemantics {
  /** 甘特语义(工单甘特):计划基线 / 依赖箭头 / 里程碑。 */
  gantt: { baseline: boolean; link: boolean; milestone: boolean }
  /** 卡片(资源排产板):优先级 / 插单 / 齐套 / 换型耗时 / 瓶颈。 */
  card: {
    priority: boolean
    rush: boolean
    kitting: boolean
    changeover: boolean
    bottleneck: boolean
  }
  /** 状态:冲突 / 锁定。 */
  status: { conflict: boolean; locked: boolean }
  /** 阻塞:方案里真实出现过的资源时间块类型(按固定顺序)。 */
  blocks: BlockKind[]
  /**
   * 日历:非工作时段底纹恒在(有日历按日历、无日历按通用作息);
   * 班次边界只有后端带出日历时才画;「现在」线只在计划期覆盖当下时出现。
   */
  calendar: { nonWorking: boolean; shift: boolean; now: boolean }
}

const BLOCK_ORDER: BlockKind[] = ['maintenance', 'downtime', 'lineChange', 'changeover']

const EMPTY: SchedulingLegendSemantics = {
  gantt: { baseline: false, link: false, milestone: false },
  card: { priority: false, rush: false, kitting: false, changeover: false, bottleneck: false },
  status: { conflict: false, locked: false },
  blocks: [],
  calendar: { nonWorking: false, shift: false, now: false },
}

export function deriveLegendSemantics(
  model?: ScheduleModel,
  now: number = Date.now(),
): SchedulingLegendSemantics {
  if (!model) return EMPTY
  const tasks = model.tasks ?? []
  const operations = tasks.filter((t) => t.type === 'operation' && !t.blockKind)
  const blockKinds = new Set(tasks.map((t) => t.blockKind).filter(Boolean) as BlockKind[])

  const horizonStart = Date.parse(model.horizon?.startUtc ?? '')
  const horizonEnd = Date.parse(model.horizon?.endUtc ?? '')
  const shiftCodes = new Set(
    (model.calendars ?? []).flatMap((c) => c.shiftWindows.map((w) => w.shiftCode)),
  )

  return {
    gantt: {
      baseline: operations.some((t) => t.plannedStartUtc || t.plannedEndUtc),
      link: (model.links ?? []).length > 0,
      milestone: tasks.some((t) => t.isMilestone || t.milestoneLabel),
    },
    card: {
      priority: operations.some((t) => !!t.priority),
      rush: operations.some((t) => t.isRush),
      kitting: operations.some((t) => typeof t.kitting === 'number'),
      changeover: operations.some((t) => typeof t.changeoverMin === 'number'),
      bottleneck:
        (model.resources ?? []).some((r) => (r.utilization ?? 0) > 1) ||
        (model.loads ?? []).some((l) => l.utilization > 1),
    },
    status: {
      conflict: tasks.some((t) => t.hasConflict),
      locked: operations.some((t) => t.locked),
    },
    blocks: BLOCK_ORDER.filter((kind) => blockKinds.has(kind)),
    calendar: {
      // 时间线底纹恒在:有日历按日历判定,无日历也会画周末/夜间。
      nonWorking: true,
      shift: shiftCodes.size > 0,
      now:
        Number.isFinite(horizonStart) &&
        Number.isFinite(horizonEnd) &&
        now >= horizonStart &&
        now <= horizonEnd,
    },
  }
}
