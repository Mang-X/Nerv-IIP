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
 * 工序已离开排队的工单，其「下达」发生在开工之后，与开工前的常规下达不是同一件事，
 * 确认框需要点出这个前提。
 *
 * 文案只陈述读面上看得见的前提，**不承诺下达的后果**：
 * - 不能说「不会改变工序当前进度」——下达发出的 `WorkOrderReleased` 会经
 *   `SchedulingPlanInvalidationService.InvalidateAllGeneratedPlansAsync`
 *   （scope 为 AllInvalidatablePlans，且计划内无本工单时回落成整张计划的全部工序）
 *   走到 `OperationTask.MarkScheduleInvalidated`，把 **Queued** 工序改成 `ScheduleInvalidated`；
 *   而本提示出现的场景恰好最可能存在 queued 兄弟工序。
 * - 不能说「补齐发布记录」——`WorkOrderReleasedIntegrationEventConverter` 目前把
 *   releasedAtUtc 写成 `DateTimeOffset.UtcNow`，对**已有报工**的工单会让 Quality 的
 *   `PeriodicInspectionOperation` 抛「报工时刻早于发布时刻」而整封进死信（由 #3117 承担）。
 */
export function mesWorkOrderRetroactiveReleaseNotice(order: MesWorkOrderReleaseCandidate) {
  if (!order.operationTasks?.some((task) => !isQueued(task))) return null
  return '该工单已有工序不在排队中，这是对已开工工单的补充下达。'
}
