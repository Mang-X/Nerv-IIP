import type {
  SchedulingDowntimeImpact,
  SchedulingDowntimeImpactResponse,
} from '@nerv-iip/api-client'
import type { ScheduleModel, ScheduleTask } from '@nerv-iip/scheduling'

export function downtimePresentation(item: SchedulingDowntimeImpact, now: Date) {
  const fact = item.fact!
  const actualEnd = fact.recoveredAtUtc ?? now.toISOString()
  const minutes = Math.max(
    0,
    Math.floor((Date.parse(actualEnd) - Date.parse(fact.startedAtUtc!)) / 60_000),
  )
  const hours = Math.floor(minutes / 60)
  const remaining = minutes % 60
  return {
    startUtc: fact.startedAtUtc!,
    endUtc: fact.recoveredAtUtc ?? fact.expectedRestoreAtUtc ?? now.toISOString(),
    duration: hours ? `${hours} 小时${remaining ? ` ${remaining} 分钟` : ''}` : `${remaining} 分钟`,
    status: fact.recoveredAtUtc
      ? '已恢复'
      : !fact.expectedRestoreAtUtc
        ? '无 ETR'
        : Date.parse(fact.expectedRestoreAtUtc) <= now.getTime()
          ? 'ETR 已到期，仍未恢复'
          : '预计恢复',
  }
}

// 只覆盖展示模型，编辑、重预览和发布仍使用原始草稿及持久化方案。
export function withDowntimeImpact(
  model: ScheduleModel | undefined,
  impact: SchedulingDowntimeImpactResponse | undefined,
  now: Date,
): ScheduleModel | undefined {
  if (!model || !impact || impact.baselinePlanId !== model.meta.planId) return model
  const affected = new Set(
    (impact.affectedOperations ?? []).map((op) => `${op.workOrderId}:${op.operationId}`),
  )
  const facts = (impact.items ?? []).map((item) => item.fact!)
  // 保存方案的停机底纹是计算时快照。用同一资源、同一起点（含窗口裁剪）的当前事实替换，
  // 避免清除/更新 ETR 后旧预测底纹继续占据时间轴；其它停机和维护窗口仍保留。
  const tasks = model.tasks
    .filter(
      (task) =>
        !(
          task.blockKind === 'downtime' &&
          facts.some(
            (fact) =>
              (fact.deviceAssetId
                ? task.resourceId === fact.deviceAssetId
                : task.workCenterId === fact.workCenterId) &&
              Date.parse(task.startUtc) ===
                Math.max(Date.parse(fact.startedAtUtc!), Date.parse(model.horizon.startUtc)),
          )
        ),
    )
    .map((task) =>
      task.type === 'operation' &&
      !task.blockKind &&
      affected.has(`${task.orderId}:${task.operationId}`)
        ? { ...task, downtimeRisk: '设备停机影响此工序；选定候选前保持原排程。' }
        : task,
    )
  const blocks: ScheduleTask[] = (impact.items ?? []).map((item, index) => {
    const fact = item.fact!
    const workCenterId =
      fact.workCenterId ??
      model.tasks.find((task) => task.resourceId === fact.deviceAssetId)?.workCenterId
    const display = downtimePresentation(item, now)
    return {
      id: `live-downtime:${fact.source}:${fact.sourceReferenceId}:${index}`,
      orderId: '',
      operationId: '',
      operationSequence: 0,
      type: 'operation',
      text: `设备停机 · ${display.status}`,
      resourceId: fact.deviceAssetId ?? undefined,
      workCenterId: workCenterId ?? undefined,
      dimensions: workCenterId
        ? { workCenter: { id: workCenterId, label: workCenterId } }
        : undefined,
      startUtc: display.startUtc,
      endUtc: display.endUtc,
      blockKind: 'downtime',
      locked: true,
      hasConflict: false,
    }
  })
  return { ...model, tasks: [...tasks, ...blocks] }
}
