import { describe, expect, it } from 'vitest'
import {
  aggregateReceivingGateStatus,
  isReleasedForPutaway,
  orderReleasedForPutaway,
  receivingQualityGateStatusLabel,
  RECEIVING_QUALITY_GATE_STATUS,
  requiresQualityInspection,
} from './receivingQualityGate'

const S = RECEIVING_QUALITY_GATE_STATUS

describe('收货质检门禁', () => {
  it('状态标签映射（含大小写不敏感与未知回退待检）', () => {
    expect(receivingQualityGateStatusLabel(S.pending)).toBe('待检')
    expect(receivingQualityGateStatusLabel(S.passed)).toBe('合格')
    expect(receivingQualityGateStatusLabel(S.conditionalRelease)).toBe('有条件放行')
    expect(receivingQualityGateStatusLabel(S.rejected)).toBe('不合格')
    expect(receivingQualityGateStatusLabel(S.notRequired)).toBe('免检')
    expect(receivingQualityGateStatusLabel('PASSED')).toBe('合格')
    expect(receivingQualityGateStatusLabel(undefined)).toBe('待检')
    expect(receivingQualityGateStatusLabel('weird')).toBe('待检')
  })

  it('isReleasedForPutaway 镜像后端：仅 passed / conditional-release', () => {
    expect(isReleasedForPutaway(S.passed)).toBe(true)
    expect(isReleasedForPutaway(S.conditionalRelease)).toBe(true)
    expect(isReleasedForPutaway(S.pending)).toBe(false)
    expect(isReleasedForPutaway(S.rejected)).toBe(false)
    expect(isReleasedForPutaway(S.notRequired)).toBe(false)
  })

  it('requiresQualityInspection：非 not-required 即需检', () => {
    expect(requiresQualityInspection(S.pending)).toBe(true)
    expect(requiresQualityInspection(S.notRequired)).toBe(false)
  })

  it('aggregateReceivingGateStatus 优先级：不合格>待检>有条件放行>合格>免检', () => {
    expect(aggregateReceivingGateStatus([S.passed, S.rejected, S.pending])).toBe(S.rejected)
    expect(aggregateReceivingGateStatus([S.passed, S.pending])).toBe(S.pending)
    expect(aggregateReceivingGateStatus([S.passed, S.conditionalRelease])).toBe(
      S.conditionalRelease,
    )
    expect(aggregateReceivingGateStatus([S.passed, S.notRequired])).toBe(S.passed)
    expect(aggregateReceivingGateStatus([S.notRequired, S.notRequired])).toBe(S.notRequired)
    expect(aggregateReceivingGateStatus([])).toBe('')
    expect(aggregateReceivingGateStatus([null, undefined])).toBe('')
  })

  it('orderReleasedForPutaway：全部放行/免检且无待检不合格才可上架', () => {
    expect(orderReleasedForPutaway([S.passed, S.notRequired])).toBe(true)
    expect(orderReleasedForPutaway([S.passed, S.conditionalRelease])).toBe(true)
    expect(orderReleasedForPutaway([S.passed, S.pending])).toBe(false)
    expect(orderReleasedForPutaway([S.passed, S.rejected])).toBe(false)
    expect(orderReleasedForPutaway([S.notRequired])).toBe(true)
    expect(orderReleasedForPutaway([])).toBe(false)
  })
})
