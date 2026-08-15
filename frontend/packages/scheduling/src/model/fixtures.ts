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

// 带工作日历与不可用窗口的样例:覆盖「甘特日历底纹 + 班次边界 + 资源时间块」这条读面。
// 6/10 是工作日(早班 08–16、中班 16–24),6/11 只排早班;换型窗口落在 WC-001 两批活之间。
export const samplePlanWithCalendar: SchedulePlanContract = {
  ...samplePlan,
  planId: 'plan-calendar-1',
  calendars: [
    {
      calendarId: 'CAL-MAIN',
      resourceIds: ['WC-001', 'WC-002'],
      workCenterIds: ['WC-001', 'WC-002'],
      shiftWindows: [
        {
          startUtc: '2026-06-10T00:00:00.000Z',
          endUtc: '2026-06-10T08:00:00.000Z',
          shiftCode: 'early-shift',
        },
        {
          startUtc: '2026-06-10T08:00:00.000Z',
          endUtc: '2026-06-10T16:00:00.000Z',
          shiftCode: 'middle-shift',
        },
        {
          startUtc: '2026-06-11T00:00:00.000Z',
          endUtc: '2026-06-11T08:00:00.000Z',
          shiftCode: 'early-shift',
        },
      ],
    },
  ],
  blockWindows: [
    {
      resourceId: 'WC-001',
      workCenterId: 'WC-001',
      startUtc: '2026-06-10T10:00:00.000Z',
      endUtc: '2026-06-10T11:00:00.000Z',
      reasonCode: 'changeover.setup',
      kind: 'changeover',
    },
    {
      resourceId: 'WC-002',
      workCenterId: 'WC-002',
      startUtc: '2026-06-10T13:00:00.000Z',
      endUtc: '2026-06-10T15:00:00.000Z',
      reasonCode: 'maintenance.preventive',
      kind: 'maintenance',
    },
  ],
}
