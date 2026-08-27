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

/**
 * 从工单读面保守推导“可以尝试下达”。非 queued 工序的 action readiness 不包含
 * release handler 会执行的设备/物料检查，因此必须失败关闭，不能把空 blocker 当作已就绪。
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
    if ((task.status ?? '').toLowerCase() !== 'queued') {
      return '当前工序状态无法证明下达就绪，不能下达'
    }
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
