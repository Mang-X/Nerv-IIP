import { defineStepFlow } from './defineStepFlow'

export interface RepairCtx {
  deviceAssetId?: string
  priority?: string
  created?: boolean
}

export interface InspectCtx {
  planId?: string
  result?: string
  recorded?: boolean
}

/** 故障报修多步流程：选设备 → 填明细（优先级/故障描述）→ 创建维修工单。 */
export const repairOrderFlow = defineStepFlow<RepairCtx>({
  id: 'equipment.repair',
  steps: [
    { id: 'selectDevice', done: (c) => Boolean(c.deviceAssetId) },
    { id: 'fillDetails', done: (c) => Boolean(c.priority) },
    { id: 'create', done: (c) => Boolean(c.created) },
  ],
})

/** 点检多步流程：选保养计划 → 录结果 → 记录点检。 */
export const inspectionFlow = defineStepFlow<InspectCtx>({
  id: 'equipment.inspect',
  steps: [
    { id: 'selectPlan', done: (c) => Boolean(c.planId) },
    { id: 'enterResult', done: (c) => Boolean(c.result) },
    { id: 'record', done: (c) => Boolean(c.recorded) },
  ],
})
