import { describe, expect, it } from 'vitest'
import {
  isSchedulableWorkbenchCandidate,
  isSchedulingWorkbenchQuery,
  SCHEDULABLE_WORK_ORDER_STATUSES,
} from './useSchedulingWorkbench'

describe('scheduling workbench helpers', () => {
  it('matches generated query keys structurally instead of by substring', () => {
    const predicate = isSchedulingWorkbenchQuery(['listBusinessConsoleSchedulingPlans'])

    expect(predicate({ key: [{ _id: 'listBusinessConsoleSchedulingPlans' }] } as never)).toBe(true)
    expect(
      predicate({
        key: [{ _id: 'listBusinessConsoleSchedulingPlansArchive' }],
      } as never),
    ).toBe(false)
    expect(predicate({ key: ['listBusinessConsoleSchedulingPlans'] } as never)).toBe(false)
  })

  it('keeps the UI prefilter aligned with terminal work-order statuses', () => {
    expect(
      isSchedulableWorkbenchCandidate({
        workOrderId: 'WO-001',
        productionVersionId: 'PV-001',
        status: 'released',
      }),
    ).toBe(true)
    expect(
      isSchedulableWorkbenchCandidate({
        workOrderId: 'WO-002',
        productionVersionId: 'PV-001',
        status: 'completed',
      }),
    ).toBe(false)
  })

  it('derives the query whitelist from the same table the prefilter reads', () => {
    // 曾经查询白名单与终态黑名单各维护一份：两份今天等价，MES 一加状态就各自漂移。
    expect(SCHEDULABLE_WORK_ORDER_STATUSES.length).toBeGreaterThan(0)
    for (const status of SCHEDULABLE_WORK_ORDER_STATUSES) {
      expect(
        isSchedulableWorkbenchCandidate({
          workOrderId: 'WO-001',
          productionVersionId: 'PV-001',
          status,
        }),
      ).toBe(true)
    }
  })

  it('does not silently drop a work-order status the table has never seen', () => {
    // 未知状态（MES 新加的）不当终态吞掉：权威判定在 Scheduling 服务，前端不替它下结论。
    expect(
      isSchedulableWorkbenchCandidate({
        workOrderId: 'WO-003',
        productionVersionId: 'PV-001',
        status: 'awaiting-material',
      }),
    ).toBe(true)
  })
})
