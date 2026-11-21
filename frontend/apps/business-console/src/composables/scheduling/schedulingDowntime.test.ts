import { describe, expect, it } from 'vitest'
import { toModel } from '@nerv-iip/scheduling'
import type { SchedulingDowntimeImpactResponse } from '@nerv-iip/api-client'
import { downtimePresentation, withDowntimeImpact } from './schedulingDowntime'

const start = '2026-10-09T06:00:00.000Z'
const at = (hour: number) => new Date(`2026-10-09T${String(hour).padStart(2, '0')}:00:00.000Z`)
const fact = {
  source: 'mes',
  sourceReferenceId: 'DT-01',
  deviceAssetId: 'DEV-01',
  workCenterId: 'WC-01',
  startedAtUtc: start,
}
const impact: SchedulingDowntimeImpactResponse = {
  baselinePlanId: 'plan-001',
  items: [{ fact, operationsWithAlternativesCount: 1 }],
  affectedOperations: [{ workOrderId: 'WO-01', operationId: 'OP-10' }],
}
const model = () =>
  toModel({
    planId: 'plan-001',
    assignments: [
      {
        assignmentId: 'a1',
        orderId: 'WO-01',
        operationId: 'OP-10',
        resourceId: 'DEV-01',
        workCenterId: 'WC-01',
        startUtc: start,
        endUtc: at(10).toISOString(),
        isLocked: true,
        segments: [
          { startUtc: start, endUtc: at(7).toISOString() },
          { startUtc: at(9).toISOString(), endUtc: at(10).toISOString() },
        ],
      },
      {
        assignmentId: 'a2',
        orderId: 'WO-02',
        operationId: 'OP-10',
        resourceId: 'DEV-01',
        startUtc: start,
        endUtc: at(7).toISOString(),
      },
    ],
  })

describe('persisted-baseline downtime display', () => {
  it('grows open blocks and real duration, uses ETR only for the block, and stops at actual recovery', () => {
    expect(downtimePresentation(impact.items![0], at(8))).toMatchObject({
      endUtc: at(8).toISOString(),
      duration: '2 小时',
      status: '无 ETR',
    })
    expect(downtimePresentation(impact.items![0], at(9))).toMatchObject({
      endUtc: at(9).toISOString(),
      duration: '3 小时',
    })
    const predicted = {
      ...impact.items![0],
      fact: { ...fact, expectedRestoreAtUtc: at(10).toISOString() },
    }
    expect(downtimePresentation(predicted, at(8))).toMatchObject({
      endUtc: at(10).toISOString(),
      duration: '2 小时',
    })
    expect(downtimePresentation(predicted, at(10)).status).toContain('ETR 已到期，仍未恢复')
    const recovered = {
      ...predicted,
      fact: { ...predicted.fact, recoveredAtUtc: at(9).toISOString() },
    }
    expect(downtimePresentation(recovered, at(11))).toMatchObject({
      endUtc: at(9).toISOString(),
      duration: '3 小时',
      status: '已恢复',
    })
  })
  it('marks exact operations without changing assignments, segments, locks or unrelated semantics', () => {
    const original = model()
    const before = structuredClone(original)
    const displayed = withDowntimeImpact(original, impact, at(8))!
    const operation = displayed.tasks.find((t) => t.id === 'a1')!
    expect(operation.downtimeRisk).toContain('设备停机')
    const { downtimeRisk: _, ...unchanged } = operation
    expect(unchanged).toEqual(before.tasks.find((t) => t.id === 'a1'))
    expect(displayed.tasks.find((t) => t.id === 'a2')).toEqual(
      before.tasks.find((t) => t.id === 'a2'),
    )
    expect(displayed.tasks.find((t) => t.blockKind === 'downtime')).toMatchObject({
      startUtc: start,
      endUtc: at(8).toISOString(),
      resourceId: 'DEV-01',
      locked: true,
    })
    expect(original).toEqual(before)
    expect(withDowntimeImpact(original, { ...impact, baselinePlanId: 'other' }, at(8))).toBe(
      original,
    )
  })
  it('replaces a matching saved downtime prediction while retaining other windows', () => {
    const original = model()
    original.horizon.startUtc = at(7).toISOString()
    original.tasks.push({
      id: 'snapshot',
      orderId: '',
      operationId: '',
      operationSequence: 0,
      type: 'operation',
      resourceId: 'DEV-01',
      startUtc: at(7).toISOString(),
      endUtc: at(11).toISOString(),
      text: '停机',
      blockKind: 'downtime',
      locked: true,
      hasConflict: false,
    })
    original.tasks.push({ ...original.tasks.at(-1)!, id: 'other', resourceId: 'DEV-02' })
    const displayed = withDowntimeImpact(original, impact, at(9))!
    expect(displayed.tasks.some((task) => task.id === 'snapshot')).toBe(false)
    expect(displayed.tasks.some((task) => task.id === 'other')).toBe(true)
    expect(displayed.tasks.find((task) => task.id.startsWith('live-downtime:'))?.endUtc).toBe(
      at(9).toISOString(),
    )
  })
  it('updates and clears ETR on refreshed facts while retaining manual draft edits', () => {
    const edited = model()
    edited.tasks.find((t) => t.id === 'a1')!.startUtc = at(7).toISOString()
    const updated = {
      ...impact,
      items: [{ fact: { ...fact, expectedRestoreAtUtc: at(11).toISOString() } }],
    }
    expect(withDowntimeImpact(edited, updated, at(8))!.tasks.find((t) => t.blockKind)?.endUtc).toBe(
      at(11).toISOString(),
    )
    const cleared = withDowntimeImpact(edited, impact, at(9))!
    expect(cleared.tasks.find((t) => t.blockKind)?.endUtc).toBe(at(9).toISOString())
    expect(cleared.tasks.find((t) => t.id === 'a1')?.startUtc).toBe(at(7).toISOString())
  })
})
