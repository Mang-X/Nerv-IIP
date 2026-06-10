import type { ResourceLoadBucket, ScheduleModel, ScheduleTask, TimeScale } from '../../model/types'
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

const SVG_NS = 'http://www.w3.org/2000/svg'
const ROW_H = 38
const BAR_H = 22
const HEADER_H = 34
const LEFT_PAD = 8
const RIGHT_PAD = 32
const MS_PER_HOUR = 3_600_000

const PX_PER_HOUR: Record<Exclude<TimeScale, 'auto'>, number> = {
  hour: 56,
  day: 8,
  week: 1.3,
  month: 0.36,
}

/** 轻量、确定性的 SVG 排程渲染器。免商业许可,供 CI / 视觉基线 / 性能基线 / 自研引擎对接位与降级兜底。 */
export class NativeEngine implements SchedulingEngine {
  private container?: HTMLElement
  private svg?: SVGSVGElement
  private options!: SchedulingEngineOptions
  private model?: ScheduleModel
  private scale: TimeScale = 'day'
  private selectedTaskId?: string
  private readonly listeners = new Map<EngineEventName, Set<(p: unknown) => void>>()
  private drag?: { taskId: string; startX: number; baseStart: number; baseEnd: number }

  mount(container: HTMLElement, options: SchedulingEngineOptions): void {
    this.container = container
    this.options = options
    this.scale = options.scale
    container.classList.add('nerv-gantt', 'nerv-gantt-native')
    this.applyTheme(options.theme)
    const svg = document.createElementNS(SVG_NS, 'svg')
    svg.setAttribute('class', 'nerv-gantt-svg')
    svg.style.display = 'block'
    container.appendChild(svg)
    this.svg = svg
  }

  setData(model: ScheduleModel): void {
    this.model = model
    this.render()
  }

  applyCommand(command: EngineCommand): void {
    switch (command.kind) {
      case 'scaleTo':
        this.scale = command.scale
        this.emit('scaleChanged', { scale: command.scale })
        this.render()
        break
      case 'zoomIn':
      case 'zoomOut': {
        const cur = this.resolveScale()
        const i = SCALE_ORDER.indexOf(cur)
        const next = command.kind === 'zoomIn' ? SCALE_ORDER[Math.max(0, i - 1)] : SCALE_ORDER[Math.min(SCALE_ORDER.length - 1, i + 1)]
        this.scale = next
        this.emit('scaleChanged', { scale: next })
        this.render()
        break
      }
      case 'selectTask':
      case 'focusConflict':
        this.selectedTaskId = command.taskId
        this.emit('taskSelected', { taskId: command.taskId })
        if (command.kind === 'focusConflict') this.emit('conflictClicked', { taskId: command.taskId })
        this.render()
        break
      case 'setReadOnly':
        this.options.readOnly = command.readOnly
        break
      case 'setTheme':
        this.options.theme = command.theme
        this.applyTheme(command.theme)
        this.render()
        break
      case 'scrollToToday':
      case 'fitToScreen':
        // 确定性:用 horizon 中点而非真实时钟,保证测试/视觉基线稳定。
        this.render()
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
    this.listeners.clear()
    if (this.container) {
      this.container.classList.remove('nerv-gantt', 'nerv-gantt-native', 'nerv-gantt-dark')
      this.container.replaceChildren()
    }
    this.svg = undefined
    this.model = undefined
  }

  // --- internals ----------------------------------------------------------

  private emit<E extends EngineEventName>(event: E, payload: EngineEvents[E]): void {
    this.listeners.get(event)?.forEach((cb) => cb(payload))
  }

  private applyTheme(theme: ThemeBinding): void {
    if (!this.container) return
    this.container.classList.toggle('nerv-gantt-dark', theme.isDark)
    for (const [k, v] of Object.entries(theme.tokens)) this.container.style.setProperty(k, v)
  }

  private token(name: string, fallback: string): string {
    return this.options.theme.tokens[name]?.trim() || fallback
  }

  private resolveScale(): Exclude<TimeScale, 'auto'> {
    if (this.scale !== 'auto') return this.scale
    const h = this.model?.horizon
    if (!h?.startUtc || !h?.endUtc) return 'day'
    const days = (Date.parse(h.endUtc) - Date.parse(h.startUtc)) / (24 * MS_PER_HOUR)
    if (days <= 2) return 'hour'
    if (days <= 14) return 'day'
    if (days <= 90) return 'week'
    return 'month'
  }

  private layoutRows(model: ScheduleModel): ScheduleTask[] {
    if (this.options.view === 'order') {
      // 工单分组 → 其工序(按序),树形排布。
      const orders = model.tasks.filter((t) => t.type === 'order')
      const rows: ScheduleTask[] = []
      for (const o of orders) {
        rows.push(o)
        rows.push(
          ...model.tasks
            .filter((t) => t.type === 'operation' && t.orderId === o.orderId)
            .sort((a, b) => a.operationSequence - b.operationSequence),
        )
      }
      // 容错:无父的工序也排进去。
      for (const t of model.tasks) if (!rows.includes(t)) rows.push(t)
      return rows
    }
    // 资源视图:只渲染工序行(按资源分组);order 节点不出条。
    return model.tasks.filter((t) => t.type === 'operation')
  }

  private render(): void {
    const { svg, model } = this
    if (!svg || !model) return
    svg.replaceChildren()

    const scale = this.resolveScale()
    const pxPerHour = PX_PER_HOUR[scale]
    const start = Date.parse(model.horizon.startUtc) || 0
    const end = Date.parse(model.horizon.endUtc) || start + 24 * MS_PER_HOUR
    const x = (iso: string): number =>
      LEFT_PAD + ((Date.parse(iso) || start) - start) / MS_PER_HOUR * pxPerHour
    const width = Math.max(640, x(model.horizon.endUtc) + RIGHT_PAD)

    const rows = this.layoutRows(model)
    const resourceRowIndex = new Map<string, number>()
    if (this.options.view === 'resource') {
      model.resources.forEach((r, i) => resourceRowIndex.set(r.id, i))
    }
    const rowCount = this.options.view === 'resource' ? Math.max(1, model.resources.length) : rows.length
    const height = HEADER_H + rowCount * ROW_H + 8

    svg.setAttribute('viewBox', `0 0 ${width} ${height}`)
    svg.setAttribute('width', String(width))
    svg.setAttribute('height', String(height))

    const border = this.token('--border', 'oklch(0.922 0 0)')
    const brand = this.token('--brand', 'oklch(0.55 0.18 255)')
    const destructive = this.token('--destructive', 'oklch(0.577 0.245 27.325)')
    const warning = this.token('--warning', 'oklch(0.75 0.15 75)')
    const muted = this.token('--muted', 'oklch(0.97 0 0)')
    const foreground = this.token('--foreground', 'oklch(0.145 0 0)')

    // 行背景 + 发丝网格(每天一条)。
    const grid = document.createElementNS(SVG_NS, 'g')
    grid.setAttribute('class', 'nerv-gantt-grid')
    for (let r = 0; r < rowCount; r++) {
      if (r % 2 === 1) {
        const stripe = rect(LEFT_PAD, HEADER_H + r * ROW_H, width - LEFT_PAD, ROW_H, muted)
        stripe.setAttribute('opacity', '0.5')
        grid.appendChild(stripe)
      }
    }
    for (let t = start; t <= end; t += 24 * MS_PER_HOUR) {
      const gx = LEFT_PAD + (t - start) / MS_PER_HOUR * pxPerHour
      const line = document.createElementNS(SVG_NS, 'line')
      line.setAttribute('x1', String(gx))
      line.setAttribute('y1', String(HEADER_H))
      line.setAttribute('x2', String(gx))
      line.setAttribute('y2', String(height))
      line.setAttribute('stroke', border)
      line.setAttribute('stroke-width', '1')
      grid.appendChild(line)
    }
    // “现在”标记:horizon 中点(确定性)。
    const nowX = LEFT_PAD + (end - start) / 2 / MS_PER_HOUR * pxPerHour
    const nowLine = document.createElementNS(SVG_NS, 'line')
    nowLine.setAttribute('x1', String(nowX))
    nowLine.setAttribute('y1', String(HEADER_H - 6))
    nowLine.setAttribute('x2', String(nowX))
    nowLine.setAttribute('y2', String(height))
    nowLine.setAttribute('stroke', brand)
    nowLine.setAttribute('stroke-width', '1.5')
    nowLine.setAttribute('stroke-dasharray', '4 3')
    nowLine.setAttribute('class', 'nerv-gantt-now')
    grid.appendChild(nowLine)
    svg.appendChild(grid)

    // 资源视图:负载直方图(产能带 + 过载热度)。
    if (this.options.view === 'resource') {
      const loadG = document.createElementNS(SVG_NS, 'g')
      loadG.setAttribute('class', 'nerv-gantt-loads')
      for (const l of model.loads) {
        const ri = resourceRowIndex.get(l.resourceId)
        if (ri === undefined) continue
        drawLoad(loadG, l, ri)
      }
      svg.appendChild(loadG)
    }

    function drawLoad(g: SVGGElement, l: ResourceLoadBucket, ri: number): void {
      const bx = LEFT_PAD + ((Date.parse(l.windowStartUtc) || start) - start) / MS_PER_HOUR * pxPerHour
      const bw = Math.max(2, ((Date.parse(l.windowEndUtc) || start) - (Date.parse(l.windowStartUtc) || start)) / MS_PER_HOUR * pxPerHour)
      const band = rect(bx, HEADER_H + ri * ROW_H + ROW_H - 6, bw, 4, l.utilization > 1 ? destructive : warning)
      band.setAttribute('opacity', String(Math.min(1, 0.25 + l.utilization * 0.5)))
      g.appendChild(band)
    }

    // 任务条。
    const bars = document.createElementNS(SVG_NS, 'g')
    bars.setAttribute('class', 'nerv-gantt-bars')
    rows.forEach((task, i) => {
      const node = document.createElementNS(SVG_NS, 'g')
      node.setAttribute('data-task-id', task.id)
      node.setAttribute('data-task-type', task.type)
      if (task.hasConflict) node.setAttribute('data-conflict', task.conflictReason ?? 'true')
      node.setAttribute('class', 'nerv-gantt-node')

      const view = this.options.view
      const rowIdx = view === 'resource' ? (task.resourceId ? resourceRowIndex.get(task.resourceId) ?? i : i) : i
      const y = HEADER_H + rowIdx * ROW_H + (ROW_H - BAR_H) / 2
      const bx = x(task.startUtc)
      const bw = Math.max(6, x(task.endUtc) - bx)

      const selected = task.id === this.selectedTaskId
      const isOrder = task.type === 'order'
      const bar = rect(bx, isOrder ? y + 6 : y, bw, isOrder ? BAR_H - 12 : BAR_H, isOrder ? muted : brand)
      bar.setAttribute('rx', isOrder ? '3' : '6')
      bar.setAttribute('class', isOrder ? 'nerv-gantt-bar nerv-gantt-bar-order' : 'nerv-gantt-bar nerv-gantt-bar-op')
      if (task.hasConflict) {
        bar.setAttribute('stroke', destructive)
        bar.setAttribute('stroke-width', '2')
      }
      if (selected) {
        bar.setAttribute('stroke', brand)
        bar.setAttribute('stroke-width', '2.5')
        bar.setAttribute('filter', 'drop-shadow(0 0 4px ' + brand + ')')
      }
      node.appendChild(bar)

      if (task.locked && !isOrder) {
        const lock = document.createElementNS(SVG_NS, 'circle')
        lock.setAttribute('cx', String(bx + bw - 6))
        lock.setAttribute('cy', String(y + 5))
        lock.setAttribute('r', '3')
        lock.setAttribute('fill', foreground)
        lock.setAttribute('class', 'nerv-gantt-lock')
        node.appendChild(lock)
      }

      node.addEventListener('click', () => {
        this.selectedTaskId = task.id
        this.emit('taskSelected', { taskId: task.id })
        if (task.hasConflict) this.emit('conflictClicked', { taskId: task.id })
        this.render()
      })

      if (!this.options.readOnly && !isOrder) this.bindDrag(node, task, pxPerHour)

      bars.appendChild(node)
    })
    svg.appendChild(bars)
  }

  private bindDrag(node: SVGGElement, task: ScheduleTask, pxPerHour: number): void {
    node.style.cursor = 'grab'
    const onDown = (e: PointerEvent) => {
      this.drag = {
        taskId: task.id,
        startX: e.clientX,
        baseStart: Date.parse(task.startUtc),
        baseEnd: Date.parse(task.endUtc),
      }
      node.style.cursor = 'grabbing'
      window.addEventListener('pointermove', onMove)
      window.addEventListener('pointerup', onUp)
    }
    const onMove = (_e: PointerEvent) => {
      /* 视觉拖影由组件层/真实指针驱动;此处保留路径,落点在 pointerup 归一化。 */
    }
    const onUp = (e: PointerEvent) => {
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup', onUp)
      node.style.cursor = 'grab'
      const d = this.drag
      this.drag = undefined
      if (!d) return
      const deltaMs = ((e.clientX - d.startX) / pxPerHour) * MS_PER_HOUR
      const newStart = new Date(d.baseStart + deltaMs).toISOString()
      const newEnd = new Date(d.baseEnd + deltaMs).toISOString()
      this.emit('taskDragEnd', {
        taskId: task.id,
        operationId: task.operationId,
        resourceId: task.resourceId,
        startUtc: newStart,
        endUtc: newEnd,
        kind: 'move',
      })
    }
    node.addEventListener('pointerdown', onDown)
  }
}

function rect(x: number, y: number, w: number, h: number, fill: string): SVGRectElement {
  const r = document.createElementNS(SVG_NS, 'rect')
  r.setAttribute('x', String(x))
  r.setAttribute('y', String(y))
  r.setAttribute('width', String(w))
  r.setAttribute('height', String(h))
  r.setAttribute('fill', fill)
  return r
}
