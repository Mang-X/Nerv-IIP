import { describe, expect, it } from 'vitest'
import {
  isSchedulableWorkbenchCandidate,
  isSchedulingWorkbenchQuery,
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
})
