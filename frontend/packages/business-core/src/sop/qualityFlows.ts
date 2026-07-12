import { defineStepFlow } from './defineStepFlow'

export interface QualityInspectionTaskCtx {
  taskId?: string
  /** 是否已录入至少一条有效特性结果。 */
  hasResults?: boolean
  submitted?: boolean
}

/** 检验任务执行多步流程：选待检任务 → 逐特性录结果 → 提交（MAN-457 / #811）。 */
export const qualityInspectionTaskFlow = defineStepFlow<QualityInspectionTaskCtx>({
  id: 'quality.inspectionTask',
  steps: [
    { id: 'selectTask', done: (c) => Boolean(c.taskId) },
    { id: 'recordResults', done: (c) => Boolean(c.hasResults) },
    { id: 'submit', done: (c) => Boolean(c.submitted) },
  ],
})
