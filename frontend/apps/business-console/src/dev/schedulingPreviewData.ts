import type { SchedulePlanContract } from '@nerv-iip/api-client'

// 开发预览用样例计划(非产品数据,仅供 dev preview / 视觉确认)。
const H = 3_600_000
const base = Date.parse('2026-06-10T08:00:00.000Z')
const iso = (offsetH: number) => new Date(base + offsetH * H).toISOString()

export const previewPlan: SchedulePlanContract = {
  planId: 'preview-1',
  status: 'generated',
  algorithmVersion: 'heuristic-1',
  generatedAtUtc: iso(0),
  assignments: [
    { assignmentId: 'WO1-10', orderId: 'WO-2026-001', operationId: '下料', operationSequence: 10, resourceId: '激光切割-01', workCenterId: '激光切割-01', startUtc: iso(0), endUtc: iso(3), isLocked: false },
    { assignmentId: 'WO1-20', orderId: 'WO-2026-001', operationId: '折弯', operationSequence: 20, resourceId: '折弯-02', workCenterId: '折弯-02', startUtc: iso(3), endUtc: iso(6), isLocked: false },
    { assignmentId: 'WO1-30', orderId: 'WO-2026-001', operationId: '焊接', operationSequence: 30, resourceId: '焊接-01', workCenterId: '焊接-01', startUtc: iso(6), endUtc: iso(11), isLocked: true },
    { assignmentId: 'WO2-10', orderId: 'WO-2026-002', operationId: '下料', operationSequence: 10, resourceId: '激光切割-01', workCenterId: '激光切割-01', startUtc: iso(3), endUtc: iso(7), isLocked: false },
    { assignmentId: 'WO2-20', orderId: 'WO-2026-002', operationId: '机加工', operationSequence: 20, resourceId: '加工中心-03', workCenterId: '加工中心-03', startUtc: iso(7), endUtc: iso(13), isLocked: false },
    { assignmentId: 'WO3-10', orderId: 'WO-2026-003', operationId: '装配', operationSequence: 10, resourceId: '焊接-01', workCenterId: '焊接-01', startUtc: iso(11), endUtc: iso(16), isLocked: false },
    { assignmentId: 'WO3-20', orderId: 'WO-2026-003', operationId: '总装', operationSequence: 20, resourceId: '加工中心-03', workCenterId: '加工中心-03', startUtc: iso(13), endUtc: iso(20), isLocked: false },
  ],
  resourceLoads: [
    { resourceId: '激光切割-01', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 420, availableMinutes: 480, utilization: 0.88 },
    { resourceId: '折弯-02', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 180, availableMinutes: 480, utilization: 0.38 },
    { resourceId: '焊接-01', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 600, availableMinutes: 480, utilization: 1.25 },
    { resourceId: '加工中心-03', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 540, availableMinutes: 480, utilization: 1.13 },
  ],
  conflicts: [
    { conflictId: 'cf1', reasonCode: 'capacity', severity: 'warning', orderId: 'WO-2026-003', operationId: '装配', resourceId: '焊接-01', message: '焊接-01 在该时段超出可用产能' },
    { conflictId: 'cf2', reasonCode: 'dueDate', severity: 'error', orderId: 'WO-2026-003', operationId: '总装', resourceId: '加工中心-03', message: '预计完工晚于交期' },
  ],
  unscheduledOperations: [
    { orderId: 'WO-2026-004', operationId: '喷涂', reasonCode: 'material', message: '面漆未齐套,等待采购到货' },
  ],
  changeSummary: [
    { orderId: 'WO-2026-001', operationId: '焊接', changeType: 'preserved', message: '锁定,保持原计划' },
    { orderId: 'WO-2026-003', operationId: '总装', changeType: 'delayed', message: '受前序占用,后移 3 小时' },
  ],
  ganttItems: [],
}
