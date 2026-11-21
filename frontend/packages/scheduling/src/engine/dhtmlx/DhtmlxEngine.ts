import { scheduleTime } from '../../model/factory-time'
import { freezeWindow, projectFreezeWindow } from '../../model/freeze-window'
import type { ScheduleModel, ScheduleTask, TimeScale } from '../../model/types'
import {
  SCALE_ORDER,
  type EngineCommand,
  type EngineEventName,
  type EngineEvents,
  type SchedulingEngine,
  type SchedulingEngineOptions,
  type Unsubscribe,
} from '../engine'
import { resolveTimeScale } from '../../model/scale'
import { createGanttInstanceSync } from './loader'
import { applySkin } from './skin'
import type { DhxGantt, DhxTask } from './dhtmlx-types'
import { GanttProjection } from './gantt-projection'
import { GRID_COLUMNS } from './grid-columns'
import { playOnceClass, prefersReducedMotion } from './motion'
import { ResourceTooltip } from './resource-tooltip'
import { scalesFor } from './scales'
import { cardHtml, escapeHtml, fmt, LOCK_SVG, tooltipHtml } from './task-templates'
export { hasAnyLaneKpi, resolveLaneKpiVisibility } from './grid-columns'
export type { LaneKpiKey, LaneKpiVisibility } from './grid-columns'
export { cardHtml, tooltipHtml } from './task-templates'

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
  /**
   * 搜索命中集。`undefined`/空 = 不在搜索态(所有条正常显示);非空 = 命中高亮、其余压暗。
   * 用 Set 而不是每次遍历数组:资源板实测 400+ 条,task_class 每次重绘对每条都要问一次。
   */
  private searchHits?: Set<string>
  private readonly projection = new GanttProjection()
  private markerId?: string
  private lastPointerY = 0
  private lastPointerX = 0
  private pointerMove?: (e: MouseEvent) => void
  private pointerDown?: (e: MouseEvent) => void
  private pointerUp?: (e: MouseEvent) => void
  private keyDown?: (e: KeyboardEvent) => void
  private readonly resourceTooltip = new ResourceTooltip()
  private settleRaf = 0
  private dropHint?: HTMLElement
  private dragging = false
  private dragTaskId?: string
  private dragGrabOffX = 0
  private dragOrigStart?: Date
  private dragOrigEnd?: Date
  private dragOrigLaneId?: string
  private cancelZone?: HTMLElement
  private overCancel = false
  private suppressNextClick = false
  private shownLinkIds: string[] = []
  private resizeObserver?: ResizeObserver
  private resizeRaf = 0
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
    if (!inst) return // 不可用:useEngine 不会挂载本引擎,组件显示占位。
    this.gantt = inst

    this.configure(inst, options)
    this.wireEvents(inst)
    inst.init(container)
    // 容器尺寸常在挂载后才定型(文档/响应式布局、字体或图片加载后回流)。DHTMLX 不自动重排,
    // 会停在初始(可能为 0/极窄)宽度,时间线塌陷成 1px。观察容器尺寸变化并 rAF 去抖重排。
    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver((entries) => {
        const w = entries[0]?.contentRect.width ?? 0
        if (w < 2) return
        cancelAnimationFrame(this.resizeRaf)
        // setSizes 重算布局盒宽度(时间线 = 容器 − 网格);仅 render() 不重算盒尺寸,
        // 容器变宽后时间线仍会停在初始(可能塌成 1px)。先 setSizes 再 render。
        this.resizeRaf = requestAnimationFrame(() => {
          this.gantt?.setSizes?.()
          this.gantt?.render()
        })
      })
      this.resizeObserver.observe(container)
    }
    // 计划基线只在工单甘特出现;资源排产板是卡片板,不画计划/实际基线(避免卡片上多出细线)。
    if (options.view !== 'resource') this.addBaselineLayer(inst)
    // 资源时间块作为背景带:见 task_class 的 .nerv-block 处理(条本身样式化,不再单独背景层——
    // addTaskLayer 在 split 泳道视图不渲染子任务)。
    // 资源排产板:时间块(维护/停机/换线/换型)画成齐行铺满的淡斜纹背景带(位于工单卡片之下)。
    // 资源视图跨泳道拖拽改派:记录指针 Y(监听 document,DHTMLX 拖拽会捕获指针,container 上收不到)。
    // 同时用自有覆盖层做拖拽落点预览(不被 DHTMLX 重绘冲掉)。
    if (options.view === 'resource') {
      const hint = document.createElement('div')
      hint.className = 'nerv-drop-hint'
      hint.style.display = 'none'
      hint.innerHTML = '<span class="nerv-drop-tip"></span>'
      container.appendChild(hint)
      this.dropHint = hint
      // 取消区:拖拽时出现,松手于此处则撤销改派。
      const cancel = document.createElement('div')
      cancel.className = 'nerv-drop-cancel'
      cancel.style.display = 'none'
      cancel.innerHTML =
        '<svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/></svg><span>拖到此处取消改派</span>'
      container.appendChild(cancel)
      this.cancelZone = cancel

      // 自绘轻量 tooltip:跟指针平滑移动(DHTMLX 插件跟单元格、内容重、在超大卡片上滞后)。
      // 内容一次构建、移动时只改 transform,拖拽进行时隐藏。
      this.resourceTooltip.mount()

      // 自定义拖拽:DHTMLX 原生 move 已关(见 configure)。原块静止,虚影随指针(横向=改时间、
      // 纵向=改泳道),松手于泳道提交、松手于取消区撤销。彻底解决"原块漂"与"横向失效"。
      let downX = 0
      let downY = 0
      this.pointerDown = (e: MouseEvent) => {
        if (e.button !== 0 || this.options.readOnly) return
        // 端点交给原生 resize;整条移动仍走虚影与跨泳道改派。
        if ((e.target as HTMLElement)?.closest?.('.gantt_task_drag')) return
        const bar = (e.target as HTMLElement)?.closest?.('.gantt_task_line') as HTMLElement | null
        if (!bar || !bar.querySelector('.nerv-card')) return
        const id = bar.getAttribute('task_id')
        const t = id ? inst.getTask(id) : undefined
        if (!id || !t) return
        if ((t.nerv?.segments?.length ?? 0) > 1) return
        if (t.nerv?.locked) {
          // 已锁定:不可拖拽。给出抖动反馈并上报(上层提示「先解锁」并聚焦该块)。
          this.signalLockedDrag(id, bar)
          return
        }
        this.dragTaskId = id
        this.dragGrabOffX = e.clientX - bar.getBoundingClientRect().left
        downX = e.clientX
        downY = e.clientY
        this.dragOrigStart = t.start_date ? new Date(t.start_date) : undefined
        this.dragOrigEnd = t.end_date ? new Date(t.end_date) : undefined
        this.dragOrigLaneId = t.parent != null ? String(t.parent) : undefined
        this.dragging = false
      }
      this.pointerMove = (e: MouseEvent) => {
        this.lastPointerY = e.clientY
        this.lastPointerX = e.clientX
        // 自绘 tooltip 跟指针(拖拽进行中不显,交给落点虚影)。
        this.resourceTooltip.update(inst, e, this.container, this.dragging)
        if (!this.dragTaskId) return
        if (!this.dragging) {
          if (Math.abs(e.clientX - downX) < 5 && Math.abs(e.clientY - downY) < 5) return
          this.dragging = true
          this.suppressNextClick = true // 拖拽一旦开始,抑制随后的点击(避免弹详情)
          this.resourceTooltip.hide() // 拖拽进行中不显 tooltip(落点虚影已给足反馈)
          if (this.cancelZone) this.cancelZone.style.display = 'flex'
          this.barEl(this.dragTaskId)?.classList.add('nerv-drag-source')
        }
        const cr = this.cancelZone?.getBoundingClientRect()
        this.overCancel =
          !!cr &&
          e.clientX >= cr.left &&
          e.clientX <= cr.right &&
          e.clientY >= cr.top &&
          e.clientY <= cr.bottom
        this.cancelZone?.classList.toggle('nerv-drop-cancel-hot', this.overCancel)
        this.updateDropHint(inst)
      }
      this.pointerUp = () => {
        const id = this.dragTaskId
        if (!id) return
        const committed = this.dragging && !this.overCancel
        if (committed) {
          // 关键:提交时**不**在此处去掉 nerv-drag-source。原块保持淡出,直到 commit→setData
          // 重建出「新位置」的条(新 DOM 元素,天然无淡出类)。否则先去暗会让原块在**旧位置**
          // 亮起一帧、再跳到新位 = 用户看到的"闪回"。
          this.commitCustomDrag(inst, id)
        } else {
          // 未提交(取消 / 未越过阈值):就地恢复原块。
          this.barEl(id)?.classList.remove('nerv-drag-source')
        }
        this.hideDropHint()
        this.dragTaskId = undefined
      }
      // Esc 中止拖拽:走取消分支(恢复原块、隐藏虚影、不上报 dragEnd)。
      this.keyDown = (e: KeyboardEvent) => {
        if (e.key !== 'Escape' || !this.dragging || !this.dragTaskId) return
        e.preventDefault()
        this.cancelCustomDrag()
      }
      container.addEventListener('mousedown', this.pointerDown)
      document.addEventListener('mousemove', this.pointerMove)
      document.addEventListener('mouseup', this.pointerUp)
      document.addEventListener('keydown', this.keyDown)
    }
    if (this.model) this.setData(this.model)
  }

  setData(model: ScheduleModel): void {
    this.model = model
    const g = this.gantt
    if (!g) return
    // model 已知后再应用自适应刻度(configure 早于 setData,那时 horizon 未知)。
    g.config.scales = this.scalesFor()
    g.clearAll()
    this.shownLinkIds = [] // clearAll 已清掉连线
    // project 顺带算出本批数据里哪些泳道指标真有值(见 kpiVisibility)。列定义要在 parse
    // 之前按结果重建:整列被去掉时泳道名列要变宽,晚于 parse 改会先闪一帧旧布局。
    const data = this.projection.project(model, this.options)
    g.config.columns = GRID_COLUMNS(
      this.options.view,
      this.resolveDimLabel(),
      () => this.projection.kpiVisibility,
    )
    g.parse(data)
    this.refreshMarker()
    this.mirrorTaskIds()
    if (this.selectedTaskId) this.showOrderLinks(this.selectedTaskId) // 拖拽/重排后保持选中工单连线
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
        if (command.kind === 'focusConflict')
          this.emit('conflictClicked', { taskId: command.taskId })
        break
      case 'revealTask':
        // showTask 同时管纵向(滚到那一行)与横向(滚到那段时间)。条不存在就什么都不做,
        // 不要退化成 showDate:搜索结果落在已折叠/已过滤掉的行上时,乱跳视口比不动更糟。
        if (g?.isTaskExists?.(command.taskId)) g.showTask?.(command.taskId)
        break
      case 'setSearchHighlight': {
        this.searchHits = command.taskIds.length ? new Set(command.taskIds) : undefined
        // task_class 只在重绘时重算,必须显式 render,否则输入框里字都打完了图还没变。
        g?.render()
        break
      }
      case 'setReadOnly':
        this.options.readOnly = command.readOnly
        if (g) {
          const nativeDrag = !command.readOnly && this.options.view !== 'resource'
          g.config.readonly = command.readOnly
          g.config.drag_move = nativeDrag
          g.config.drag_resize = !command.readOnly
          g.config.drag_links = !command.readOnly && this.options.view === 'order'
          g.render()
        }
        break
      case 'setTheme':
        this.options.theme = command.theme
        if (this.container) applySkin(this.container, command.theme)
        g?.render()
        break
      case 'setGroupBy':
        this.options.groupBy = command.groupBy
        {
          const cols = g?.config?.columns as Array<{ label?: string }> | undefined
          if (cols?.[0]) cols[0].label = this.resolveDimLabel()
        }
        if (this.model) this.setData(this.model)
        break
      case 'setLaneOrder':
        this.options.laneOrder = command.laneOrder
        if (this.model) this.setData(this.model)
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
    if (this.pointerMove) document.removeEventListener('mousemove', this.pointerMove)
    if (this.pointerUp) document.removeEventListener('mouseup', this.pointerUp)
    if (this.pointerDown && this.container)
      this.container.removeEventListener('mousedown', this.pointerDown)
    if (this.keyDown) document.removeEventListener('keydown', this.keyDown)
    this.pointerMove = undefined
    this.pointerUp = undefined
    this.pointerDown = undefined
    this.keyDown = undefined
    this.dropHint?.remove()
    this.dropHint = undefined
    this.cancelZone?.remove()
    this.cancelZone = undefined
    this.resourceTooltip.destroy()
    this.dragging = false
    cancelAnimationFrame(this.settleRaf)
    cancelAnimationFrame(this.resizeRaf)
    this.resizeObserver?.disconnect()
    this.resizeObserver = undefined
    g?.destructor?.()
    this.gantt = undefined
    this.markerId = undefined
    if (this.container) {
      this.container.classList.remove(
        'nerv-gantt',
        'nerv-gantt-dhx',
        'nerv-gantt-dark',
        'nerv-dhx-scope',
      )
      this.container.replaceChildren()
    }
  }

  // --- internals ----------------------------------------------------------

  private emit<E extends EngineEventName>(event: E, payload: EngineEvents[E]): void {
    this.listeners.get(event)?.forEach((cb) => cb(payload))
  }

  private applyScale(g?: DhxGantt): void {
    if (!g) return
    g.config.scales = this.scalesFor()
    g.render()
  }

  /** 刻度解析与图例同源(model/scale.ts):图例讲的班次边界必须和这里画出来的一致。 */
  private resolveScale(): Exclude<TimeScale, 'auto'> {
    return resolveTimeScale(this.scale, this.model?.horizon)
  }
  private scalesFor(): Array<Record<string, unknown>> {
    return scalesFor(this.resolveScale(), this.options.view, this.model, this.gantt)
  }
  /** 当前分组维度的显示名(左侧列1表头)。 */
  private resolveDimLabel(): string {
    const key = this.options.groupBy || 'workCenter'
    return this.model?.groupDimensions?.find((d) => d.key === key)?.label ?? '工作中心'
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
    this.markerId = g.addMarker({
      start_date: this.nowDate(),
      css: 'nerv-today-marker',
      text: '现在',
    })
  }

  private configure(inst: DhxGantt, options: SchedulingEngineOptions): void {
    const c = inst.config
    c.date_format = '%Y-%m-%d %H:%i'
    // 关键:工序是亚天(小时级)。默认 duration_unit='day' 会把时长取整到 0 天,
    // 导致拖拽/拉伸时条宽变 0 并乱跳。改为按小时计时 + 按小时吸附。
    c.duration_unit = 'hour'
    c.duration_step = 1
    c.time_step = 60
    c.round_dnd_dates = true
    // 关闭 DHTMLX 自带错误弹窗:多实例同页时,跨实例的 getTask/addLink 偶发 "Task not found"
    // 会被 DHTMLX 弹成右上角红条堆叠;我们已在调用处按容器/存在性守卫,这里再兜底禁用其错误 UI。
    c.show_errors = false
    c.readonly = options.readOnly
    // 资源排产板整条移动走自定义虚影;端点拉伸沿用 DHTMLX 原生 resize。
    const nativeDrag = !options.readOnly && options.view !== 'resource'
    c.drag_move = nativeDrag
    c.drag_resize = !options.readOnly
    c.drag_links = !options.readOnly && options.view === 'order'
    c.drag_progress = false
    // 网格内拖拽换分支暂时关闭(onAfterTaskMove 易误触、破坏拖拽);改派后续用时间线跨行拖拽实现。
    c.order_branch = false
    c.order_branch_free = false
    c.open_split_tasks = false // split 分组行:同组工序铺在一行,不展开成多行
    c.row_height = options.view === 'resource' ? 280 : 48
    c.bar_height = options.view === 'resource' ? 264 : 22
    c.grid_width = options.view === 'resource' ? 258 : 560
    c.grid_resize = true
    // 默认不画连线;资源板仅在「选中某工序」时动态显示该工单的工序连线(showOrderLinks)。
    c.show_links = true
    c.highlight_critical_path = options.view === 'order'
    c.columns = GRID_COLUMNS(
      options.view,
      this.resolveDimLabel(),
      () => this.projection.kpiVisibility,
    )
    c.scales = this.scalesFor()
    // 资源排产板小时刻度有 3 行(日期/班次/小时),抬高刻度区。
    c.scale_height = options.view === 'resource' && this.resolveScale() === 'hour' ? 70 : 50
    c.min_column_width = 36
    // tooltip 每帧跟指针而非停在进入点:超小 timeout 让重定位近乎实时,并给指针留出偏移。
    c.tooltip_timeout = 1
    c.tooltip_offset_x = 14
    c.tooltip_offset_y = 18

    // 资源排产板改用自绘轻量 tooltip(跟指针、内容不重排);工单甘特仍用 DHTMLX 插件。
    const useDhxTooltip = options.view !== 'resource'
    try {
      inst.plugins?.({
        tooltip: useDhxTooltip,
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
      // 资源时间块不作任务条(由 GanttProjection 排除),改由 timeline_cell_class 给单元格上底纹。
      if (t?.type === 'order') cls.push('nerv-order')
      if (t?.colorKey && !t?.blockKind) cls.push(`nerv-cat-${t.colorKey}`)
      if (t?.hasConflict) cls.push('nerv-conflict')
      if (t?.downtimeRisk) cls.push('nerv-downtime-risk')
      if (t?.locked) cls.push('nerv-locked')
      if ((t?.segments?.length ?? 0) > 1) cls.push('nerv-segmented')
      if (t?.id === this.selectedTaskId) cls.push('nerv-selected')
      // 搜索态:命中加环、未命中压暗。工单汇总行不参与压暗——把父行也压掉会让整棵树"消失",
      // 看起来像图加载失败。
      if (this.searchHits && t) {
        if (this.searchHits.has(t.id)) cls.push('nerv-search-hit')
        else if (t.type !== 'order') cls.push('nerv-search-miss')
      }
      return cls.join(' ')
    }
    inst.templates.grid_row_class = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask }) =>
      task.nerv?.hasConflict ? 'nerv-row-conflict' : ''
    inst.templates.tooltip_text = (
      _s: unknown,
      _e: unknown,
      task: { nerv?: ScheduleTask; text?: string },
    ) => (task.nerv ? tooltipHtml(task.nerv) : (task.text ?? ''))
    const isResource = options.view === 'resource'
    // 资源排产板:条内渲染工单卡片;工单甘特:条内不渲染,工序名放右侧。
    inst.templates.task_text = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask }) => {
      const t = task.nerv
      if ((t?.segments?.length ?? 0) > 1) {
        const origin = inst.getTaskPosition!(
          task as DhxTask,
          new Date(t!.startUtc),
          new Date(t!.endUtc),
        ).left
        return t!
          .segments!.map((segment, index) => {
            const pos = inst.getTaskPosition!(
              task as DhxTask,
              new Date(segment.startUtc),
              new Date(segment.endUtc),
            )
            return `<div class="nerv-segment" style="left:${pos.left - origin}px;width:${pos.width}px" title="第 ${index + 1} 段 · ${fmt(segment.startUtc, t!.timezone)} 至 ${fmt(segment.endUtc, t!.timezone)}">${isResource ? cardHtml(t!) : ''}</div>`
          })
          .join('')
      }
      if (!isResource || t?.type !== 'operation') return ''
      return cardHtml(t)
    }
    inst.templates.rightside_text = (
      _s: unknown,
      _e: unknown,
      task: { nerv?: ScheduleTask; text?: string },
    ) => {
      if (isResource) return ''
      const t = task.nerv
      if (t?.type !== 'operation') return ''
      const lock = t.locked ? `<span class="nerv-card-lock">${LOCK_SVG}</span>` : ''
      return `<span class="nerv-bar-label">${escapeHtml(task.text ?? '')}${t.downtimeRisk ? ' · 停机风险' : ''}${lock}</span>`
    }
    // 时间线底纹讲三件事:
    // ① 工作 / 非工作——有后端工作日历时按日历判定(休息日、班次之外都算非工作),
    //    没有日历时才退回「周末 + 20:00–08:00 夜间」的通用作息假设;
    // ② 班次边界——单元格正好落在某个班次起点上时加一条竖线(nerv-shift-start);
    // ③ 资源时间块(维护/停机/换线/换型)——该行对应的资源在此时段有块 → 叠加斜纹底纹。
    // 三者都走单元格,恒在卡片之下、与格子融为一体、绝不覆盖卡片。
    // 单元格内容天然在任务条下层，按真实格线裁剪，不将稳定窗口当不可用时间块。
    inst.templates.timeline_cell_content = (task: { id?: string | number }, date: Date) => {
      if (
        this.options.view !== 'resource' ||
        (this.options.groupBy ?? 'workCenter') !== 'workCenter'
      )
        return ''
      const workCenterId = String(task.id ?? '').replace(/^lane:/, '')
      const window = freezeWindow(this.model?.freezeContext, workCenterId)
      if (!window) return ''
      const scales = inst.config.scales as Array<{ unit: string; step: number }>
      const scale = scales[scales.length - 1]!
      const end = inst.date!.add(date, scale.step, scale.unit)
      const projected = projectFreezeWindow(window, date.getTime(), end.getTime())
      if (!projected) return ''
      return `<span class="nv-scheduling-freeze-window" data-freeze-window style="left:${projected.left}%;width:${projected.width}%" aria-hidden="true"></span>`
    }

    inst.templates.timeline_cell_class = (task: { id?: string | number }, date: Date) => {
      const classes: string[] = []
      const ts = date.getTime()
      const lanes = this.projection.lanesOf(this.options.view, task?.id)
      if (this.projection.workingWindows.size) {
        if (!this.projection.isWorkingAt(lanes, ts)) {
          const day = date.getDay()
          classes.push(day === 0 || day === 6 ? 'nerv-weekend' : 'nerv-offwork')
        }
        if (this.projection.shiftStarts.has(ts)) classes.push('nerv-shift-start')
      } else {
        const day = date.getDay()
        const h = date.getHours()
        if (day === 0 || day === 6 || h < 8 || h >= 20) classes.push('nerv-offwork')
      }
      if (this.projection.blockCells.size) {
        for (const lane of lanes) {
          const wins = this.projection.blockCells.get(lane)
          if (!wins) continue
          const hit = wins.find((w) => ts >= w.start && ts < w.end)
          if (hit) {
            classes.push('nerv-cell-block', `nerv-cell-block-${hit.kind}`)
            break
          }
        }
      }
      return classes.join(' ')
    }
  }

  private wireEvents(inst: DhxGantt): void {
    this.eventIds.push(
      inst.attachEvent('onTaskClick', (id) => {
        // 资源板自定义拖拽后,DHTMLX 会把 mouseup 当点击 → 抑制这次点击,避免拖完弹详情。
        if (this.suppressNextClick) {
          this.suppressNextClick = false
          return false
        }
        const taskId = String(id)
        this.selectedTaskId = taskId
        this.markSelectedBar(taskId)
        this.showOrderLinks(taskId)
        this.emit('taskSelected', { taskId })
        if (this.model?.tasks.find((t) => t.id === taskId)?.hasConflict)
          this.emit('conflictClicked', { taskId })
        return true
      }),
    )
    this.eventIds.push(
      inst.attachEvent('onBeforeTaskDrag', (id, mode) => {
        const taskId = String(id)
        const task = inst.getTask(taskId)?.nerv
        if ((task?.segments?.length ?? 0) > 1) return false
        if (this.options.view === 'resource') {
          if (
            this.options.readOnly ||
            mode !== 'resize' ||
            task?.type !== 'operation' ||
            task.blockKind
          )
            return false
          if (task.locked) {
            const bar = this.barEl(taskId)
            if (bar) this.signalLockedDrag(taskId, bar)
            return false
          }
          this.resourceTooltip.hide()
          this.suppressNextClick = true
        }
        return true
      }),
    )
    this.eventIds.push(
      inst.attachEvent('onAfterTaskDrag', (id, mode) =>
        this.emitDrag(inst, String(id), String(mode)),
      ),
    )
    this.eventIds.push(
      inst.attachEvent('onAfterTaskMove', (id) => this.emitDrag(inst, String(id), 'move')),
    )
    // 资源视图整条移动走自定义实现;端点拉伸由上述原生事件归一化。
    this.eventIds.push(inst.attachEvent('onGanttRender', () => this.mirrorTaskIds()))
  }

  /** 资源视图:按指针落点 Y 定位目标泳道任务(split 模式下可见行即泳道)。 */
  private laneAtPointer(inst: DhxGantt): DhxTask | undefined {
    const root = this.container
    if (!root || !inst.getTaskByIndex) return undefined
    const area = root.querySelector('.gantt_data_area') ?? root.querySelector('.gantt_task_bg')
    if (!area) return undefined
    const top = area.getBoundingClientRect().top
    const scrollY = inst.getScrollState?.().y ?? 0
    const rowH = Number(inst.config.row_height) || 1
    const idx = Math.floor((this.lastPointerY - top + scrollY) / rowH)
    if (idx < 0) return undefined
    const lane = inst.getTaskByIndex(idx)
    return lane?.id != null && String(lane.id).startsWith('lane:') ? lane : undefined
  }

  /** 拖拽预览:在目标泳道、指针所在时间位置画一个卡片大小的虚影(松手前可见落点的「行+时间」)。 */
  private updateDropHint(inst: DhxGantt): void {
    const root = this.container
    const hint = this.dropHint
    if (!root || !hint) return
    const lane = this.laneAtPointer(inst)
    const row = lane?.id
      ? (root.querySelector(`.gantt_task_row[task_id="${String(lane.id)}"]`) as HTMLElement | null)
      : null
    const area = root.querySelector('.gantt_data_area') as HTMLElement | null
    const srcBar = this.dragTaskId ? this.barEl(this.dragTaskId) : null
    // 在取消区上方时,不显示泳道虚影(取消区自身高亮即可)。
    if (!row || !area || !srcBar || this.overCancel) {
      hint.style.display = 'none'
      return
    }
    const cRect = root.getBoundingClientRect()
    const rRect = row.getBoundingClientRect()
    const aRect = area.getBoundingClientRect()
    // 虚影随指针:横向 = 新时间(保留抓取点),纵向 = 目标泳道。夹在时间区内。
    const w = Math.max(96, Math.round(srcBar.getBoundingClientRect().width))
    const left = Math.min(
      Math.max(this.lastPointerX - this.dragGrabOffX, aRect.left),
      aRect.right - w,
    )
    const newStart = this.timeAtScreenX(inst, left, aRect)
    const laneName = lane?.nerv?.text ?? ''
    const same = String(lane?.id) === String(this.dragOrigLaneId)
    const timeStr = newStart ? this.fmtHm(newStart) : ''
    const tip = hint.querySelector('.nerv-drop-tip')
    if (tip) tip.textContent = (same ? '调整到 ' : `改派 ${laneName} · `) + timeStr
    hint.style.display = 'flex'
    hint.style.top = `${rRect.top - cRect.top + 6}px`
    hint.style.left = `${left - cRect.left}px`
    hint.style.width = `${w}px`
    hint.style.height = `${rRect.height - 12}px`
  }

  /** 选中某工序时,显示其所属工单各工序之间的连线(跨泳道);切换选中即换。 */
  private showOrderLinks(taskId?: string): void {
    const g = this.gantt
    if (!g) return
    const hadShown = this.shownLinkIds.length > 0
    for (const id of this.shownLinkIds) {
      if (!g.isLinkExists || g.isLinkExists(id)) g.deleteLink?.(id)
    }
    this.shownLinkIds = []
    // 工单甘特:连线由 config 常显,选中/拖拽后**不需要**整表 render(render 会造成明显闪动,
    // 用户反馈"原版不闪、我这闪"的根因)。仅当上次确实加过临时连线时才重绘一次做清理。
    if (this.options.view !== 'resource') {
      if (hadShown) g.render()
      return
    }
    const task = this.model?.tasks.find((t) => t.id === taskId)
    if (!task) {
      if (hadShown) g.render()
      return
    }
    const orderIds = new Set(
      this.model!.tasks.filter((t) => t.orderId === task.orderId && t.type === 'operation').map(
        (t) => t.id,
      ),
    )
    for (const l of this.model?.links ?? []) {
      if (
        orderIds.has(l.source) &&
        orderIds.has(l.target) &&
        g.isTaskExists?.(l.source) &&
        g.isTaskExists?.(l.target)
      ) {
        const id = `sel:${l.id}`
        g.addLink?.({ id, source: l.source, target: l.target, type: '0' })
        this.shownLinkIds.push(id)
      }
    }
    g.render()
  }

  /** 即时标记选中条(点击不触发重绘,task_class 不会重算,需手动加类)。 */
  private markSelectedBar(taskId: string): void {
    const root = this.container
    if (!root) return
    root
      .querySelectorAll('.gantt_task_line.nerv-selected')
      .forEach((el) => el.classList.remove('nerv-selected'))
    this.barEl(taskId)?.classList.add('nerv-selected')
  }

  /** 按 task_id 取条形元素。 */
  private barEl(id: string): HTMLElement | null {
    return (
      (this.container?.querySelector(`.gantt_task_line[task_id="${id}"]`) as HTMLElement | null) ??
      null
    )
  }

  /** 屏幕 X(虚影左缘)→ 时间(经数据区偏移与水平滚动换算)。 */
  private timeAtScreenX(inst: DhxGantt, screenLeft: number, aRect: DOMRect): Date | undefined {
    if (!inst.dateFromPos) return undefined
    const scrollX = inst.getScrollState?.().x ?? 0
    const d = inst.dateFromPos(screenLeft - aRect.left + scrollX)
    return d && !Number.isNaN(d.getTime()) ? d : undefined
  }

  private fmtHm(d: Date): string {
    const task = this.model?.tasks.find((task) => task.id === this.dragTaskId)
    return scheduleTime(d.toISOString(), task?.timezone, { hour: '2-digit', minute: '2-digit' })
  }

  /** 自定义拖拽提交:按虚影位置算出新时间(横向)+ 目标泳道(纵向),写回任务后走统一上报。 */
  private commitCustomDrag(inst: DhxGantt, id: string): void {
    const t = inst.getTask(id)
    const area = this.container?.querySelector('.gantt_data_area') as HTMLElement | null
    const srcBar = this.barEl(id)
    if (t && area && srcBar && this.dragOrigStart && this.dragOrigEnd) {
      const aRect = area.getBoundingClientRect()
      const w = srcBar.getBoundingClientRect().width
      const left = Math.min(
        Math.max(this.lastPointerX - this.dragGrabOffX, aRect.left),
        aRect.right - w,
      )
      const newStart = this.timeAtScreenX(inst, left, aRect)
      if (newStart) {
        const dur = this.dragOrigEnd.getTime() - this.dragOrigStart.getTime()
        t.start_date = newStart
        t.end_date = new Date(newStart.getTime() + dur)
      }
    }
    this.emitDrag(inst, id, 'move')
    this.scheduleSettle(id)
  }

  /**
   * 落定回弹:提交后上层会 setData 重建卡片。等下一帧新条(按 task_id)出现,加一次性
   * .nerv-card-settle 入场类,animationend 后移除。reduced-motion 跳过。
   */
  private scheduleSettle(id: string): void {
    if (prefersReducedMotion()) return
    cancelAnimationFrame(this.settleRaf)
    // 双 rAF:第一帧让 emit→setData 的重建落地,第二帧新条已在 DOM,再加类触发入场。
    this.settleRaf = requestAnimationFrame(() => {
      this.settleRaf = requestAnimationFrame(() => {
        const bar = this.barEl(id)
        if (bar) playOnceClass(bar, 'nerv-card-settle')
      })
    })
  }

  /** 锁定块拖拽反馈:抖动该 bar(reduced-motion 跳过)并上报 lockedDragAttempt。 */
  private signalLockedDrag(taskId: string, bar: HTMLElement): void {
    if (!prefersReducedMotion()) playOnceClass(bar, 'nerv-lock-deny')
    this.emit('lockedDragAttempt', { taskId })
  }

  /** Esc 中止自定义拖拽:恢复原块与原时间、隐藏虚影/取消区,不上报 dragEnd。 */
  private cancelCustomDrag(): void {
    const id = this.dragTaskId
    if (id) {
      this.barEl(id)?.classList.remove('nerv-drag-source')
      const t = this.gantt?.getTask(id)
      if (t) {
        if (this.dragOrigStart) t.start_date = new Date(this.dragOrigStart)
        if (this.dragOrigEnd) t.end_date = new Date(this.dragOrigEnd)
      }
    }
    this.suppressNextClick = true // 中止后紧随的 mouseup 会被当点击,抑制之
    this.hideDropHint()
  }

  private hideDropHint(): void {
    this.dragging = false
    this.dragTaskId = undefined
    this.dragOrigStart = undefined
    this.dragOrigEnd = undefined
    this.dragOrigLaneId = undefined
    this.overCancel = false
    if (this.dropHint) this.dropHint.style.display = 'none'
    if (this.cancelZone) {
      this.cancelZone.style.display = 'none'
      this.cancelZone.classList.remove('nerv-drop-cancel-hot')
    }
  }

  private emitDrag(inst: DhxGantt, taskId: string, mode: string): void {
    // 落在取消区:撤销改派,工序回到原泳道与原时间,不上报。
    if (this.options.view === 'resource' && this.overCancel && mode === 'move') {
      const t = inst.getTask(taskId)
      if (t) {
        if (this.dragOrigStart) t.start_date = new Date(this.dragOrigStart)
        if (this.dragOrigEnd) t.end_date = new Date(this.dragOrigEnd)
        inst.updateTask?.(taskId)
      }
      this.hideDropHint()
      return
    }
    this.hideDropHint()
    const task = inst.getTask(taskId)
    const src = this.model?.tasks.find((t) => t.id === taskId)
    // 注意:src 与 task.nerv 是同一对象,改派前先存原 resourceId,否则 kind 判定会被自身改写干扰。
    const originalResourceId = src?.resourceId
    let reassigned = false
    // 资源视图跨泳道拖拽 = 改派:按落点找目标泳道,更新该任务在当前分组维度的归属。
    // (split 模式下不能只 updateTask 跨行;更新归属后让上层重新 parse 才会落到新泳道。)
    if (this.options.view === 'resource' && task?.nerv && mode !== 'resize') {
      const lane = this.laneAtPointer(inst)
      if (lane && String(lane.id) !== String(task.parent)) {
        const laneResId = String(lane.id).slice(5)
        const dim = this.options.groupBy || 'workCenter'
        task.nerv.dimensions = {
          ...(task.nerv.dimensions ?? {}),
          [dim]: { id: laneResId, label: String(lane.text ?? laneResId) },
        }
        if (dim === 'workCenter') {
          task.nerv.resourceId = laneResId
          task.nerv.workCenterId = laneResId
        }
        task.parent = String(lane.id)
        task.$resource = laneResId
        reassigned = true
      }
    }
    const parent = task?.parent != null ? String(task.parent) : undefined
    const reassignedResource = parent?.startsWith('lane:') ? parent.slice(5) : task?.$resource
    const kind =
      mode === 'resize'
        ? 'resize'
        : reassigned || (reassignedResource && reassignedResource !== originalResourceId)
          ? 'reassign'
          : 'move'
    const start = task?.start_date?.toISOString() ?? src?.startUtc ?? ''
    const end = task?.end_date?.toISOString() ?? src?.endUtc ?? ''
    // 防御:零/负时长时不上报(避免把条收成一条线)。
    if (start && end && Date.parse(end) <= Date.parse(start)) return
    const payload = {
      taskId,
      operationId: src?.operationId ?? taskId,
      resourceId:
        mode === 'resize' ? originalResourceId : (reassignedResource ?? originalResourceId),
      startUtc: start,
      endUtc: end,
      kind: kind as 'move' | 'resize' | 'reassign',
    }
    // 关键:延后到 DHTMLX 完成本次拖拽后再上报。同步上报会触发上层 setData →
    // clearAll()+parse() 在拖拽回调内重建数据,破坏拖拽中的条(收成一条线)。
    setTimeout(() => this.emit('taskDragEnd', payload), 0)
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

  /** 计划基线层:在实际条后画半透明"计划"条(plannedStart/End),偏移即见计划/实际差。 */
  private addBaselineLayer(inst: DhxGantt): void {
    if (!inst.addTaskLayer || !inst.getTaskPosition) return
    inst.addTaskLayer((task: DhxTask) => {
      if (!task.planned_start || !task.planned_end || task.nerv?.type !== 'operation') return false
      const pos = inst.getTaskPosition!(task, task.planned_start, task.planned_end)
      const barH = Number(inst.config.bar_height) || pos.height
      const rowH = Number(inst.config.row_height) || pos.height
      const offset = Math.max(0, (rowH - barH) / 2)
      const el = document.createElement('div')
      el.className = 'nerv-baseline'
      if (task.nerv?.colorKey)
        el.style.setProperty('--bl', `var(--nv-scheduling-category-${task.nerv.colorKey})`)
      el.style.left = `${pos.left}px`
      el.style.top = `${pos.top + offset}px`
      el.style.width = `${Math.max(3, pos.width)}px`
      el.style.height = `${barH}px`
      return el
    })
  }
}
