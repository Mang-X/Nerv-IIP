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

// 最小化的 DHTMLX gantt 实例形状(只声明适配器用到的子集,避免引入试用版类型依赖)。
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
  selectTask?: (id: string | number) => void
  render: () => void
  destructor?: () => void
  showDate?: (date: Date) => void
}
interface DhxTask {
  id: string
  start_date?: Date
  end_date?: Date
  $resource?: string
}

const SCALE_CONFIG: Record<Exclude<TimeScale, 'auto'>, Array<Record<string, unknown>>> = {
  hour: [
    { unit: 'day', step: 1, format: '%m-%d' },
    { unit: 'hour', step: 1, format: '%H' },
  ],
  day: [
    { unit: 'month', step: 1, format: '%Y-%m' },
    { unit: 'day', step: 1, format: '%d' },
  ],
  week: [
    { unit: 'month', step: 1, format: '%Y-%m' },
    { unit: 'week', step: 1, format: '%W' },
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

/** DHTMLX Gantt 9.x(试用专业版)适配器。封装 vanilla 核心为统一 SchedulingEngine。 */
export class DhtmlxEngine implements SchedulingEngine {
  private container?: HTMLElement
  private options!: SchedulingEngineOptions
  private gantt?: DhxGantt
  private model?: ScheduleModel
  private scale: TimeScale = 'day'
  private selectedTaskId?: string
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

    const inst = this.createInstance() as DhxGantt | null
    if (!inst) return // 不可用:由 useEngine 负责回落 NativeEngine。
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
    g.clearAll()
    g.parse(this.toGanttData(model))
    this.mirrorTaskIds()
  }

  applyCommand(command: EngineCommand): void {
    const g = this.gantt
    switch (command.kind) {
      case 'scaleTo':
        this.scale = command.scale
        if (g) {
          g.config.scales = SCALE_CONFIG[this.resolveScale()]
          g.render()
        }
        this.emit('scaleChanged', { scale: command.scale })
        break
      case 'zoomIn':
      case 'zoomOut': {
        const i = SCALE_ORDER.indexOf(this.resolveScale())
        this.scale =
          command.kind === 'zoomIn'
            ? SCALE_ORDER[Math.max(0, i - 1)]
            : SCALE_ORDER[Math.min(SCALE_ORDER.length - 1, i + 1)]
        if (g) {
          g.config.scales = SCALE_CONFIG[this.resolveScale()]
          g.render()
        }
        this.emit('scaleChanged', { scale: this.scale })
        break
      }
      case 'selectTask':
      case 'focusConflict':
        this.selectedTaskId = command.taskId
        g?.selectTask?.(command.taskId)
        this.emit('taskSelected', { taskId: command.taskId })
        if (command.kind === 'focusConflict') this.emit('conflictClicked', { taskId: command.taskId })
        break
      case 'setReadOnly':
        this.options.readOnly = command.readOnly
        if (g) {
          g.config.readonly = command.readOnly
          g.render()
        }
        break
      case 'setTheme':
        this.options.theme = command.theme
        if (this.container) applySkin(this.container, command.theme)
        g?.render()
        break
      case 'scrollToToday':
      case 'fitToScreen':
        if (g?.showDate && this.model) g.showDate(this.horizonMidpoint())
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
    if (this.container) {
      this.container.classList.remove('nerv-gantt', 'nerv-gantt-dhx', 'nerv-gantt-dark', 'nerv-dhx-scope')
      this.container.replaceChildren()
    }
  }

  // --- internals ----------------------------------------------------------

  private emit<E extends EngineEventName>(event: E, payload: EngineEvents[E]): void {
    this.listeners.get(event)?.forEach((cb) => cb(payload))
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

  private horizonMidpoint(): Date {
    const h = this.model!.horizon
    return new Date((Date.parse(h.startUtc) + Date.parse(h.endUtc)) / 2)
  }

  private configure(inst: DhxGantt, options: SchedulingEngineOptions): void {
    inst.config.date_format = '%Y-%m-%d %H:%i'
    inst.config.readonly = options.readOnly
    inst.config.drag_move = !options.readOnly
    inst.config.drag_resize = !options.readOnly
    inst.config.drag_links = !options.readOnly
    inst.config.row_height = 38
    inst.config.bar_height = 22
    inst.config.scales = SCALE_CONFIG[this.resolveScale()]
    try {
      inst.plugins?.({
        tooltip: true,
        marker: true,
        undo: true,
        critical_path: options.view === 'order',
      })
    } catch {
      /* 插件不可用(精简构建):忽略,核心仍可用。 */
    }
    // 给条形按状态加类,供 token 皮肤上色。
    inst.templates.task_class = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask }) => {
      const t = task.nerv
      const cls: string[] = []
      if (t?.hasConflict) cls.push('nerv-conflict')
      if (t?.id === this.selectedTaskId) cls.push('nerv-selected')
      return cls.join(' ')
    }
  }

  private wireEvents(inst: DhxGantt): void {
    this.eventIds.push(
      inst.attachEvent('onTaskClick', (id) => {
        const taskId = String(id)
        this.selectedTaskId = taskId
        this.emit('taskSelected', { taskId })
        const conflicted = this.model?.tasks.find((t) => t.id === taskId)?.hasConflict
        if (conflicted) this.emit('conflictClicked', { taskId })
        return true
      }),
    )
    this.eventIds.push(
      inst.attachEvent('onAfterTaskDrag', (id, mode) => {
        const taskId = String(id)
        const task = inst.getTask(taskId)
        const src = this.model?.tasks.find((t) => t.id === taskId)
        this.emit('taskDragEnd', {
          taskId,
          operationId: src?.operationId ?? taskId,
          resourceId: task?.$resource ?? src?.resourceId,
          startUtc: task?.start_date?.toISOString() ?? src?.startUtc ?? '',
          endUtc: task?.end_date?.toISOString() ?? src?.endUtc ?? '',
          kind: mode === 'resize' ? 'resize' : task?.$resource && task.$resource !== src?.resourceId ? 'reassign' : 'move',
        })
      }),
    )
    this.eventIds.push(inst.attachEvent('onGanttRender', () => this.mirrorTaskIds()))
  }

  /** 把 DHTMLX 的 task_id 属性镜像为统一的 data-task-id,供引擎契约/选择器统一定位。 */
  private mirrorTaskIds(): void {
    const root = this.container
    if (!root) return
    root.querySelectorAll('[task_id]').forEach((el) => {
      const id = el.getAttribute('task_id')
      if (id && !el.hasAttribute('data-task-id')) el.setAttribute('data-task-id', id)
    })
  }

  private toGanttData(model: ScheduleModel): { data: unknown[]; links: unknown[] } {
    const toDate = (iso: string) => (iso ? new Date(iso) : undefined)
    const data: unknown[] = []

    if (this.options.view === 'resource') {
      for (const r of model.resources) {
        data.push({ id: `res:${r.id}`, text: r.text, type: 'project', open: true })
      }
      for (const t of model.tasks) {
        if (t.type !== 'operation') continue
        data.push(this.toGanttTask(t, t.resourceId ? `res:${t.resourceId}` : 0, toDate))
      }
    } else {
      for (const t of model.tasks) {
        const parent = t.type === 'operation' ? t.parentId ?? 0 : 0
        data.push(this.toGanttTask(t, parent, toDate))
      }
    }

    const links = model.links.map((l) => ({ id: l.id, source: l.source, target: l.target, type: '0' }))
    return { data, links }
  }

  private toGanttTask(t: ScheduleTask, parent: string | number, toDate: (iso: string) => Date | undefined) {
    return {
      id: t.id,
      text: t.text,
      start_date: toDate(t.startUtc),
      end_date: toDate(t.endUtc),
      parent,
      type: t.type === 'order' ? 'project' : 'task',
      progress: t.progress ?? 0,
      readonly: t.type === 'order',
      $resource: t.resourceId,
      nerv: t, // 供 templates 读取业务状态(冲突/锁定/选中)。
    }
  }
}
