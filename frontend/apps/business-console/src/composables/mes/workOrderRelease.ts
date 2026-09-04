import { describeMesReadinessReason } from '@nerv-iip/business-core'

const RELEASEABLE_WORK_ORDER_STATUSES = new Set(['created', 'started', 'hold'])
const RELEASE_IGNORED_TASK_BLOCKERS = new Set(['PREVIOUS_OPERATION_INCOMPLETE'])

export type MesWorkOrderReleaseCandidate = {
  workOrderId?: string
  status?: string
  productionVersionId?: string | null
  operationTasks?: Array<{
    status?: string
    blockReasons?: Array<string> | null
    evaluatedAtUtc?: string | null
  }> | null
  hasActiveQualityHold?: boolean
  qualityHolds?: Array<{ isActive?: boolean }> | null
}

function isQueued(task: { status?: string }) {
  return (task.status ?? '').toLowerCase() === 'queued'
}

/**
 * 从工单读面推导“可以尝试下达”，口径对齐 `ReleaseWorkOrderCommandHandler`：
 * 工单状态、生产版本、工艺路线快照、质量保留、物料/设备就绪，**不含工序状态**。
 *
 * 工序 readiness 只在 queued 工序上求值（`MesOperationTaskActionReadinessEvaluator`
 * 对非 queued 工序恒返回空 blockReasons），所以非 queued 工序既不作为就绪证据、
 * 也不作为阻断理由——把空 blocker 当已就绪是假绿，把它当阻断则比后端守卫更严，
 * 会把「工序已开工的工单事后补下达」这条自愈路径从界面上藏掉。
 */
export function mesWorkOrderReleaseBlocker(order: MesWorkOrderReleaseCandidate) {
  if (!order.workOrderId) return '工单标识缺失，不能下达'
  if (!RELEASEABLE_WORK_ORDER_STATUSES.has((order.status ?? '').toLowerCase())) {
    return '当前状态不能下达'
  }
  if (!order.productionVersionId?.trim()) return '缺少生产版本，不能下达'
  if (!order.operationTasks?.length) return '尚未生成工序任务，不能下达'
  if (order.hasActiveQualityHold || order.qualityHolds?.some((hold) => hold.isActive)) {
    return '存在有效质量保留，不能下达'
  }
  for (const task of order.operationTasks) {
    if (!isQueued(task)) continue
    if (!task.evaluatedAtUtc?.trim() || !Array.isArray(task.blockReasons)) {
      return '工序就绪状态尚未取得，不能下达'
    }
    const reason = task.blockReasons.find((candidate) => {
      const { code } = describeMesReadinessReason(candidate)
      return !RELEASE_IGNORED_TASK_BLOCKERS.has(code)
    })
    if (reason) {
      const display = describeMesReadinessReason(reason)
      return `${display.label}，不能下达${display.detail ? `：${display.detail}` : ''}`
    }
  }
  return null
}

/**
 * 工序已离开排队的工单，其「下达」是事后补发布事实（母票 #3113 的自愈路径），
 * 与开工前的常规下达不是同一件事。确认框里必须说出这点，否则用户无从判断
 * 这一下会不会动到正在跑的工序。
 */
export function mesWorkOrderRetroactiveReleaseNotice(order: MesWorkOrderReleaseCandidate) {
  if (!order.operationTasks?.some((task) => !isQueued(task))) return null
  return '该工单已有工序不在排队中：下达只补齐工单的发布记录，不会改变工序当前进度。'
}
