import type { DhxGantt } from './dhtmlx-types'
import { prefersReducedMotion } from './motion'
import { tooltipHtml } from './task-templates'

export class ResourceTooltip {
  private tipEl?: HTMLElement
  private tipTaskId?: string
  private tipRaf = 0
  private tipPendingX = 0
  private tipPendingY = 0

  mount(): void {
    const tip = document.createElement('div')
    tip.className = 'nerv-tip'
    tip.style.position = 'fixed'
    tip.style.left = '0'
    tip.style.top = '0'
    tip.style.zIndex = '60'
    tip.style.pointerEvents = 'none'
    tip.style.opacity = '0'
    tip.style.display = 'none'
    if (!prefersReducedMotion())
      tip.style.transition = 'opacity 120ms var(--nv-ease-out-expo, ease)'
    document.body.appendChild(tip)
    this.tipEl = tip
  }

  destroy(): void {
    this.tipEl?.remove()
    this.tipEl = undefined
    this.tipTaskId = undefined
    cancelAnimationFrame(this.tipRaf)
    this.tipRaf = 0
  }

  /** 自绘 tooltip:悬停任务块时跟指针平滑移动;不在块上或拖拽中则隐藏。 */
  update(
    inst: DhxGantt,
    e: MouseEvent,
    container: HTMLElement | undefined,
    dragging: boolean,
  ): void {
    const tip = this.tipEl
    if (!tip) return
    if (dragging) {
      this.hide()
      return
    }
    const bar = (e.target as HTMLElement)?.closest?.('.gantt_task_line') as HTMLElement | null
    // 只处理本实例容器内的条:页面可能有多个实例,document 级 mousemove 会让每个实例都触发;
    // 用别的实例的 task_id 调本实例 getTask 会抛 "Task not found"(DHTMLX getTask 对缺失 id 抛错),
    // 每帧抛异常还会拖垮性能(tooltip/拖拽卡顿的元凶)。先按容器归属过滤,再按存在性取 task。
    if (!bar || !container?.contains(bar)) {
      this.hide()
      return
    }
    const id = bar.getAttribute('task_id') ?? undefined
    const t = id && inst.isTaskExists?.(id) ? inst.getTask(id) : undefined
    if (!id || !t?.nerv || t.nerv.type !== 'operation' || t.nerv.blockKind) {
      this.hide()
      return
    }
    // 内容一次构建(切换任务时才重建),移动时只改 transform,避免重排。
    if (this.tipTaskId !== id) {
      tip.innerHTML = tooltipHtml(t.nerv)
      this.tipTaskId = id
    }
    tip.style.display = 'block'
    // pointermove 高频:把位置写入合并到每帧一次 rAF(避免每个事件都触发合成),用 translate 定位(GPU 层,不触发 layout)。
    // 这里存的是**指针原始坐标**,偏移与边界翻转都留到 rAF 里算 —— 那时 tip 已完成布局,
    // 才量得到真实尺寸。
    this.tipPendingX = e.clientX
    this.tipPendingY = e.clientY
    if (!this.tipRaf) {
      this.tipRaf = requestAnimationFrame(() => {
        this.tipRaf = 0
        const el = this.tipEl
        if (!el || el.style.display === 'none') return
        el.style.transform = this.tipTransform(el)
        // 首帧就位后再淡入(display:none → block 时 transition 不生效,需分帧)。
        if (el.style.opacity !== '1') el.style.opacity = '1'
      })
    }
  }

  /**
   * tooltip 的落位。默认摆在指针右下方,但**贴近视口右/下边缘时翻到另一侧**——
   * 此前无条件 `指针 + (14, 18)`,鼠标停在甘特底部一行时 tooltip 直接被视口下边切掉,
   * 正好是信息最密的那几行(#1418)。
   *
   * 翻转而不是简单夹取:夹到边上会让 tooltip 盖住指针下的任务条,反而挡住要看的东西。
   * 翻转后若仍装不下(tooltip 比视口还高)再夹进边距,保证至少从顶部开始可读。
   */
  private tipTransform(el: HTMLElement): string {
    const GAP_X = 14
    const GAP_Y = 18
    const MARGIN = 8
    const w = el.offsetWidth
    const h = el.offsetHeight
    const vw = window.innerWidth
    const vh = window.innerHeight

    let x = this.tipPendingX + GAP_X
    if (x + w > vw - MARGIN) x = this.tipPendingX - GAP_X - w
    x = Math.max(MARGIN, Math.min(x, vw - MARGIN - w))

    let y = this.tipPendingY + GAP_Y
    if (y + h > vh - MARGIN) y = this.tipPendingY - GAP_Y - h
    y = Math.max(MARGIN, Math.min(y, vh - MARGIN - h))

    return `translate(${Math.round(x)}px, ${Math.round(y)}px)`
  }

  hide(): void {
    cancelAnimationFrame(this.tipRaf)
    this.tipRaf = 0
    const tip = this.tipEl
    if (!tip || tip.style.display === 'none') return
    tip.style.opacity = '0'
    tip.style.display = 'none'
    this.tipTaskId = undefined
  }
}
