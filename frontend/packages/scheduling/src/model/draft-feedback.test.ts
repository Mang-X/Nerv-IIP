import { describe, expect, it } from 'vitest'
import { toModel } from './aps-mapper'
import { evaluateDraft } from './draft-feedback'
import type { SchedulePlanContract } from '@nerv-iip/api-client'

const utc = (hour: number) => new Date(Date.UTC(2026, 8, 30, hour)).toISOString()
// DomainInvariant: #4043 验收 2–6；独立期望来自票面和 APS 的连续窗口/保留占用口径。
const assignment = (id: string, start: number, end: number, resourceId = 'RES-1') => ({
  assignmentId: id,
  orderId: id,
  operationId: 'OP-10',
  operationSequence: 10,
  resourceId,
  workCenterId: 'WC-1',
  startUtc: utc(start),
  endUtc: utc(end),
  isLocked: false,
})
const feedbackPlan: SchedulePlanContract = {
  planId: 'APS-260930-001',
  assignments: [assignment('WO-01', 8, 10), assignment('WO-02', 10, 12)],
  calendars: [
    {
      calendarId: 'CAL-1',
      resourceIds: ['RES-1', 'RES-2'],
      workCenterIds: ['WC-1'],
      shiftWindows: [
        { startUtc: utc(8), endUtc: utc(12), shiftCode: '早班' },
        { startUtc: utc(11), endUtc: utc(14), shiftCode: '中班' },
        { startUtc: utc(14), endUtc: utc(18), shiftCode: '晚班' },
      ],
    },
  ],
  validationContext: {
    horizonStartUtc: utc(0),
    horizonEndUtc: utc(24),
    resources: ['RES-1', 'RES-2'].map((resourceId) => ({
      resourceId,
      workCenterId: 'WC-1',
      calendarId: 'CAL-1',
      capacityUnits: 1,
      utilizationRate: 1,
    })),
    operations: ['WO-01', 'WO-02', 'WO-03'].map((orderId) => ({
      orderId,
      operationId: 'OP-10',
      predecessorOperationIds: [],
      dueUtc: utc(12),
      durationMinutes: 120,
      setupMinutes: 0,
      isFixed: false,
    })),
    fixedReservations: [],
  },
}
const evaluate = (plan: SchedulePlanContract) => evaluateDraft(toModel(plan))
const kinds = (plan: SchedulePlanContract, id = 'WO-01') =>
  evaluate(plan).tasks[id]!.issues.map((x) => x.kind)

describe('draft feedback (#4043)', () => {
  it('maps the frozen context and operation due without joining MES data', () => {
    const model = toModel(feedbackPlan)
    expect(model.validationContext?.operations).toHaveLength(3)
    expect(model.tasks.find((x) => x.id === 'WO-01')?.dueUtc).toBe(utc(12))
    expect(toModel({ ...feedbackPlan, validationContext: null }).validationContext).toBeUndefined()
  })
  it('merges overlapping and adjacent shifts, but reports a real calendar gap', () => {
    const plan = structuredClone(feedbackPlan)
    plan.assignments = [assignment('WO-01', 9, 17)]
    expect(kinds(plan)).not.toContain('calendar')
    plan.calendars![0]!.shiftWindows![2]!.startUtc = utc(15)
    expect(kinds(plan)).toContain('calendar')
  })
  it('uses half-open intervals and segment concurrency rather than pairwise overlap', () => {
    const plan = structuredClone(feedbackPlan)
    expect(kinds(plan)).not.toContain('capacity')
    plan.validationContext!.resources![0]!.capacityUnits = 2
    plan.assignments = [
      assignment('WO-01', 8, 12),
      assignment('WO-02', 8, 10),
      assignment('WO-03', 10, 12),
    ]
    expect(kinds(plan)).not.toContain('capacity')
    plan.assignments[2]!.startUtc = utc(9)
    expect(evaluate(plan).tasks['WO-01']!.issues).toContainEqual(
      expect.objectContaining({
        kind: 'capacity',
        scope: 'resource',
        startUtc: utc(9),
        endUtc: utc(10),
      }),
    )
    expect(kinds(plan, 'WO-02')).toContain('capacity')
    expect(kinds(plan, 'WO-03')).toContain('capacity')
  })
  it('counts work-center-only external reservations and does not double count visible fixed work', () => {
    const plan = structuredClone(feedbackPlan)
    plan.assignments = [assignment('WO-01', 8, 10), assignment('WO-02', 8, 10, 'RES-2')]
    plan.validationContext!.fixedReservations = [
      {
        orderId: 'WO-EXT',
        operationId: 'OP-10',
        workCenterId: 'WC-1',
        startUtc: utc(9),
        endUtc: utc(11),
      },
    ]
    expect(evaluate(plan).tasks['WO-01']!.issues).toContainEqual(
      expect.objectContaining({
        kind: 'capacity',
        scope: 'workCenter',
        startUtc: utc(9),
        endUtc: utc(10),
      }),
    )
    plan.validationContext!.fixedReservations = [{ ...plan.assignments[0]! }]
    plan.validationContext!.operations![0]!.isFixed = true
    plan.validationContext!.resources![0]!.utilizationRate = 0.5
    plan.validationContext!.operations![0]!.setupMinutes = 60
    expect(kinds(plan)).not.toContain('capacity')
    expect(kinds(plan, 'WO-02')).not.toContain('capacity')
  })
  it.each(['RES-1', undefined])(
    'uses edited fixed work instead of its frozen reservation (resource %s)',
    (resourceId) => {
      const plan = structuredClone(feedbackPlan)
      plan.assignments = [
        { ...assignment('WO-01', 8, 10), isLocked: true },
        assignment('WO-02', 8, 10),
        assignment('WO-03', 19, 20, 'RES-2'),
      ]
      plan.validationContext!.fixedReservations = [
        {
          orderId: 'WO-01',
          operationId: 'OP-10',
          workCenterId: 'WC-1',
          resourceId,
          startUtc: utc(8),
          endUtc: utc(10),
        },
      ]
      plan.validationContext!.operations![0]!.isFixed = true
      const model = toModel(plan)
      const task = model.tasks.find((task) => task.id === 'WO-01')!
      Object.assign(task, {
        locked: false,
        resourceId: 'RES-2',
        startUtc: utc(19),
        endUtc: utc(20),
      })
      const feedback = evaluateDraft(model)
      expect(feedback.tasks['WO-01']!.issues).toContainEqual(
        expect.objectContaining({ kind: 'calendar' }),
      )
      expect(feedback.tasks['WO-01']!.issues).toContainEqual(
        expect.objectContaining({
          kind: 'capacity',
          scope: 'resource',
          startUtc: utc(19),
          endUtc: utc(20),
        }),
      )
      expect(feedback.tasks['WO-02']!.issues.map((issue) => issue.kind)).not.toContain('capacity')
    },
  )
  it('includes setup before a successor and utilization-reserved tails, not just displayed bars', () => {
    const plan = structuredClone(feedbackPlan)
    plan.validationContext!.operations![1]!.setupMinutes = 30
    expect(kinds(plan, 'WO-02')).toContain('capacity')
    plan.validationContext!.operations![1]!.setupMinutes = 0
    plan.validationContext!.resources![0]!.utilizationRate = 0.5
    expect(kinds(plan, 'WO-02')).toContain('capacity')
    plan.assignments = [assignment('WO-01', 16, 18)]
    expect(kinds(plan)).toContain('calendar')
  })
  it('treats unavailable blocks as unavailable even on a multi-capacity resource', () => {
    const plan = structuredClone(feedbackPlan)
    plan.validationContext!.resources![0]!.capacityUnits = 3
    plan.blockWindows = [
      { resourceId: 'RES-1', startUtc: utc(9), endUtc: utc(10), kind: 'maintenance' },
    ]
    expect(kinds(plan)).toContain('capacity')
  })
  it('checks true predecessor identities including unscheduled operations, not visible sequence links', () => {
    const plan = structuredClone(feedbackPlan)
    plan.assignments![1]!.orderId = 'WO-01'
    plan.assignments![1]!.operationId = 'OP-30'
    plan.validationContext!.operations![1] = {
      ...plan.validationContext!.operations![1]!,
      orderId: 'WO-01',
      operationId: 'OP-30',
      predecessorOperationIds: ['OP-20'],
    }
    expect(kinds(plan, 'WO-02')).toContain('predecessorUnscheduled')
    plan.validationContext!.operations![1]!.predecessorOperationIds = ['OP-10']
    expect(kinds(plan, 'WO-02')).not.toContain('predecessor')
    plan.assignments![1]!.startUtc = utc(9)
    expect(kinds(plan, 'WO-02')).toContain('predecessor')
  })
  it('calculates early, on-time and late from the snapshot operation due and edited end', () => {
    const plan = structuredClone(feedbackPlan)
    expect(evaluate(plan).tasks['WO-01']!.due).toMatchObject({
      status: 'early',
      deltaMinutes: -120,
    })
    expect(evaluate(plan).tasks['WO-02']!.due).toMatchObject({ status: 'onTime', deltaMinutes: 0 })
    plan.assignments![1]!.endUtc = utc(13)
    expect(evaluate(plan).tasks['WO-02']!.due).toMatchObject({ status: 'late', deltaMinutes: 60 })
  })
  it('checks actual split segments without treating the production gap as occupancy', () => {
    const plan = structuredClone(feedbackPlan)
    plan.assignments![0] = {
      ...assignment('WO-01', 8, 14),
      segments: [
        { startUtc: utc(8), endUtc: utc(10) },
        { startUtc: utc(12), endUtc: utc(14) },
      ],
    }
    expect(kinds(plan)).not.toContain('capacity')
  })
  it('does not claim validation when the authoritative context is absent or edited time is incomplete', () => {
    expect(kinds({ ...feedbackPlan, validationContext: null })).toEqual(['unknown'])
    expect(
      evaluate({ ...feedbackPlan, validationContext: null }).tasks['WO-01']!.due,
    ).toBeUndefined()
    const plan = structuredClone(feedbackPlan)
    plan.assignments![0]!.startUtc = '2026-09-'
    expect(kinds(plan)).toContain('invalidTime')
  })
})
