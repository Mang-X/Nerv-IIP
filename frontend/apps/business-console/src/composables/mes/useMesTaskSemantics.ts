import type { StatusTone } from '@nerv-iip/ui'

/**
 * 工序任务的状态语义单一事实源（派工看板 / 工序执行 / 工单详情共用）。
 *
 * 车间读一行工序时问的是三件互不相干的事，所以拆成三个正交事实、各占一列，
 * 谁也不许替谁回答（曾踩坑：把「排程状态」当成工序的总状态，已完工的工序被标成「未排程」）：
 *
 * 1. 执行状态 execution —— 这道工序现在干到哪一步了（待开工 / 加工中 / 已暂停 / 已完工…）。
 * 2. 派工状态 dispatch  —— 这道工序派给谁了（已派工 / 待派工 / 完工时未记录工人）。
 * 3. 排程状态 schedule  —— 排产有没有给它定过时间（已排程 / 未排程 / 排程已失效 / 完工后不适用）。
 *
 * 终态（已完工 / 已关闭 / 已取消 / 已报废）的工序不再需要排程、也不再需要派工，
 * 这两列一律给「不适用」而不是「未排程 / 未派工」——那两个词会被现场读成「漏排了、漏派了」。
 */

export type MesExecutionStateKey =
  | 'queued'
  | 'ready'
  | 'running'
  | 'paused'
  | 'blocked'
  | 'completed'
  | 'cancelled'
  | 'scheduleInvalidated'
  | 'unknown'

export interface MesTaskStateDisplay {
  key: string
  label: string
  tone: StatusTone
}

export interface MesOperationTaskLike {
  status?: string | null
  assignedUserId?: string | null
  assignedUserName?: string | null
  scheduledAtUtc?: string | null
  scheduleInvalidationReasonCode?: string | null
  blockingReasons?: string[] | null
}

const EXECUTION_STATES: Record<MesExecutionStateKey, MesTaskStateDisplay> = {
  queued: { key: 'queued', label: '待开工', tone: 'warning' },
  ready: { key: 'ready', label: '可开工', tone: 'success' },
  running: { key: 'running', label: '加工中', tone: 'info' },
  paused: { key: 'paused', label: '已暂停', tone: 'warning' },
  blocked: { key: 'blocked', label: '已阻塞', tone: 'danger' },
  completed: { key: 'completed', label: '已完工', tone: 'success' },
  cancelled: { key: 'cancelled', label: '已取消', tone: 'neutral' },
  scheduleInvalidated: { key: 'scheduleInvalidated', label: '待重排', tone: 'warning' },
  unknown: { key: 'unknown', label: '未知状态', tone: 'neutral' },
}

// 后端工序生命周期状态（含历史别名）→ 一线话执行状态。
const EXECUTION_BY_STATUS: Record<string, MesExecutionStateKey> = {
  queued: 'queued',
  created: 'queued',
  released: 'queued',
  ready: 'ready',
  active: 'running',
  inprogress: 'running',
  running: 'running',
  started: 'running',
  paused: 'paused',
  hold: 'paused',
  held: 'paused',
  blocked: 'blocked',
  completed: 'completed',
  closed: 'completed',
  posted: 'completed',
  cancelled: 'cancelled',
  scrapped: 'cancelled',
  scheduleinvalidated: 'scheduleInvalidated',
}

/** 终态：工序已经走完，排程与派工都不再适用。 */
const SETTLED_STATES = new Set<MesExecutionStateKey>(['completed', 'cancelled'])

function normalize(status?: string | null) {
  return (status ?? '').trim().toLowerCase()
}

/** 工序生命周期状态 → 执行状态（标签 + 色调）。 */
export function resolveExecutionState(status?: string | null): MesTaskStateDisplay {
  const key = EXECUTION_BY_STATUS[normalize(status)]
  return key ? EXECUTION_STATES[key] : EXECUTION_STATES.unknown
}

/** 该工序是否已走完（完工 / 取消 / 报废 / 关闭）。 */
export function isSettledTask(status?: string | null): boolean {
  const key = EXECUTION_BY_STATUS[normalize(status)]
  return key ? SETTLED_STATES.has(key) : false
}

export function isScheduleInvalidatedTask(status?: string | null): boolean {
  return normalize(status) === 'scheduleinvalidated'
}

/**
 * 派工状态。已完工的工序若没留下工人，说的是「完工时未记录」而不是「待派工」——
 * 后者会让班组长以为这道工序还等着他派人。
 */
export function resolveDispatchState(row: MesOperationTaskLike): MesTaskStateDisplay {
  const worker = row.assignedUserName?.trim() || undefined
  const assigned = Boolean(row.assignedUserId?.trim() || worker)
  if (assigned) {
    return { key: 'assigned', label: worker ? `已派 ${worker}` : '已派工', tone: 'info' }
  }
  if (isSettledTask(row.status)) {
    return { key: 'notRecorded', label: '完工未记录工人', tone: 'neutral' }
  }
  return { key: 'unassigned', label: '待派工', tone: 'warning' }
}

/**
 * 排程状态。只认 `scheduledAtUtc`（排产下达才写它），人工派工不会把它置上，
 * 所以「人工指派但未经排产」不会被误报成已排程。终态工序给「不适用」。
 */
export function resolveScheduleState(row: MesOperationTaskLike): MesTaskStateDisplay {
  if (isScheduleInvalidatedTask(row.status)) {
    return { key: 'invalidated', label: '排程已失效', tone: 'warning' }
  }
  if (isSettledTask(row.status)) {
    return { key: 'settled', label: '已完工，不适用', tone: 'neutral' }
  }
  if (row.scheduledAtUtc) return { key: 'scheduled', label: '已排程', tone: 'info' }
  if (row.assignedUserId?.trim()) {
    return { key: 'manual', label: '人工派工，未排产', tone: 'neutral' }
  }
  return { key: 'unscheduled', label: '未排程', tone: 'warning' }
}

export interface MesDispatchAffordance {
  /** 动作按钮文案——永远是动词，不是状态。 */
  label: string
  /** 不可用时给出为什么、下一步去哪；可用时为空。 */
  blockedReason?: string
  enabled: boolean
}

/**
 * 派工动作可用性与文案。
 * 「有阻塞 / 待重排 / 已完工」都是不能派的理由，理由写在禁用项的说明里，
 * 不要把理由塞进按钮文案（曾踩坑：按钮上写「有阻塞，先处理」，读起来像一个可点的动作）。
 */
export function resolveDispatchAffordance(row: MesOperationTaskLike): MesDispatchAffordance {
  if (isSettledTask(row.status)) {
    return { label: '派工', blockedReason: '该工序已结束，无需派工。', enabled: false }
  }
  if (isScheduleInvalidatedTask(row.status)) {
    return {
      label: '派工',
      blockedReason: '排程已失效，等计划员重新排程后才能派工。',
      enabled: false,
    }
  }
  if (row.blockingReasons?.length) {
    return {
      label: '派工',
      blockedReason: '存在开工阻塞，先按阻塞项处理后再派工。',
      enabled: false,
    }
  }
  const worker = row.assignedUserName?.trim()
  if (row.assignedUserId?.trim()) {
    return { label: worker ? `改派（当前 ${worker}）` : '改派', enabled: true }
  }
  return { label: '派工', enabled: true }
}

/** 开工阻塞的呈现口径：无阻塞时说「无阻塞」，不要说「可派工」（那是动作不是事实）。 */
export function hasBlockingReasons(row: MesOperationTaskLike): boolean {
  return Boolean(row.blockingReasons?.length)
}

export type MesLifecycleActionKey = 'start' | 'pause' | 'resume' | 'complete'

export interface MesLifecycleAction {
  key: MesLifecycleActionKey
  label: string
  enabled: boolean
  /** 不可用的原因；可用时为空。 */
  blockedReason?: string
  /** 需要二次确认（完工不可轻易回退）。 */
  confirm?: boolean
}

/**
 * 工序生命周期动作的可用性，严格对齐后端 OperationTask 聚合的状态机：
 * start 仅 Queued → InProgress；pause 仅 InProgress → Paused；
 * resume 仅 Paused → InProgress；complete 仅 InProgress → Completed。
 * 前端按同一套规则禁用，避免点下去才被后端 409/400 顶回来。
 */
export function resolveLifecycleActions(row: MesOperationTaskLike): MesLifecycleAction[] {
  const key = EXECUTION_BY_STATUS[normalize(row.status)]
  const queued = key === 'queued' || key === 'ready'
  const running = key === 'running'
  const paused = key === 'paused'
  const settled = key ? SETTLED_STATES.has(key) : false
  const invalidated = key === 'scheduleInvalidated'

  function reason(available: boolean, need: string): string | undefined {
    if (available) return undefined
    if (settled) return '该工序已结束。'
    if (invalidated) return '排程已失效，等重新排程后再操作。'
    return need
  }

  return [
    {
      key: 'start',
      label: '开工',
      enabled: queued,
      blockedReason: reason(queued, '只有待开工的工序可以开工。'),
    },
    {
      key: 'pause',
      label: '暂停',
      enabled: running,
      blockedReason: reason(running, '只有加工中的工序可以暂停。'),
    },
    {
      key: 'resume',
      label: '恢复加工',
      enabled: paused,
      blockedReason: reason(paused, '只有已暂停的工序可以恢复。'),
    },
    {
      key: 'complete',
      label: '完工',
      enabled: running,
      blockedReason: reason(running, '先开工才能完工。'),
      confirm: true,
    },
  ]
}
