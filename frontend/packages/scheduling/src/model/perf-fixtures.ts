import type { SchedulePlanContract } from '@nerv-iip/api-client'

const BASE = Date.parse('2026-06-10T00:00:00.000Z')
const HOUR = 3_600_000

/** 确定性生成大规模排程计划(默认 200 工单 × 10 工序 = 2000 工序,200 资源),用于性能基线。 */
export function makeLargePlan(orders = 200, opsPerOrder = 10, resources = 200): SchedulePlanContract {
  const assignments = []
  for (let o = 0; o < orders; o++) {
    for (let k = 0; k < opsPerOrder; k++) {
      const idx = o * opsPerOrder + k
      const start = BASE + (idx % 500) * HOUR
      assignments.push({
        assignmentId: `a${idx}`,
        orderId: `WO-${o}`,
        operationId: `op-${k}`,
        operationSequence: (k + 1) * 10,
        resourceId: `WC-${idx % resources}`,
        workCenterId: `WC-${idx % resources}`,
        startUtc: new Date(start).toISOString(),
        endUtc: new Date(start + 2 * HOUR).toISOString(),
        isLocked: false,
      })
    }
  }
  const resourceLoads = Array.from({ length: resources }, (_, r) => ({
    resourceId: `WC-${r}`,
    windowStartUtc: new Date(BASE).toISOString(),
    windowEndUtc: new Date(BASE + 24 * HOUR).toISOString(),
    assignedMinutes: 120 + (r % 6) * 60,
    availableMinutes: 480,
    utilization: (120 + (r % 6) * 60) / 480,
  }))
  return {
    planId: 'perf',
    status: 'generated',
    algorithmVersion: 'perf',
    generatedAtUtc: new Date(BASE).toISOString(),
    assignments,
    resourceLoads,
    conflicts: [],
    unscheduledOperations: [],
    changeSummary: [],
    ganttItems: [],
  }
}
