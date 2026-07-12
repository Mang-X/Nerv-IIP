import type { SchedulePlanContract } from '@nerv-iip/api-client'

// 确定性样例,贴合 #206 SchedulePlanContract 形状,供 mapper / 引擎契约测试复用。
export const samplePlan: SchedulePlanContract = {
  planId: 'plan-1',
  status: 'generated',
  algorithmVersion: 'heuristic-1',
  generatedAtUtc: '2026-06-10T00:00:00.000Z',
  assignments: [
    {
      assignmentId: 'a1',
      orderId: 'WO-001',
      operationId: 'op-10',
      operationSequence: 10,
      resourceId: 'WC-001',
      workCenterId: 'WC-001',
      startUtc: '2026-06-10T08:00:00.000Z',
      endUtc: '2026-06-10T10:00:00.000Z',
      isLocked: false,
      explanationCode: 'earliestSlot',
    },
    {
      assignmentId: 'a2',
      orderId: 'WO-001',
      operationId: 'op-20',
      operationSequence: 20,
      resourceId: 'WC-002',
      workCenterId: 'WC-002',
      startUtc: '2026-06-10T10:00:00.000Z',
      endUtc: '2026-06-10T12:00:00.000Z',
      isLocked: true,
      explanationCode: 'locked',
    },
  ],
  resourceLoads: [
    {
      resourceId: 'WC-001',
      windowStartUtc: '2026-06-10T00:00:00.000Z',
      windowEndUtc: '2026-06-11T00:00:00.000Z',
      assignedMinutes: 120,
      availableMinutes: 480,
      utilization: 0.25,
    },
  ],
  conflicts: [
    {
      conflictId: 'c1',
      reasonCode: 'capacity',
      severity: 'warning',
      orderId: 'WO-001',
      operationId: 'op-20',
      resourceId: 'WC-002',
      message: '产能不足',
    },
  ],
  unscheduledOperations: [
    { orderId: 'WO-002', operationId: 'op-10', reasonCode: 'material', message: '物料未齐套' },
  ],
  changeSummary: [
    { orderId: 'WO-001', operationId: 'op-20', changeType: 'moved', message: '后移 2 小时' },
  ],
  ganttItems: [],
}
