import type { ScheduleModel, ScheduleTask } from '../../model/types'
import { type SchedulingEngineOptions } from '../engine'
import { resolveLaneKpiVisibility, type LaneKpiVisibility, type LaneTask } from './grid-columns'

export class GanttProjection {
  /**
   * 泳道产能指标的上屏白名单。mount 时列定义闭包引用它,setData 时按真实数据重算——
   * 所以初值必须是"全不显示":列先建好、数据还没到,这一瞬间若默认全显示就会闪一排 0%。
   */
  kpiVisibility: LaneKpiVisibility = {
    utilization: false,
    oee: false,
    changeoverCount: false,
    materialRisk: false,
  }
  /** 资源时间块:泳道 id → 时段窗口列表,供 timeline_cell_class 给单元格上底纹(块不作任务条)。 */
  blockCells = new Map<string, { start: number; end: number; kind: string }[]>()
  /** 工序 id → 该工序所属资源/工作中心(工单甘特里按行找它对应的时间块)。 */
  private taskLane = new Map<string, string[]>()
  /**
   * 工作日历:资源/工作中心 id → 工作窗口(排序后)。`__all__` 是全局并集,
   * 用于「日历没覆盖到这条泳道」时的回退。为空表示后端没带日历,引擎退回通用作息假设。
   */
  workingWindows = new Map<string, { start: number; end: number }[]>()
  /** 班次起点(毫秒),用于在单元格左侧画班次边界线。 */
  shiftStarts = new Set<number>()

  /** 一行(泳道或工序行)对应的资源 / 工作中心键;工单甘特按工序自身的资源找块。 */
  lanesOf(view: SchedulingEngineOptions['view'], rowId?: string | number): string[] {
    if (typeof rowId !== 'string' && typeof rowId !== 'number') return []
    const id = String(rowId)
    if (view === 'resource') return [id.replace(/^lane:/, '')]
    return this.taskLane.get(id) ?? []
  }

  isWorkingAt(lanes: string[], ts: number): boolean {
    for (const lane of lanes) {
      const wins = this.workingWindows.get(lane)
      if (wins) return wins.some((w) => ts >= w.start && ts < w.end)
    }
    // 该行不在任何一份日历的适用范围内(或行本身没有资源归属):用全局并集判定,
    // 这样至少休息日/夜间仍然是灰的,而不是整片当成工作时间。
    const all = this.workingWindows.get('__all__')
    return all ? all.some((w) => ts >= w.start && ts < w.end) : true
  }

  /** 从模型的工作日历建索引:每个资源/工作中心一份工作窗口表 + 全局并集 + 班次起点集合。 */
  private buildCalendarIndex(model: ScheduleModel): void {
    this.workingWindows = new Map()
    this.shiftStarts = new Set()
    const calendars = model.calendars ?? []
    if (!calendars.length) return

    const all: { start: number; end: number }[] = []
    for (const calendar of calendars) {
      const windows = calendar.shiftWindows
        .map((w) => ({ start: Date.parse(w.startUtc), end: Date.parse(w.endUtc) }))
        .filter((w) => Number.isFinite(w.start) && Number.isFinite(w.end) && w.end > w.start)
        .sort((a, b) => a.start - b.start)
      if (!windows.length) continue
      for (const w of windows) {
        all.push(w)
        this.shiftStarts.add(w.start)
      }
      for (const key of [...calendar.resourceIds, ...calendar.workCenterIds]) {
        if (!key) continue
        const existing = this.workingWindows.get(key)
        this.workingWindows.set(
          key,
          existing ? [...existing, ...windows].sort((a, b) => a.start - b.start) : windows,
        )
      }
    }
    if (all.length)
      this.workingWindows.set(
        '__all__',
        all.sort((a, b) => a.start - b.start),
      )
  }

  /** 时间块索引:泳道键(资源 + 工作中心都登记)→ 时段窗口,供单元格底纹查表。 */
  private buildBlockIndex(blocks: ScheduleTask[], laneOf: (t: ScheduleTask) => string): void {
    this.blockCells = new Map()
    for (const b of blocks) {
      const start = Date.parse(b.startUtc)
      const end = Date.parse(b.endUtc)
      if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) continue
      const keys = new Set([laneOf(b), b.resourceId ?? '', b.workCenterId ?? ''])
      for (const key of keys) {
        if (!key) continue
        const arr = this.blockCells.get(key) ?? []
        arr.push({ start, end, kind: b.blockKind! })
        this.blockCells.set(key, arr)
      }
    }
  }

  project(
    model: ScheduleModel,
    options: SchedulingEngineOptions,
  ): { data: unknown[]; links: unknown[] } {
    const { view, groupBy } = options
    const toDate = (isoVal: string) => (isoVal ? new Date(isoVal) : undefined)
    const data: unknown[] = []
    this.buildCalendarIndex(model)
    // 工序行 → 资源/工作中心(工单甘特用:某道工序所在设备在此时段维护/停机,那一行就该有底纹)。
    this.taskLane = new Map(
      model.tasks
        .filter((t) => !t.blockKind)
        .map((t) => [
          t.id,
          [t.resourceId, t.workCenterId, t.dimensions?.workCenter?.id].filter(Boolean) as string[],
        ]),
    )

    if (view === 'resource') {
      // 一资源(所选维度)一泳道:分组行用 split task,同组工序铺在它那一行。里程碑不入资源板。
      const dim = groupBy || 'workCenter'
      const ops = model.tasks.filter(
        (t) => t.type === 'operation' && !t.isMilestone && !t.blockKind,
      )
      // 资源时间块不作任务条:改为按泳道+时段给时间线「单元格」上底纹(与非工作日历同一实现),
      // 真正与格子融为一体、恒在卡片之下、绝不覆盖。见 timeline_cell_class + this.blockCells。
      const blocks = model.tasks.filter((t) => !!t.blockKind)
      const resById = new Map(model.resources.map((r) => [r.id, r]))
      const loadByResource = new Map<
        string,
        { assigned: number; available: number; utilization: number }
      >()
      for (const load of model.loads) {
        const total = loadByResource.get(load.resourceId) ?? {
          assigned: 0,
          available: 0,
          utilization: 0,
        }
        total.assigned += load.assignedMinutes
        total.available += load.availableMinutes
        total.utilization = Math.max(total.utilization, load.utilization)
        loadByResource.set(load.resourceId, total)
      }
      const groups = new Map<string, string>()
      const laneOf = (t: ScheduleTask) => t.dimensions?.[dim]?.id ?? t.resourceId ?? '__none__'
      const resourcesByLane = new Map<string, Set<string>>()
      const configuredLanes = model.groupValues?.[dim] ?? []
      for (const lane of configuredLanes) groups.set(lane.id, lane.label)
      // 用资源播种泳道,保证空泳道常驻——拖走最后一个工序时该行不消失。但仅当资源本身就是
      // 该维度的成员时才播种:若工序携带的维度 id(如 WC-*)与资源 id(如设备 DEV-*)分属两个
      // 空间,播种全部资源会生出一批永远为空的设备泳道垫在最上方,把真正有工序的泳道挤出首屏。
      if (dim === 'workCenter') {
        const dimIds = new Set<string>()
        for (const t of ops) {
          const id = t.dimensions?.[dim]?.id
          if (id) dimIds.add(id)
        }
        for (const r of model.resources) {
          if (dimIds.size > 0 && !dimIds.has(r.id)) continue
          if (!groups.has(r.id)) groups.set(r.id, r.text)
        }
      }
      for (const t of ops) {
        const id = laneOf(t)
        // 工序携带的维度标签(如「切割中心」)优先于资源原始名(如「激光切割-01」)。
        groups.set(id, t.dimensions?.[dim]?.label ?? t.resourceId ?? groups.get(id) ?? '未分配')
        if (t.resourceId) {
          const resources = resourcesByLane.get(id) ?? new Set<string>()
          resources.add(t.resourceId)
          resourcesByLane.set(id, resources)
        }
      }
      // 时间块:按当前维度算出所属泳道 + 时段窗口,供 timeline_cell_class 给单元格上底纹;
      // 同时播种泳道(块所在资源即使没工序,泳道也要在)。
      this.buildBlockIndex(blocks, laneOf)
      for (const b of blocks) {
        const id = laneOf(b)
        if (!groups.has(id)) groups.set(id, b.dimensions?.[dim]?.label ?? b.resourceId ?? '未分配')
      }
      // 泳道按资源固定顺序排(改派后不重排整板);非资源维度保持出现顺序(稳定排序)。
      const resOrder = new Map(model.resources.map((r, i) => [r.id, i]))
      const groupOrder = new Map(configuredLanes.map((lane, index) => [lane.id, index]))
      // 每条泳道上有多少工序——排序与「仅有排程」共用，只数一遍。
      const opCountByLane = new Map<string, number>()
      for (const t of ops) {
        const id = laneOf(t)
        opCountByLane.set(id, (opCountByLane.get(id) ?? 0) + 1)
      }
      const laneOrder = options.laneOrder ?? 'busiest'
      const laneEntries =
        laneOrder === 'onlyScheduled'
          ? [...groups.entries()].filter(([id]) => (opCountByLane.get(id) ?? 0) > 0)
          : [...groups.entries()]
      const sortedGroups = laneEntries.sort((a, b) => {
        if (laneOrder === 'name') return a[1].localeCompare(b[1], 'zh-Hans-CN')
        if (laneOrder === 'busiest') {
          // 忙的排上面：按车间/产线分组时名录里的空泳道会挤在首屏，演示第一眼像坏了
          // （第五轮走查实测：首屏两条空产线，滚下去才见到有活的那条）。
          const diff = (opCountByLane.get(b[0]) ?? 0) - (opCountByLane.get(a[0]) ?? 0)
          if (diff !== 0) return diff
          // 同样忙（含都为空）时回落名录顺序，避免每次渲染抖动。
        }
        return (
          (groupOrder.get(a[0]) ?? resOrder.get(a[0]) ?? Number.MAX_SAFE_INTEGER) -
          (groupOrder.get(b[0]) ?? resOrder.get(b[0]) ?? Number.MAX_SAFE_INTEGER)
        )
      })
      // 本批泳道指标先攒起来,循环末尾一次性判定哪些指标真有数据(#1399 M8)。
      const laneKpis: Array<LaneTask['kpi']> = []
      for (const [id, label] of sortedGroups) {
        const res = resById.get(id)
        const resourceIds = resourcesByLane.get(id) ?? new Set([id])
        const load = [...resourceIds].reduce<
          { assigned: number; available: number; utilization: number } | undefined
        >((total, resourceId) => {
          const next = loadByResource.get(resourceId)
          if (!next) return total
          return {
            assigned: (total?.assigned ?? 0) + next.assigned,
            available: (total?.available ?? 0) + next.available,
            utilization: Math.max(total?.utilization ?? 0, next.utilization),
          }
        }, undefined)
        const utilization = load
          ? load.available > 0
            ? load.assigned / load.available
            : load.utilization
          : undefined
        const kpi =
          res || utilization != null
            ? {
                utilization: res?.utilization ?? utilization,
                oee: res?.oee,
                changeoverCount: res?.changeoverCount,
                materialRisk: res?.materialRisk,
              }
            : undefined
        laneKpis.push(kpi)
        data.push({
          id: `lane:${id}`,
          text: label,
          type: 'project',
          render: 'split',
          open: true,
          kpi,
          nerv: {
            type: 'order',
            orderId: id,
            operationId: '',
            operationSequence: 0,
            text: label,
            startUtc: '',
            endUtc: '',
            locked: false,
            hasConflict: false,
          },
        })
      }
      this.kpiVisibility = resolveLaneKpiVisibility(laneKpis)
      for (const t of ops) data.push(this.toGanttTask(t, `lane:${laneOf(t)}`, toDate))
      // 工单内线仍按选中展示；装配跨工单前置常显。
      const taskById = new Map(ops.map((task) => [task.id, task]))
      const links = model.links
        .filter((link) => {
          const source = taskById.get(link.source)
          const target = taskById.get(link.target)
          return source && target && source.orderId !== target.orderId
        })
        .map((link) => ({ id: link.id, source: link.source, target: link.target, type: '0' }))
      return { data, links }
    }

    // 工单甘特里块同样不作任务条,但要能看见:按「这道工序所在设备/工作中心在此时段不可用」
    // 给该工序行的单元格上底纹(见 timeline_cell_class + taskLane)。
    this.buildBlockIndex(
      model.tasks.filter((t) => !!t.blockKind),
      (t) => t.workCenterId ?? t.resourceId ?? '__none__',
    )
    for (const t of model.tasks) {
      if (t.blockKind) continue // 资源时间块不作任务条
      const parent = t.type === 'operation' ? (t.parentId ?? 0) : 0
      data.push(this.toGanttTask(t, parent, toDate))
    }
    const links = model.links.map((l) => ({
      id: l.id,
      source: l.source,
      target: l.target,
      type: '0',
    }))
    return { data, links }
  }

  private toGanttTask(
    t: ScheduleTask,
    parent: string | number,
    toDate: (iso: string) => Date | undefined,
  ) {
    return {
      id: t.id,
      text: t.text || t.operationId || t.orderId,
      start_date: toDate(t.startUtc),
      end_date: toDate(t.endUtc),
      planned_start: t.plannedStartUtc ? toDate(t.plannedStartUtc) : undefined,
      planned_end: t.plannedEndUtc ? toDate(t.plannedEndUtc) : undefined,
      parent,
      type: t.isMilestone ? 'milestone' : t.type === 'order' ? 'project' : 'task',
      progress: t.progress ?? 0,
      readonly: t.type === 'order',
      open: true,
      $resource: t.resourceId,
      nerv: t,
    }
  }
}
