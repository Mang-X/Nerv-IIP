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
import { conflictReasonLabel } from '../../model/labels'
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
  getTaskByIndex?: (index: number) => DhxTask | undefined
  updateTask?: (id: string | number) => void
  getScrollState?: () => { x: number; y: number }
  dateFromPos?: (x: number) => Date
  render: () => void
  setSizes?: () => void
  destructor?: () => void
  showDate?: (date: Date) => void
  addMarker?: (marker: Record<string, unknown>) => string
  deleteMarker?: (id: string) => void
  addLink?: (link: Record<string, unknown>) => string
  deleteLink?: (id: string | number) => void
  isLinkExists?: (id: string | number) => boolean
  addTaskLayer?: (renderer: (task: DhxTask) => HTMLElement | boolean) => string
  getTaskPosition?: (
    task: DhxTask,
    start?: Date,
    end?: Date,
  ) => { left: number; top: number; width: number; height: number }
  date?: { add: (d: Date, n: number, unit: string) => Date }
}
interface DhxTask {
  id: string
  text?: string
  start_date?: Date
  end_date?: Date
  planned_start?: Date
  planned_end?: Date
  parent?: string | number
  $resource?: string
  nerv?: ScheduleTask
}

interface GridTask {
  type?: string
  start_date?: Date
  end_date?: Date
  nerv?: ScheduleTask
}

const PRIORITY_CELL: Record<string, [string, string, string]> = {
  high: ['nerv-prio-high', '↑', '高'],
  medium: ['nerv-prio-mid', '—', '中'],
  low: ['nerv-prio-low', '↓', '低'],
}

interface LaneTask extends GridTask {
  kpi?: { utilization?: number; oee?: number; changeoverCount?: number; materialRisk?: number }
}

// 左侧列1:资源名(+瓶颈标)。列头随所选维度变(工作中心/设备/班组/产线)。
function laneNameCell(t: LaneTask): string {
  const name = t.nerv?.text ?? ''
  const over = (t.kpi?.utilization ?? 0) > 1
  return `<div class="nerv-lane-id"><span class="nerv-lane-name">${name}</span>${over ? '<span class="nerv-lane-tag">瓶颈</span>' : ''}</div>`
}
// 左侧列2:产能指标(利用率 / OEE / 切换 / 待料)。
function laneKpiCell(t: LaneTask): string {
  const k = t.kpi
  if (!k) return ''
  const util = Math.round((k.utilization ?? 0) * 100)
  const oee = Math.round((k.oee ?? 0) * 100)
  const over = util > 100
  const co = k.changeoverCount ?? 0
  const risk = k.materialRisk ?? 0
  return `<div class="nerv-lane-kpis">
    <span class="nerv-lane-kpi"><i>利用率</i><b class="${over ? 'nerv-over' : ''}">${util}%</b></span>
    <span class="nerv-lane-kpi"><i>OEE</i><b>${oee}%</b></span>
    <span class="nerv-lane-kpi"><i>切换</i><b>${co}</b></span>
    <span class="nerv-lane-kpi"><i>待料</i><b class="${risk ? 'nerv-warn' : ''}">${risk}</b></span>
  </div>`
}

const GRID_COLUMNS = (view: 'order' | 'resource', dimLabel = '工作中心') => {
  if (view === 'resource') {
    return [
      { name: 'text', label: dimLabel, tree: true, width: 128, resize: true, template: laneNameCell },
      { name: 'kpi', label: '产能指标', width: 124, resize: true, template: laneKpiCell },
    ]
  }
  const name = { name: 'text', label: '任务名称', tree: true, width: 196, resize: true }
  return [
    name,
    { name: 'owner', label: '负责人', align: 'center', width: 72, template: ownerCell },
    { name: 'priority', label: '优先级', align: 'center', width: 66, template: priorityCell },
    { name: 'status', label: '状态', align: 'center', width: 80, template: statusCell },
    { name: 'duration', label: '工时', align: 'center', width: 52, template: durationLabel },
    { name: 'progress', label: '进度', align: 'center', width: 98, template: progressCell },
  ]
}

function durationLabel(t: GridTask): string {
  if (t.type === 'project' || !t.start_date || !t.end_date) return ''
  const h = Math.round((t.end_date.getTime() - t.start_date.getTime()) / 3_600_000)
  return h >= 1 ? `${h}h` : '<1h'
}
function ownerCell(t: GridTask): string {
  return t.type === 'project' ? '' : t.nerv?.owner ?? '—'
}
function priorityCell(t: GridTask): string {
  const p = t.nerv?.priority
  if (t.type === 'project' || !p) return ''
  const [cls, arrow, label] = PRIORITY_CELL[p]
  return `<span class="nerv-prio ${cls}">${arrow} ${label}</span>`
}
function statusCell(t: GridTask): string {
  const s = t.nerv?.status
  if (t.type === 'project' || !s) return ''
  return `<span class="nerv-st"><i class="nerv-dot nerv-dot-${s.tone}"></i>${s.label}</span>`
}
function progressCell(t: GridTask): string {
  if (t.type === 'project' || t.nerv?.type !== 'operation') return ''
  const pct = Math.round((t.nerv?.progress ?? 0) * 100)
  return `<span class="nerv-pcell"><span class="nerv-pbar"><span style="width:${pct}%"></span></span><span class="nerv-ptext">${pct}%</span></span>`
}

// 统一用 lucide 图标(与项目图标包一致):lock / zap(插单)/ triangle-alert(冲突)。
const lucide = (paths: string, size = 11) =>
  `<svg class="nerv-ic" viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`
const LOCK_SVG = lucide('<rect width="18" height="11" x="3" y="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>')
const RUSH_ICON = lucide(
  '<path d="M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z"/>',
)
const ALERT_ICON = lucide(
  '<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4"/><path d="M12 17h.01"/>',
  12,
)
const fmtMd = (iso: string) => {
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : `${d.getMonth() + 1}-${String(d.getDate()).padStart(2, '0')}`
}

const BLOCK_LABEL: Record<NonNullable<ScheduleTask['blockKind']>, string> = {
  maintenance: '设备维护',
  downtime: '计划停机',
  lineChange: '换线窗口',
  changeover: '换型窗口',
}
/** 资源时间块(维护/停机/换线/换型)背景带的低调内标签(与格子融合,非卡片)。 */
function blockLabelHtml(t: ScheduleTask): string {
  const fmtHm = (iso: string) => {
    const d = new Date(iso)
    return Number.isNaN(d.getTime()) ? '' : `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  }
  const span = t.startUtc && t.endUtc ? `${fmtHm(t.startUtc)}-${fmtHm(t.endUtc)}` : ''
  return `<span class="nerv-block-bg-label">${BLOCK_LABEL[t.blockKind!]}${span ? ` · ${span}` : ''}</span>`
}

/** 资源排产板工单卡片(条内 HTML)。布局对齐参考图:WO+优先级+插单+锁 / 产品·工序 / 数量·交期 / 换型·占用 / 齐套。 */
function cardHtml(t: ScheduleTask): string {
  const prio = t.priority
    ? `<span class="nerv-card-prio nerv-prio-${t.priority}">${PRIORITY_CELL[t.priority][2]}</span>`
    : ''
  const rush = t.isRush ? `<span class="nerv-card-rush" title="插单">${RUSH_ICON}</span>` : ''
  const lock = t.locked ? `<span class="nerv-card-lock" title="已锁定">${LOCK_SVG}</span>` : ''
  const due = t.dueUtc ? fmtMd(t.dueUtc) : ''
  const kit = t.kitting != null ? Math.round(t.kitting * 100) : null
  const kitCls = kit == null ? '' : kit >= 100 ? 'ok' : kit >= 80 ? 'warn' : 'bad'
  const co = t.changeoverMin ? `换型 ${t.changeoverMin}m` : ''
  const load = t.load != null ? `占用 ${Math.round(t.load * 100)}%` : ''
  const meta3 = [co, load].filter(Boolean).join('　')
  // 冲突徽标:与优先级/插单/锁并排在首行(不再悬浮角标)。
  const alert = t.hasConflict ? `<span class="nerv-card-alert" title="冲突">${ALERT_ICON}</span>` : ''
  const prog =
    t.progress != null
      ? `<span class="nerv-card-prog"><span style="width:${Math.round(t.progress * 100)}%"></span></span>`
      : ''
  return `<div class="nerv-card">
    <div class="nerv-card-r1"><span class="nerv-card-wo">${t.orderId}</span><span class="nerv-card-meta">${prio}${rush}${alert}${lock}</span></div>
    <div class="nerv-card-r2">${t.product ?? ''}<span class="nerv-card-op"> · ${t.operationId}</span></div>
    <div class="nerv-card-r3">${t.quantity != null ? `数量 ${t.quantity}` : ''}${due ? `　交期 ${due}` : ''}</div>
    <div class="nerv-card-r3">${meta3}</div>
    <div class="nerv-card-tags">${kit != null ? `<span class="nerv-kit nerv-kit-${kitCls}">齐套 ${kit}%</span>` : ''}</div>
    ${prog}
  </div>`
}

const WEEKDAY = ['周日', '周一', '周二', '周三', '周四', '周五', '周六']
const fmtDayLong = (d: Date) => `${d.getMonth() + 1}月${d.getDate()}日 ${WEEKDAY[d.getDay()]}`
const fmtHour = (d: Date) => `${String(d.getHours()).padStart(2, '0')}:00`
// 班次带(资源排产板):8h 三班制 — 夜班 00-08 / 早班 08-16 / 中班 16-24。
const shiftLabel = (d: Date) => {
  const h = d.getHours()
  return h < 8 ? '夜班 00–08' : h < 16 ? '早班 08–16' : '中班 16–24'
}
const shiftCss = (d: Date) => (d.getHours() < 8 || d.getHours() >= 16 ? 'nerv-shift nerv-shift-dim' : 'nerv-shift')
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

/** 任务块 tooltip 的 HTML(DHTMLX 插件与资源板自绘 tip 共用同一内容)。 */
function tooltipHtml(t: ScheduleTask): string {
  const prio = t.priority ? { high: '高', medium: '中', low: '低' }[t.priority] : ''
  const pct = (v?: number) => (v == null ? '' : `${Math.round(v * 100)}%`)
  const chip = (txt: string, tone: string) =>
    `<span style="font-size:11px;font-weight:600;padding:0 6px;border-radius:4px;color:${tone};background:color-mix(in oklch,${tone},transparent 86%)">${txt}</span>`
  const badges = [
    prio ? chip(`${prio}优先`, 'var(--destructive)') : '',
    t.isRush ? chip('插单', 'oklch(0.7 0.17 60)') : '',
    t.locked ? chip('已锁定', 'var(--brand)') : '',
    t.hasConflict ? chip('冲突', 'var(--destructive)') : '',
  ]
    .filter(Boolean)
    .join('')
  const head = `<div style="font-weight:700;font-size:13px;letter-spacing:.01em;margin-bottom:6px;padding-bottom:6px;border-bottom:1px solid color-mix(in oklch,var(--foreground),transparent 88%);display:flex;align-items:center;gap:6px;flex-wrap:wrap"><span>${t.orderId}</span>${badges}</div>`
  const rows: Array<[string, string]> = [
    ['工序', t.text || '—'],
    ...(t.product ? ([['产品', t.product]] as Array<[string, string]>) : []),
    ...(t.resourceId ? ([['资源', t.resourceId]] as Array<[string, string]>) : []),
    ...(t.owner ? ([['负责人', t.owner]] as Array<[string, string]>) : []),
    ['起止', `${fmt(t.startUtc)} → ${fmt(t.endUtc)}`],
    ...(t.quantity != null ? ([['数量', String(t.quantity)]] as Array<[string, string]>) : []),
    ...(t.dueUtc ? ([['交期', fmt(t.dueUtc)]] as Array<[string, string]>) : []),
    ...(t.kitting != null ? ([['齐套', pct(t.kitting)]] as Array<[string, string]>) : []),
    ...(t.changeoverMin ? ([['换型', `${t.changeoverMin} 分钟`]] as Array<[string, string]>) : []),
    ...(t.load != null ? ([['占用', pct(t.load)]] as Array<[string, string]>) : []),
    ...(t.status ? ([['状态', t.status.label]] as Array<[string, string]>) : []),
    ...(t.hasConflict && t.conflictReason
      ? ([['冲突', conflictReasonLabel[t.conflictReason]]] as Array<[string, string]>)
      : []),
  ]
  const body = rows
    .map(
      ([k, v]) =>
        `<div style="display:flex;gap:10px;justify-content:space-between"><span style="opacity:.7">${k}</span><span>${v}</span></div>`,
    )
    .join('')
  return head + body
}

/** 用户是否偏好减少动效(所有 JS 动效在此守卫下降级为直接生效、无过渡)。 */
function prefersReducedMotion(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  )
}

/** 一次性动画类:加类前强制回流以重启动画,animationend 后自动移除(可重复触发)。 */
function playOnceClass(el: HTMLElement, cls: string): void {
  el.classList.remove(cls)
  void el.offsetWidth // 强制回流,确保下次加类能重新触发关键帧
  el.classList.add(cls)
  const done = () => {
    el.classList.remove(cls)
    el.removeEventListener('animationend', done)
  }
  el.addEventListener('animationend', done)
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
  private lastPointerY = 0
  private lastPointerX = 0
  private pointerMove?: (e: MouseEvent) => void
  private pointerDown?: (e: MouseEvent) => void
  private pointerUp?: (e: MouseEvent) => void
  private keyDown?: (e: KeyboardEvent) => void
  private tipEl?: HTMLElement
  private tipTaskId?: string
  private tipRaf = 0
  private tipPendingX = 0
  private tipPendingY = 0
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
  /** 资源时间块:泳道 id → 时段窗口列表,供 timeline_cell_class 给单元格上底纹(块不作任务条)。 */
  private blockCells = new Map<string, { start: number; end: number; kind: string }[]>()
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
      const tip = document.createElement('div')
      tip.className = 'nerv-tip'
      tip.style.position = 'fixed'
      tip.style.left = '0'
      tip.style.top = '0'
      tip.style.zIndex = '60'
      tip.style.pointerEvents = 'none'
      tip.style.opacity = '0'
      tip.style.display = 'none'
      if (!prefersReducedMotion()) tip.style.transition = 'opacity 120ms var(--nerv-ease, ease)'
      document.body.appendChild(tip)
      this.tipEl = tip

      // 自定义拖拽:DHTMLX 原生 move 已关(见 configure)。原块静止,虚影随指针(横向=改时间、
      // 纵向=改泳道),松手于泳道提交、松手于取消区撤销。彻底解决"原块漂"与"横向失效"。
      let downX = 0
      let downY = 0
      this.pointerDown = (e: MouseEvent) => {
        if (e.button !== 0) return
        const bar = (e.target as HTMLElement)?.closest?.('.gantt_task_line') as HTMLElement | null
        if (!bar || !bar.querySelector('.nerv-card')) return
        const id = bar.getAttribute('task_id')
        const t = id ? inst.getTask(id) : undefined
        if (!id || !t) return
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
        this.updateTip(inst, e)
        if (!this.dragTaskId) return
        if (!this.dragging) {
          if (Math.abs(e.clientX - downX) < 5 && Math.abs(e.clientY - downY) < 5) return
          this.dragging = true
          this.suppressNextClick = true // 拖拽一旦开始,抑制随后的点击(避免弹详情)
          this.hideTip() // 拖拽进行中不显 tooltip(落点虚影已给足反馈)
          if (this.cancelZone) this.cancelZone.style.display = 'flex'
          this.barEl(this.dragTaskId)?.classList.add('nerv-drag-source')
        }
        const cr = this.cancelZone?.getBoundingClientRect()
        this.overCancel =
          !!cr && e.clientX >= cr.left && e.clientX <= cr.right && e.clientY >= cr.top && e.clientY <= cr.bottom
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
    g.parse(this.toGanttData(model))
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
        if (command.kind === 'focusConflict') this.emit('conflictClicked', { taskId: command.taskId })
        break
      case 'setReadOnly':
        this.options.readOnly = command.readOnly
        if (g) {
          const nativeDrag = !command.readOnly && this.options.view !== 'resource'
          g.config.readonly = command.readOnly
          g.config.drag_move = nativeDrag
          g.config.drag_resize = nativeDrag
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
    if (this.pointerDown && this.container) this.container.removeEventListener('mousedown', this.pointerDown)
    if (this.keyDown) document.removeEventListener('keydown', this.keyDown)
    this.pointerMove = undefined
    this.pointerUp = undefined
    this.pointerDown = undefined
    this.keyDown = undefined
    this.dropHint?.remove()
    this.dropHint = undefined
    this.cancelZone?.remove()
    this.cancelZone = undefined
    this.tipEl?.remove()
    this.tipEl = undefined
    this.tipTaskId = undefined
    this.dragging = false
    cancelAnimationFrame(this.tipRaf)
    this.tipRaf = 0
    cancelAnimationFrame(this.settleRaf)
    cancelAnimationFrame(this.resizeRaf)
    this.resizeObserver?.disconnect()
    this.resizeObserver = undefined
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
    g.config.scales = this.scalesFor()
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

  /** 当前刻度配置。资源排产板在小时刻度下插入「班次带」(日期 / 班次 / 小时)。 */
  private scalesFor(): Array<Record<string, unknown>> {
    const scale = this.resolveScale()
    const base = SCALE_CONFIG[scale]
    if (this.options.view === 'resource' && scale === 'hour') {
      return [base[0], { unit: 'hour', step: 8, format: shiftLabel, css: shiftCss }, base[1]]
    }
    return base
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
    this.markerId = g.addMarker({ start_date: this.nowDate(), css: 'nerv-today-marker', text: '现在' })
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
    // 资源排产板用自定义拖拽(原块静止 + 虚影随指针);关闭 DHTMLX 原生 move/resize 以免冲突。
    const nativeDrag = !options.readOnly && options.view !== 'resource'
    c.drag_move = nativeDrag
    c.drag_resize = nativeDrag
    c.drag_links = !options.readOnly && options.view === 'order'
    c.drag_progress = false
    // 网格内拖拽换分支暂时关闭(onAfterTaskMove 易误触、破坏拖拽);改派后续用时间线跨行拖拽实现。
    c.order_branch = false
    c.order_branch_free = false
    c.open_split_tasks = false // split 分组行:同组工序铺在一行,不展开成多行
    c.row_height = options.view === 'resource' ? 128 : 48
    c.bar_height = options.view === 'resource' ? 112 : 22
    c.grid_width = options.view === 'resource' ? 258 : 560
    c.grid_resize = true
    // 默认不画连线;资源板仅在「选中某工序」时动态显示该工单的工序连线(showOrderLinks)。
    c.show_links = true
    c.highlight_critical_path = options.view === 'order'
    c.columns = GRID_COLUMNS(options.view, this.resolveDimLabel())
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
      // 资源时间块不作任务条(见 toGanttData 已排除),改由 timeline_cell_class 给单元格上底纹。
      if (t?.type === 'order') cls.push('nerv-order')
      if (t?.colorKey && !t?.blockKind) cls.push(`nerv-cat-${t.colorKey}`)
      if (t?.hasConflict) cls.push('nerv-conflict')
      if (t?.locked) cls.push('nerv-locked')
      if (t?.id === this.selectedTaskId) cls.push('nerv-selected')
      return cls.join(' ')
    }
    inst.templates.grid_row_class = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask }) =>
      task.nerv?.hasConflict ? 'nerv-row-conflict' : ''
    inst.templates.tooltip_text = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask; text?: string }) =>
      task.nerv ? tooltipHtml(task.nerv) : task.text ?? ''
    const isResource = options.view === 'resource'
    // 资源排产板:条内渲染工单卡片;工单甘特:条内不渲染,工序名放右侧。
    inst.templates.task_text = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask }) => {
      const t = task.nerv
      if (!isResource || t?.type !== 'operation') return ''
      return cardHtml(t)
    }
    inst.templates.rightside_text = (_s: unknown, _e: unknown, task: { nerv?: ScheduleTask; text?: string }) => {
      if (isResource) return ''
      const t = task.nerv
      if (t?.type !== 'operation') return ''
      const lock = t.locked ? `<span class="nerv-card-lock">${LOCK_SVG}</span>` : ''
      return `<span class="nerv-bar-label">${task.text ?? ''}${lock}</span>`
    }
    // 时间线底纹:只两态——工作(原色,无 class)/ 非工作(周末或 20:00–08:00 夜间,统一 nerv-offwork)。
    // 资源时间块(维护/停机/换线/换型)也走单元格底纹:该泳道在此时段有块 → 叠加 nerv-cell-block(与
    // 日历同实现,恒在卡片之下、与格子融为一体、绝不覆盖)。
    inst.templates.timeline_cell_class = (task: { id?: string | number }, date: Date) => {
      const classes: string[] = []
      const day = date.getDay()
      const h = date.getHours()
      if (day === 0 || day === 6 || h < 8 || h >= 20) classes.push('nerv-offwork')
      if (this.options.view === 'resource' && this.blockCells.size) {
        const laneId = typeof task?.id === 'string' ? task.id.replace(/^lane:/, '') : ''
        const wins = this.blockCells.get(laneId)
        if (wins) {
          const ts = date.getTime()
          for (const w of wins) {
            if (ts >= w.start && ts < w.end) {
              classes.push('nerv-cell-block', `nerv-cell-block-${w.kind}`)
              break
            }
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
      inst.attachEvent('onAfterTaskDrag', (id, mode) => this.emitDrag(inst, String(id), String(mode))),
    )
    this.eventIds.push(
      inst.attachEvent('onAfterTaskMove', (id) => this.emitDrag(inst, String(id), 'move')),
    )
    // 资源视图拖拽走自定义实现(见 mount 的 pointerDown/Move/Up);此处不接 DHTMLX 原生拖拽。
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
    const left = Math.min(Math.max(this.lastPointerX - this.dragGrabOffX, aRect.left), aRect.right - w)
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
      this.model!.tasks.filter((t) => t.orderId === task.orderId && t.type === 'operation').map((t) => t.id),
    )
    for (const l of this.model?.links ?? []) {
      if (orderIds.has(l.source) && orderIds.has(l.target) && g.isTaskExists?.(l.source) && g.isTaskExists?.(l.target)) {
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
    root.querySelectorAll('.gantt_task_line.nerv-selected').forEach((el) => el.classList.remove('nerv-selected'))
    this.barEl(taskId)?.classList.add('nerv-selected')
  }

  /** 按 task_id 取条形元素。 */
  private barEl(id: string): HTMLElement | null {
    return (this.container?.querySelector(`.gantt_task_line[task_id="${id}"]`) as HTMLElement | null) ?? null
  }

  /** 屏幕 X(虚影左缘)→ 时间(经数据区偏移与水平滚动换算)。 */
  private timeAtScreenX(inst: DhxGantt, screenLeft: number, aRect: DOMRect): Date | undefined {
    if (!inst.dateFromPos) return undefined
    const scrollX = inst.getScrollState?.().x ?? 0
    const d = inst.dateFromPos(screenLeft - aRect.left + scrollX)
    return d && !Number.isNaN(d.getTime()) ? d : undefined
  }

  private fmtHm(d: Date): string {
    return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  }

  /** 自定义拖拽提交:按虚影位置算出新时间(横向)+ 目标泳道(纵向),写回任务后走统一上报。 */
  private commitCustomDrag(inst: DhxGantt, id: string): void {
    const t = inst.getTask(id)
    const area = this.container?.querySelector('.gantt_data_area') as HTMLElement | null
    const srcBar = this.barEl(id)
    if (t && area && srcBar && this.dragOrigStart && this.dragOrigEnd) {
      const aRect = area.getBoundingClientRect()
      const w = srcBar.getBoundingClientRect().width
      const left = Math.min(Math.max(this.lastPointerX - this.dragGrabOffX, aRect.left), aRect.right - w)
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

  /** 自绘 tooltip:悬停任务块时跟指针平滑移动;不在块上或拖拽中则隐藏。 */
  private updateTip(inst: DhxGantt, e: MouseEvent): void {
    const tip = this.tipEl
    if (!tip) return
    if (this.dragging) {
      this.hideTip()
      return
    }
    const bar = (e.target as HTMLElement)?.closest?.('.gantt_task_line') as HTMLElement | null
    // 只处理本实例容器内的条:页面可能有多个实例,document 级 mousemove 会让每个实例都触发;
    // 用别的实例的 task_id 调本实例 getTask 会抛 "Task not found"(DHTMLX getTask 对缺失 id 抛错),
    // 每帧抛异常还会拖垮性能(tooltip/拖拽卡顿的元凶)。先按容器归属过滤,再按存在性取 task。
    if (!bar || !this.container?.contains(bar)) {
      this.hideTip()
      return
    }
    const id = bar.getAttribute('task_id') ?? undefined
    const t = id && inst.isTaskExists?.(id) ? inst.getTask(id) : undefined
    if (!id || !t?.nerv || t.nerv.type !== 'operation' || t.nerv.blockKind) {
      this.hideTip()
      return
    }
    // 内容一次构建(切换任务时才重建),移动时只改 transform,避免重排。
    if (this.tipTaskId !== id) {
      tip.innerHTML = tooltipHtml(t.nerv)
      this.tipTaskId = id
    }
    tip.style.display = 'block'
    // pointermove 高频:把位置写入合并到每帧一次 rAF(避免每个事件都触发合成),用 translate 定位(GPU 层,不触发 layout)。
    this.tipPendingX = e.clientX + 14
    this.tipPendingY = e.clientY + 18
    if (!this.tipRaf) {
      this.tipRaf = requestAnimationFrame(() => {
        this.tipRaf = 0
        const el = this.tipEl
        if (!el || el.style.display === 'none') return
        el.style.transform = `translate(${Math.round(this.tipPendingX)}px, ${Math.round(this.tipPendingY)}px)`
        // 首帧就位后再淡入(display:none → block 时 transition 不生效,需分帧)。
        if (el.style.opacity !== '1') el.style.opacity = '1'
      })
    }
  }

  private hideTip(): void {
    cancelAnimationFrame(this.tipRaf)
    this.tipRaf = 0
    const tip = this.tipEl
    if (!tip || tip.style.display === 'none') return
    tip.style.opacity = '0'
    tip.style.display = 'none'
    this.tipTaskId = undefined
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
      resourceId: reassignedResource ?? src?.resourceId,
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
      if (task.nerv?.colorKey) el.style.setProperty('--bl', `var(--nerv-cat-${task.nerv.colorKey})`)
      el.style.left = `${pos.left}px`
      el.style.top = `${pos.top + offset}px`
      el.style.width = `${Math.max(3, pos.width)}px`
      el.style.height = `${barH}px`
      return el
    })
  }

  private toGanttData(model: ScheduleModel): { data: unknown[]; links: unknown[] } {
    const toDate = (isoVal: string) => (isoVal ? new Date(isoVal) : undefined)
    const data: unknown[] = []

    if (this.options.view === 'resource') {
      // 一资源(所选维度)一泳道:分组行用 split task,同组工序铺在它那一行。里程碑不入资源板。
      const dim = this.options.groupBy || 'workCenter'
      const ops = model.tasks.filter((t) => t.type === 'operation' && !t.isMilestone && !t.blockKind)
      // 资源时间块不作任务条:改为按泳道+时段给时间线「单元格」上底纹(与非工作日历同一实现),
      // 真正与格子融为一体、恒在卡片之下、绝不覆盖。见 timeline_cell_class + this.blockCells。
      const blocks = model.tasks.filter((t) => !!t.blockKind)
      const resById = new Map(model.resources.map((r) => [r.id, r]))
      const loadByResource = new Map<string, { assigned: number; available: number; utilization: number }>()
      for (const load of model.loads) {
        const total = loadByResource.get(load.resourceId) ?? { assigned: 0, available: 0, utilization: 0 }
        total.assigned += load.assignedMinutes
        total.available += load.availableMinutes
        total.utilization = Math.max(total.utilization, load.utilization)
        loadByResource.set(load.resourceId, total)
      }
      const groups = new Map<string, string>()
      const laneOf = (t: ScheduleTask) => t.dimensions?.[dim]?.id ?? t.resourceId ?? '__none__'
      // 先用全部资源播种泳道(工作中心维度=资源本身),保证空泳道也常驻——拖走最后一个工序时该行不消失。
      if (dim === 'workCenter') {
        for (const r of model.resources) if (!groups.has(r.id)) groups.set(r.id, r.text)
      }
      for (const t of ops) {
        const id = laneOf(t)
        // 工序携带的维度标签(如「切割中心」)优先于资源原始名(如「激光切割-01」)。
        groups.set(id, t.dimensions?.[dim]?.label ?? t.resourceId ?? groups.get(id) ?? '未分配')
      }
      // 时间块:按当前维度算出所属泳道 + 时段窗口,供 timeline_cell_class 给单元格上底纹;
      // 同时播种泳道(块所在资源即使没工序,泳道也要在)。
      this.blockCells = new Map()
      for (const b of blocks) {
        const id = laneOf(b)
        if (!groups.has(id)) groups.set(id, b.dimensions?.[dim]?.label ?? b.resourceId ?? '未分配')
        const s = Date.parse(b.startUtc)
        const e = Date.parse(b.endUtc)
        if (!Number.isFinite(s) || !Number.isFinite(e)) continue
        const arr = this.blockCells.get(id) ?? []
        arr.push({ start: s, end: e, kind: b.blockKind! })
        this.blockCells.set(id, arr)
      }
      // 泳道按资源固定顺序排(改派后不重排整板);非资源维度保持出现顺序(稳定排序)。
      const resOrder = new Map(model.resources.map((r, i) => [r.id, i]))
      const sortedGroups = [...groups.entries()].sort(
        (a, b) => (resOrder.get(a[0]) ?? Number.MAX_SAFE_INTEGER) - (resOrder.get(b[0]) ?? Number.MAX_SAFE_INTEGER),
      )
      for (const [id, label] of sortedGroups) {
        const res = resById.get(id)
        const load = loadByResource.get(id)
        const utilization = load
          ? load.available > 0 ? load.assigned / load.available : load.utilization
          : undefined
        data.push({
          id: `lane:${id}`,
          text: label,
          type: 'project',
          render: 'split',
          open: true,
          kpi: res
            ? { utilization: res.utilization ?? utilization, oee: res.oee, changeoverCount: res.changeoverCount, materialRisk: res.materialRisk }
            : undefined,
          nerv: { type: 'order', orderId: id, operationId: '', operationSequence: 0, text: label, startUtc: '', endUtc: '', locked: false, hasConflict: false },
        })
      }
      for (const t of ops) data.push(this.toGanttTask(t, `lane:${laneOf(t)}`, toDate))
      // 资源视图不连工单依赖线(跨资源视觉噪声)。
      return { data, links: [] }
    }

    for (const t of model.tasks) {
      if (t.blockKind) continue // 资源时间块只属于资源排产板,不进工单甘特
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

function fmt(isoVal?: string): string {
  if (!isoVal) return '—'
  const d = new Date(isoVal)
  return Number.isNaN(d.getTime()) ? isoVal : d.toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false })
}
