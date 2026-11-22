import { scheduleTime } from '../../model/factory-time'
import { taskFactRows } from '../../model/task-facts'
import type { ScheduleTask } from '../../model/types'
import { conflictReasonLabel } from '../../model/labels'
import { materialReadyLabel } from '../../model/material-risk'

export const PRIORITY_CELL: Record<string, [string, string, string]> = {
  high: ['nerv-prio-high', '↑', '高'],
  medium: ['nerv-prio-mid', '—', '中'],
  low: ['nerv-prio-low', '↓', '低'],
}

const lucide = (paths: string, size = 11) =>
  `<svg class="nerv-ic" viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`
export const LOCK_SVG = lucide(
  '<rect width="18" height="11" x="3" y="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
)
const RUSH_ICON = lucide(
  '<path d="M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z"/>',
)
const ALERT_ICON = lucide(
  '<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4"/><path d="M12 17h.01"/>',
  12,
)
const fmtMd = (iso: string, timezone?: string) => {
  if (timezone !== undefined)
    return scheduleTime(iso, timezone, { month: '2-digit', day: '2-digit' })
  const d = new Date(iso)
  return Number.isNaN(d.getTime())
    ? ''
    : `${d.getMonth() + 1}-${String(d.getDate()).padStart(2, '0')}`
}

const BLOCK_LABEL: Record<NonNullable<ScheduleTask['blockKind']>, string> = {
  maintenance: '设备维护',
  downtime: '计划停机',
  lineChange: '换线窗口',
  changeover: '换型窗口',
}
/** 资源时间块(维护/停机/换线/换型)背景带的低调内标签(与格子融合,非卡片)。 */
export function blockLabelHtml(t: ScheduleTask): string {
  const fmtHm = (iso: string) => {
    if (t.timezone !== undefined)
      return scheduleTime(iso, t.timezone, { hour: '2-digit', minute: '2-digit' })
    const d = new Date(iso)
    return Number.isNaN(d.getTime())
      ? ''
      : `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  }
  const span = t.startUtc && t.endUtc ? `${fmtHm(t.startUtc)}-${fmtHm(t.endUtc)}` : ''
  return `<span class="nerv-block-bg-label">${BLOCK_LABEL[t.blockKind!]}${span ? ` · ${span}` : ''}</span>`
}

/** 资源排产板工单卡片(条内 HTML)。布局对齐参考图:WO+优先级+插单+锁 / 产品·工序 / 数量·交期 / 换型·占用 / 齐套。 */
export function escapeHtml(value: string): string {
  return value.replace(
    /[&<>"']/g,
    (character) =>
      ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]!,
  )
}

export function cardHtml(t: ScheduleTask): string {
  const prio = t.priority
    ? `<span class="nerv-card-prio nerv-prio-${t.priority}">${PRIORITY_CELL[t.priority][2]}</span>`
    : ''
  const rush = t.isRush ? `<span class="nerv-card-rush" title="插单">${RUSH_ICON}</span>` : ''
  const lock = t.locked ? `<span class="nerv-card-lock" title="已锁定">${LOCK_SVG}</span>` : ''
  const due = t.dueUtc ? fmtMd(t.dueUtc, t.timezone) : ''
  const time = (iso: string) =>
    scheduleTime(iso, t.timezone, { hour: '2-digit', minute: '2-digit' })
  const kit = t.kitting != null ? Math.round(t.kitting * 100) : null
  const kitCls = kit == null ? '' : kit >= 100 ? 'ok' : kit >= 80 ? 'warn' : 'bad'
  const co = t.changeoverMin ? `换型 ${t.changeoverMin}m` : ''
  const load = t.load != null ? `占用 ${Math.round(t.load * 100)}%` : ''
  const meta3 = [co, load].filter(Boolean).join('　')
  // 冲突徽标:与优先级/插单/锁并排在首行(不再悬浮角标)。
  const alert = t.hasConflict
    ? `<span class="nerv-card-alert" title="冲突">${ALERT_ICON}</span>`
    : ''
  // 物料风险(软约束):已排但缺料 —— 提示「开工前备料」,不是排不进去。
  const matRiskLabel = materialReadyLabel(t.materialRisk)
  const matRisk = t.materialRisk
    ? `<span class="nv-sched-material-risk-chip" title="${matRiskLabel ?? '需在开工前完成备料'}">${matRiskLabel ?? '缺料待备'}</span>`
    : ''
  // 设备数据风险(软约束):排在状态未知的设备上 —— 提示「开工前确认设备」,不是排不进去。
  const equipRisk = t.equipmentRisk
    ? `<span class="nv-sched-equipment-risk-chip" title="开工前请人工确认设备可用">设备状态未知</span>`
    : ''
  const prog =
    t.progress != null
      ? `<span class="nerv-card-prog"><span style="width:${Math.round(t.progress * 100)}%"></span></span>`
      : ''
  return `<div class="nerv-card">
    <div class="nerv-card-r1"><span class="nerv-card-wo">${t.orderId}</span><span class="nerv-card-meta">${prio}${rush}${alert}${lock}</span></div>
    <div class="nerv-card-r2" title="${escapeHtml(t.text)}">${escapeHtml(t.text)}</div>
    <div class="nerv-card-r3 nerv-card-time">${time(t.startUtc)}–${time(t.endUtc)}${t.status ? ` · ${escapeHtml(t.status.label)}` : ''}</div>
    ${t.product ? `<div class="nerv-card-r3">${escapeHtml(t.product)}</div>` : ''}
    <div class="nerv-card-r3">${t.quantity != null ? `数量 ${t.quantity}` : ''}${due ? `　交期 ${due}` : ''}</div>
    ${taskFactRows(t)
      .map(
        ([label, value]) =>
          `<div class="nerv-card-r3">${escapeHtml(label)} ${escapeHtml(value)}</div>`,
      )
      .join('')}
    <div class="nerv-card-r3">${meta3}</div>
    <div class="nerv-card-tags">${t.downtimeRisk ? `<span style="color:var(--destructive)">停机风险</span>` : ''}${kit != null ? `<span class="nerv-kit nerv-kit-${kitCls}">齐套 ${kit}%</span>` : ''}${matRisk}${equipRisk}</div>
    ${prog}
  </div>`
}

/**
 * 任务块 tooltip 的 HTML(DHTMLX 插件与资源板自绘 tip 共用同一内容)。
 * 导出仅为可测:这段是拼字符串直接进 innerHTML,裸色值混进来在浏览器里很难被发现
 * (得 hover 到恰好是插单的那一条),交给单测按 token 断言。
 */
export function tooltipHtml(t: ScheduleTask): string {
  const prio = t.priority ? { high: '高', medium: '中', low: '低' }[t.priority] : ''
  const pct = (v?: number) => (v == null ? '' : `${Math.round(v * 100)}%`)
  const chip = (txt: string, tone: string) =>
    `<span style="font-size:11px;font-weight:600;padding:0 6px;border-radius:4px;color:${tone};background:color-mix(in oklch,${tone},transparent 86%)">${txt}</span>`
  const badges = [
    prio ? chip(`${prio}优先`, 'var(--destructive)') : '',
    // 插单走 --nv-scheduling-rush,与图例(SchedulingLegend)、工序详情(TaskDetailPanel)同一个
    // 事实源。此前硬编码 oklch(0.7 0.17 60),与 token 的 oklch(0.68 0.18 45) 不是同一个颜色
    // ——同语义两色,且裸值进 innerHTML 后暗色主题不跟随(#1399 M6)。
    t.isRush ? chip('插单', 'var(--nv-scheduling-rush)') : '',
    t.locked ? chip('已锁定', 'var(--nv-brand)') : '',
    t.hasConflict ? chip('冲突', 'var(--destructive)') : '',
    t.downtimeRisk ? chip('停机风险', 'var(--destructive)') : '',
    t.materialRisk
      ? chip(materialReadyLabel(t.materialRisk) ?? '缺料待备', 'var(--nv-scheduling-kit-warn)')
      : '',
    t.equipmentRisk ? chip('设备状态未知', 'var(--nv-scheduling-equip-unknown)') : '',
  ]
    .filter(Boolean)
    .join('')
  const head = `<div style="font-weight:700;font-size:13px;letter-spacing:.01em;margin-bottom:6px;padding-bottom:6px;border-bottom:1px solid color-mix(in oklch,var(--foreground),transparent 88%);display:flex;align-items:center;gap:6px;flex-wrap:wrap"><span>${escapeHtml(t.orderId)}</span>${badges}</div>`
  const rows: Array<[string, string]> = [
    ['工序', t.text || '—'],
    ...taskFactRows(t),
    ...(t.product ? ([['产品', t.product]] as Array<[string, string]>) : []),
    ...(t.resourceId ? ([['资源', t.resourceId]] as Array<[string, string]>) : []),
    ...(t.owner ? ([['负责人', t.owner]] as Array<[string, string]>) : []),
    ...(t.factoryTimeLabel ? [['工厂时区', t.factoryTimeLabel] as [string, string]] : []),
    ['起止', `${fmt(t.startUtc, t.timezone)} → ${fmt(t.endUtc, t.timezone)}`],
    ...(t.quantity != null ? ([['数量', String(t.quantity)]] as Array<[string, string]>) : []),
    ...(t.dueUtc ? ([['交期', fmt(t.dueUtc, t.timezone)]] as Array<[string, string]>) : []),
    ...(t.kitting != null ? ([['齐套', pct(t.kitting)]] as Array<[string, string]>) : []),
    ...(t.changeoverMin ? ([['换型', `${t.changeoverMin} 分钟`]] as Array<[string, string]>) : []),
    ...(t.load != null ? ([['占用', pct(t.load)]] as Array<[string, string]>) : []),
    ...(t.status ? ([['状态', t.status.label]] as Array<[string, string]>) : []),
    ...(t.hasConflict && t.conflictReason
      ? ([['冲突', conflictReasonLabel[t.conflictReason]]] as Array<[string, string]>)
      : []),
    ...(t.materialRisk ? ([['物料', t.materialRisk.message]] as Array<[string, string]>) : []),
    ...(t.equipmentRisk ? ([['设备', t.equipmentRisk.message]] as Array<[string, string]>) : []),
    ...(t.downtimeRisk ? ([['停机风险', t.downtimeRisk]] as Array<[string, string]>) : []),
  ]
  const body = rows
    .map(
      ([k, v]) =>
        `<div class="nerv-tip-row"><span class="nerv-tip-key">${escapeHtml(k)}</span><span class="nerv-tip-value">${escapeHtml(v)}</span></div>`,
    )
    .join('')
  return head + body
}

export function fmt(isoVal?: string, timezone?: string): string {
  if (isoVal && timezone !== undefined)
    return scheduleTime(isoVal, timezone, {
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    })
  if (!isoVal) return '—'
  const d = new Date(isoVal)
  return Number.isNaN(d.getTime())
    ? isoVal
    : d.toLocaleString('zh-CN', {
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
      })
}
