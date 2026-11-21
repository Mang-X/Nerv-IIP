import { taskFactRows } from '../../model/task-facts'
import type { ScheduleTask } from '../../model/types'
import { escapeHtml, PRIORITY_CELL } from './task-templates'

interface GridTask {
  text?: string
  type?: string
  start_date?: Date
  end_date?: Date
  nerv?: ScheduleTask
}

export interface LaneTask extends GridTask {
  kpi?: { utilization?: number; oee?: number; changeoverCount?: number; materialRisk?: number }
}

// 左侧列1:资源名(+瓶颈标)。列头随所选维度变(工作中心/设备/班组/产线)。
function laneNameCell(t: LaneTask): string {
  const name = t.nerv?.text ?? ''
  const over = (t.kpi?.utilization ?? 0) > 1
  return `<div class="nerv-lane-id"><span class="nerv-lane-name">${name}</span>${over ? '<span class="nerv-lane-tag">瓶颈</span>' : ''}</div>`
}
export type LaneKpiKey = 'utilization' | 'oee' | 'changeoverCount' | 'materialRisk'
export type LaneKpiVisibility = Record<LaneKpiKey, boolean>

/**
 * 某个指标是否值得上屏:**全部泳道都取不到值、或全部泳道都是 0** 就不上屏(#1399 M8)。
 *
 * 「OEE 0%」比没有这一列更伤——领导看见 0% 会问"设备是不是停了",而真相只是读面没透传
 * 这个字段(根因 C)。一个恒 0 的指标不是"当前表现差",是"我们没这个数",屏幕不该混淆二者。
 *
 * 注意判定的是**跨全部泳道**而不是逐泳道:某条产线今天切换次数确实是 0 是有意义的读数,
 * 只有当整列都没有非零值时才说明这个字段根本没有数据供给。
 */
export function resolveLaneKpiVisibility(lanes: ReadonlyArray<LaneTask['kpi']>): LaneKpiVisibility {
  const anyNonZero = (key: LaneKpiKey) =>
    lanes.some((k) => {
      const v = k?.[key]
      return typeof v === 'number' && Number.isFinite(v) && v !== 0
    })
  return {
    utilization: anyNonZero('utilization'),
    oee: anyNonZero('oee'),
    changeoverCount: anyNonZero('changeoverCount'),
    materialRisk: anyNonZero('materialRisk'),
  }
}

/** 四个指标全无数据 → 整列「产能指标」都不该出现,把宽度还给泳道名。 */
export function hasAnyLaneKpi(visibility: LaneKpiVisibility): boolean {
  return Object.values(visibility).some(Boolean)
}

// 左侧列2:产能指标(利用率 / OEE / 切换 / 待料)。恒 0 / 无数据的指标整项不渲染。
function makeLaneKpiCell(getVisibility: () => LaneKpiVisibility) {
  return (t: LaneTask): string => {
    const k = t.kpi
    if (!k) return ''
    const show = getVisibility()
    const cells: string[] = []
    if (show.utilization) {
      const util = Math.round((k.utilization ?? 0) * 100)
      cells.push(
        `<span class="nerv-lane-kpi"><i>利用率</i><b class="${util > 100 ? 'nerv-over' : ''}">${util}%</b></span>`,
      )
    }
    if (show.oee) {
      cells.push(
        `<span class="nerv-lane-kpi"><i>OEE</i><b>${Math.round((k.oee ?? 0) * 100)}%</b></span>`,
      )
    }
    if (show.changeoverCount) {
      cells.push(`<span class="nerv-lane-kpi"><i>切换</i><b>${k.changeoverCount ?? 0}</b></span>`)
    }
    if (show.materialRisk) {
      const risk = k.materialRisk ?? 0
      cells.push(
        `<span class="nerv-lane-kpi"><i>待料</i><b class="${risk ? 'nerv-warn' : ''}">${risk}</b></span>`,
      )
    }
    return cells.length ? `<div class="nerv-lane-kpis">${cells.join('')}</div>` : ''
  }
}

export const GRID_COLUMNS = (
  view: 'order' | 'resource',
  dimLabel = '工作中心',
  getKpiVisibility: () => LaneKpiVisibility = () => ({
    utilization: true,
    oee: true,
    changeoverCount: true,
    materialRisk: true,
  }),
) => {
  if (view === 'resource') {
    const showKpiColumn = hasAnyLaneKpi(getKpiVisibility())
    return [
      {
        name: 'text',
        label: dimLabel,
        tree: true,
        // 没有产能指标列时泳道名独占宽度:留一条空列比不要这列更显"没做完"。
        width: showKpiColumn ? 128 : 252,
        resize: true,
        template: laneNameCell,
      },
      ...(showKpiColumn
        ? [
            {
              name: 'kpi',
              label: '产能指标',
              width: 124,
              resize: true,
              template: makeLaneKpiCell(getKpiVisibility),
            },
          ]
        : []),
    ]
  }
  const name = {
    name: 'text',
    label: '任务名称',
    tree: true,
    width: 196,
    resize: true,
    template: orderNameCell,
  }
  return [
    name,
    { name: 'owner', label: '负责人', align: 'center', width: 72, template: ownerCell },
    { name: 'priority', label: '优先级', align: 'center', width: 66, template: priorityCell },
    { name: 'status', label: '状态', align: 'center', width: 80, template: statusCell },
    { name: 'duration', label: '工时', align: 'center', width: 52, template: durationLabel },
    { name: 'progress', label: '工单进度', align: 'center', width: 98, template: progressCell },
  ]
}

function orderNameCell(t: GridTask): string {
  const task = t.nerv
  const fullName = t.text ?? task?.text ?? ''
  // 工单已由父节点标明，子行优先展示真实工序序号，避免长编码挤掉区分尾号。
  const label =
    task?.type === 'operation' && task.operationSequence > 0
      ? `工序 ${task.operationSequence}`
      : fullName
  const title =
    task?.operationId && task.operationId !== fullName
      ? `${fullName} · ${task.operationId}`
      : fullName
  return `<span title="${escapeHtml(title)}">${escapeHtml(label)}</span>`
}

function durationLabel(t: GridTask): string {
  if (t.type === 'project' || !t.start_date || !t.end_date) return ''
  const milliseconds = t.nerv?.segments?.length
    ? t.nerv.segments.reduce(
        (sum, segment) => sum + Date.parse(segment.endUtc) - Date.parse(segment.startUtc),
        0,
      )
    : t.end_date.getTime() - t.start_date.getTime()
  const h = Math.round(milliseconds / 3_600_000)
  return h >= 1 ? `${h}h` : '<1h'
}
function ownerCell(t: GridTask): string {
  return t.type === 'project' ? '' : (t.nerv?.owner ?? '—')
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
  const progress = taskFactRows(t.nerv).find(([label]) => label === '工单进度')
  return progress ? `<span class="nerv-ptext">${escapeHtml(progress[1])}</span>` : '—'
}
