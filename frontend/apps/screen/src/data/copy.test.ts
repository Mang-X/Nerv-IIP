import { describe, expect, it } from 'vitest'
import { MRB_PENDING_BADGE, OEE_PLACEHOLDER_BADGE, RELIABILITY_SNAPSHOT_BADGE } from './copy'

describe('screen status copy', () => {
  it('makes the OEE placeholder scope explicit instead of labelling real availability', () => {
    expect(OEE_PLACEHOLDER_BADGE).toBe('综合值 · P/Q 占位')
    expect(RELIABILITY_SNAPSHOT_BADGE).toBe('运行快照 · 非 OEE')
  })

  it('puts MRB pending review in a concise header badge', () => {
    expect(MRB_PENDING_BADGE(3)).toBe('MRB 待评审 3')
  })
})
