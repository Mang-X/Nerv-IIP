import type { ScheduleModel, ScheduleTask, TimeScale } from '../../model/types'
import {
  SCALE_ORDER,
  type EngineCommand,
  type EngineEventName,
  type EngineEvents,
  type SchedulingEngine,
  type SchedulingEngineOptions,
  type ThemeBinding,
  type Unsubscribe,
} from '../engine'
import { createGanttInstanceSync } from './loader'
import { applySkin } from './skin'

// DHTMLX gantt 实例的最小子集(只声明适配器用到的成员)。
interface DhxGantt {
  config: Record<string, unknown>
  templates: Record<string, unknown>
  ext?: { zoom?: { setLevel?: (l: string) => void } }
  plugins?: (plugins: Record<string, boolean>) => void
  attachEvent: (name: string, handler: (...args: unknown[]) => unknown) => string
  detachEvent?: (id: string) => void
  init: (container: HTMLElement) => void
  parse: (data: { data: unknown[]; links: unknown[] }) => void
  clearAll: () => void
  getTask: (id: string | number) => DhxTask | undefined
  isTaskExists?: (id: string | number) => boolean
  selectTask?: (id: string | number) => void
  render: () => void
  setSizes?: () => void
  destructor?: () => void
  showDate?: (date: Date) => void
  addMarker?: (marker: Record<string, unknown>) => string
  deleteMarker?: (id: string) => void
  date?: { add: (d: Date, n: number, unit: string) => Date }
}
interface DhxTask {
  id: string
  text?: string
  start_date?: Date
  end_date?: Date
  parent?: string | number
  $resource?: string
}

const GRID_COLUMNS = (view: 'order' | 'resource') => [
  { name: 'text', label: view === 'resource' ? '资源 / 工序' : '工单 / 工序', tree: true, width: 198, resize: true },
  { name: 'start_date', label: '开始', align: 'center', width: 104, resize: true, template: (t: DhxTask) => gridDate(t) },
  { name: 'duration', label: '工时', align: 'center', width: 60, template: (t: DhxTask) => durationLabel(t) },
]

function gridDate(t: DhxTask): string {
  if (!t.start_date || (t as { type?: string }).type === 'project') return ''
  const d = t.start_date
  return Number.isNaN(d.getTime?.()) ? '' : d.toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false })
}
function durationLabel(t: DhxTask): string {
  if ((t as { type?: string }).type === 'project' || !t.start_date || !t.end_date) return ''
  const h = Math.round((t.end_date.getTime() - t.start_date.getTime()) / 3_600_000)
  return h >= 1 ? `${h}h` : '<1h'
}

const SCALE_CONFIG: Record<Exclude<TimeScale, 'auto'>, Array<Record<string, unknown>>> = {
  hour: [
    { unit: 'day', step: 1, format: '%m-%d' },
    { unit: 'hour', step: 2, format: '%H:00' },
  ],
  day: [
    { unit: 'month', step: 1, format: '%Y年%m月' },
    { unit: 'day', step: 1, format: '%j' },
  ],
  week: [
    { unit: 'month', step: 1, format: '%Y年%m月' },
    { unit: 'week', step: 1, format: '第%W周' },
  ],
  month: [
    { unit: 'year', step: 1, format: '%Y' },
    { unit: 'month', step: 1, format: '%M' },
  ],
}

export interface DhtmlxEngineDeps {
  /** 注入实例工厂(测试用);默认从已预加载的试用版模块同步创建。 */
  createInstance?: () => unknown | null
}

/** DHTMLX Gantt 9.x(试用专业版)适配器:统一 SchedulingEngine ↔ DHTMLX vanilla 核心。 */
export class DhtmlxEngine implements SchedulingEngine {
  private container?: HTMLElement
  private options!: SchedulingEngineOptions
  private gantt?: DhxGantt
  private model?: ScheduleModel
  private scale: TimeScale = 'day'
  private selectedTaskId?: string
  private markerId?: string
  private readonly listeners = new Map<EngineEventName, Set<(p: unknown) => void>>()
  private readonly eventIds: string[] = []
  private readonly createInstance: () => unknown | null

  constructor(deps: DhtmlxEngineDeps = {}) {
    this.createInstance = deps.createInstance ?? createGanttInstanceSync
  }

  mount(container: HTMLElement, options: SchedulingEngineOptions): void {
    this.container = container
    this.options = options
    this.scale = options.scale
    applySkin(container, options.theme)
    // DHTMLX 自身布局/网格样式由应用层(business-console main.ts / 预览入口)导入,
    // 经 vite alias 指向 vendor 或空 stub,避免污染包测试面。
    const inst = this.createInstance() as DhxGantt | null
    if (!inst) return // 不可用:由 useEngine 回落 NativeEngine。
    this.gantt = inst

    this.configure(inst, options)
    this.wireEvents(inst)
    inst.init(container)
    if (this.model) this.setData(this.model)
  }

  setData(model: ScheduleModel): void {
    this.model = model
    const g = this.gantt
    if (!g) return
    // model 已知后再应用自适应刻度(configure 早于 setData,那时 horizon 未知)。
    g.config.scales = SCALE_CONFIG[this.resolveScale()]
    g.clearAll()
    g.parse(this.toGanttData(model))
    this.refreshMarker()
    this.mirrorTaskIds()
  }

  applyCommand(command: EngineCommand): void {
    const g = this.gantt
    switch (command.kind) {
      case 'scaleTo':
        this.scale = command.scale
        this.applyScale(g)
        this.emit('scaleChanged', { scale: command.scale })
        break
      case 'zoomIn':
      case 'zoomOut': {
        const i = SCALE_ORDER.indexOf(this.resolveScale())
        this.scale =
          command.kind === 'zoomIn'
            ? SCALE_ORDER[Math.max(0, i - 1)]
            : SCALE_ORDER[Math.min(SCALE_ORDER.length - 1, i + 1)]
        this.applyScale(g)
        this.emit('scaleChanged', { scale: this.scale })
        break
      }
      case 'selectTask':
      case 'focusConflict':
        this.selectedTaskId = command.taskId
        if (g?.isTaskExists?.(command.taskId)) g.selectTask?.(command.taskId)
        g?.render()
        this.emit('taskSelected', { taskId: command.taskId })
        if (command.kind === 'focusConflict') this.emit('conflictClicked', { taskId: command.taskId })
        break
      case 'setReadOnly':
        this.options.readOnly = command.readOnly
        if (g) {
          g.config.readonly = command.readOnly
          g.config.drag_move = !command.readOnly
          g.config.drag_resize = !command.readOnly
          g.config.drag_links = !command.readOnly
          g.render()
        }
        break
      case 'setTheme':
        this.options.theme = command.theme
        if (this.container) applySkin(this.container, command.theme)
        g?.render()
        break
      case 'scrollToToday':
        if (g?.showDate) g.showDate(this.nowDate())
        break
      case 'fitToScreen':
        g?.setSizes?.()
        g?.render()
        break
    }
  }

  on<E extends EngineEventName>(event: E, cb: (payload: EngineEvents[E]) => void): Unsubscribe {
    const set = this.listeners.get(event) ?? new Set()
    set.add(cb as (p: unknown) => void)
    this.listeners.set(event, set)
    return () => set.delete(cb as (p: unknown) => void)
  }

  getState() {
    return { scale: this.scale, selectedTaskId: this.selectedTaskId }
  }

  destroy(): void {
    const g = this.gantt
    if (g?.detachEvent) for (const id of this.eventIds) g.detachEvent(id)
    this.eventIds.length = 0
    this.listeners.clear()
    g?.destructor?.()
    this.gantt = undefined
    this.markerId = undefined
    if (this.container) {
      this.container.classList.remove('nerv-gantt', 'nerv-gantt-dhx', 'nerv-gantt-dark', 'nerv-dhx-scope')
      this.container.replaceChildren()
    }
  }

  // --- internals ----------------------------------------------------------

  private emit<E extends EngineEventName>(event: E, payload: EngineEvents[E]): void {
    this.listeners.get(event)?.forEach((cb) => cb(payload))
  }

  private applyScale(g?: DhxGantt): void {
    if (!g) return
    g.config.scales = SCALE_CONFIG[this.resolveScale()]
    g.render()
  }

  private resolveScale(): Exclude<TimeScale, 'auto'> {
    if (this.scale !== 'auto') return this.scale
    const h = this.model?.horizon
    if (!h?.startUtc || !h?.endUtc) return 'day'
    const days = (Date.parse(h.endUtc) - Date.parse(h.startUtc)) / 86_400_000
    if (days <= 2) return 'hour'
    if (days <= 14) return 'day'
    if (days <= 90) return 'week'
    return 'month'
  }

  private nowDate(): Date {
    const h = this.model?.horizon
    if (!h?.startUtc || !h?.endUtc) return new Date()
    // 真实当前时间;若不在计划窗口内则取窗口中点作为参考线。
    const now = Date.now()
    const s = Date.parse(h.startUtc)
    const e = Date.parse(h.endUtc)
    return now >= s && now <= e ? new Date(now) : new Date((s + e) / 2)
  }

  private refreshMarker(): void {
    const g = this.gantt
    if (!g?.addMarker) return
    if (this.markerId) g.deleteMarker?.(this.markerId)
    this.markerId = g.addMarker({ start_date: this.nowDate(), css: 'nerv-today-marker', text: '现在' })
  }

  private configure(inst: DhxGantt, options: SchedulingEngineOptions): void {
    const c = inst.config
    c.date_format = '%Y-%m-%d %H:%i'
    c.readonly = options.readOnly
    c.drag_move = !options.readOnly
    c.drag_resize = !options.readOnly
    c.drag_links = !options.readOnly
    c.drag_progress = false
    c.order_branch = !options.readOnly // 网格内拖拽换分支(资源视图=改派)
    c.order_branch_free = !options.readOnly
    c.row_height = 36
    c.bar_height = 20
    c.grid_width = 360
    c.grid_resize = true
    c.show_links = true
    c.highlight_critical_path = options.view === 'order'
    c.columns = GRID_COLUMNS(options.view)
    c.scales = SCALE_CONFIG[this.resolveScale()]
    c.scale_height = 50
    c.min_column_width = 36
    c.tooltip_timeout = 20

    try {
      inst.plugins?.({
        tooltip: true,
        marker: true,
        undo: !options.readOnly,
        critical_path: options.view === 'order',
      })
    } catch {
      /* 精简构建无该插件:忽略,核心仍可用。 */
    }

    inst.templates.task_class = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask }) => {
      const t = task.nerv
      const cls: string[] = []
      if (t?.type === 'order') cls.push('nerv-order')
      if (t?.hasConflict) cls.push('nerv-conflict')
      if (t?.locked) cls.push('nerv-locked')
      if (t?.id === this.selectedTaskId) cls.push('nerv-selected')
      return cls.join(' ')
    }
    inst.templates.grid_row_class = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask }) =>
      task.nerv?.hasConflict ? 'nerv-row-conflict' : ''
    inst.templates.tooltip_text = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask; text?: string }) => {
      const t = task.nerv
      if (!t) return task.text ?? ''
      const lines = [
        `<b>${t.type === 'order' ? '工单' : '工序'}:</b> ${t.text || t.orderId}`,
        t.resourceId ? `<b>资源:</b> ${t.resourceId}` : '',
        `<b>起止:</b> ${fmt(t.startUtc)} → ${fmt(t.endUtc)}`,
        t.locked ? '🔒 已锁定' : '',
      ].filter(Boolean)
      return lines.join('<br/>')
    }
    // 条内不渲染文字(短条会溢出);工序名放到条形右侧,始终可读。
    inst.templates.task_text = () => ''
    inst.templates.rightside_text = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask; text?: string }) =>
      task.nerv?.type === 'operation' ? task.text ?? '' : ''
  }

  private wireEvents(inst: DhxGantt): void {
    this.eventIds.push(
      inst.attachEvent('onTaskClick', (id) => {
        const taskId = String(id)
        this.selectedTaskId = taskId
        this.emit('taskSelected', { taskId })
        if (this.model?.tasks.find((t) => t.id === taskId)?.hasConflict)
          this.emit('conflictClicked', { taskId })
        return true
      }),
    )
    this.eventIds.push(
      inst.attachEvent('onAfterTaskDrag', (id, mode) => this.emitDrag(inst, String(id), String(mode))),
    )
    this.eventIds.push(
      inst.attachEvent('onAfterTaskMove', (id) => this.emitDrag(inst, String(id), 'move')),
    )
    this.eventIds.push(inst.attachEvent('onGanttRender', () => this.mirrorTaskIds()))
  }

  private emitDrag(inst: DhxGantt, taskId: string, mode: string): void {
    const task = inst.getTask(taskId)
    const src = this.model?.tasks.find((t) => t.id === taskId)
    const parent = task?.parent != null ? String(task.parent) : undefined
    const reassignedResource = parent?.startsWith('res:') ? parent.slice(4) : task?.$resource
    const kind = mode === 'resize' ? 'resize' : reassignedResource && reassignedResource !== src?.resourceId ? 'reassign' : 'move'
    this.emit('taskDragEnd', {
      taskId,
      operationId: src?.operationId ?? taskId,
      resourceId: reassignedResource ?? src?.resourceId,
      startUtc: task?.start_date?.toISOString() ?? src?.startUtc ?? '',
      endUtc: task?.end_date?.toISOString() ?? src?.endUtc ?? '',
      kind,
    })
  }

  /** 把 DHTMLX 的 task_id 属性镜像为统一的 data-task-id,供契约/选择器统一定位。 */
  private mirrorTaskIds(): void {
    const root = this.container
    if (!root) return
    root.querySelectorAll('[task_id]').forEach((el) => {
      const id = el.getAttribute('task_id')
      if (id && !el.hasAttribute('data-task-id')) el.setAttribute('data-task-id', id)
    })
  }

  private toGanttData(model: ScheduleModel): { data: unknown[]; links: unknown[] } {
    const toDate = (isoVal: string) => (isoVal ? new Date(isoVal) : undefined)
    const data: unknown[] = []

    if (this.options.view === 'resource') {
      const used = new Set(model.tasks.filter((t) => t.type === 'operation').map((t) => t.resourceId).filter(Boolean) as string[])
      const resourceIds = model.resources.length
        ? model.resources.map((r) => r.id)
        : [...used]
      for (const id of resourceIds) {
        const text = model.resources.find((r) => r.id === id)?.text ?? id
        data.push({ id: `res:${id}`, text, type: 'project', open: true, nerv: { type: 'order', orderId: id, text, resourceId: id, startUtc: '', endUtc: '', locked: false, hasConflict: false } })
      }
      for (const t of model.tasks) {
        if (t.type !== 'operation') continue
        data.push(this.toGanttTask(t, t.resourceId ? `res:${t.resourceId}` : 0, toDate))
      }
      // 资源视图不连工单依赖线(跨资源视觉噪声)。
      return { data, links: [] }
    }

    for (const t of model.tasks) {
      const parent = t.type === 'operation' ? t.parentId ?? 0 : 0
      data.push(this.toGanttTask(t, parent, toDate))
    }
    const links = model.links.map((l) => ({ id: l.id, source: l.source, target: l.target, type: '0' }))
    return { data, links }
  }

  private toGanttTask(t: ScheduleTask, parent: string | number, toDate: (iso: string) => Date | undefined) {
    return {
      id: t.id,
      text: t.text || t.operationId || t.orderId,
      start_date: toDate(t.startUtc),
      end_date: toDate(t.endUtc),
      parent,
      type: t.type === 'order' ? 'project' : 'task',
      progress: t.progress ?? 0,
      readonly: t.type === 'order',
      open: true,
      $resource: t.resourceId,
      nerv: t,
    }
  }
}

function fmt(isoVal?: string): string {
  if (!isoVal) return '—'
  const d = new Date(isoVal)
  return Number.isNaN(d.getTime()) ? isoVal : d.toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false })
}
