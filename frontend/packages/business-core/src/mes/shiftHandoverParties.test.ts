import { describe, expect, it } from 'vitest'

import {
  incomingPartyState,
  incomingUserLabel,
  outgoingPartyState,
  outgoingUserLabel,
} from './shiftHandoverParties'

/**
 * 这组断言是「两屏同一口径」的承重件：PDA 与 business-console 都从这里取判据和文案。
 * 把判据改回「按 name 判」时，`M16`/`M17` 两格必红（见 PR 评论里的变异矩阵）。
 */
describe('shiftHandoverParties', () => {
  it('shows the resolved display name when the worker directory answered', () => {
    expect(outgoingUserLabel({ outgoingUserId: 'user-a', outgoingUserName: '张三' })).toBe('张三')
    expect(
      incomingUserLabel({
        incomingUserId: 'user-b',
        incomingUserName: '李四',
        acceptedAtUtc: '2026-09-14T01:00:00Z',
      }),
    ).toBe('李四')
  })

  /**
   * 判据是**身份 id 在不在**，不是姓名在不在。网关始终按认证 principal 注入 id，
   * 所以「已接班 + id 有值 + 姓名解不出」是常态；说成「未记录」等于在一张标着
   * 「已接班」的单子上断言「没有接班人」——与数据相反。
   */
  it('says 姓名未知 — NOT 未记录 — when the id is on record but the name is not resolvable', () => {
    expect(outgoingPartyState({ outgoingUserId: 'user-admin', outgoingUserName: null })).toBe(
      'name-unresolved',
    )
    expect(outgoingUserLabel({ outgoingUserId: 'user-admin', outgoingUserName: null })).toBe(
      '姓名未知',
    )
    expect(
      incomingUserLabel({
        incomingUserId: 'user-admin',
        incomingUserName: null,
        acceptedAtUtc: '2026-09-14T01:00:00Z',
      }),
    ).toBe('姓名未知')
  })

  it('keeps 未记录 for the genuinely missing identity (问责链真的断了)', () => {
    expect(outgoingUserLabel({ outgoingUserId: null, outgoingUserName: null })).toBe('未记录')
    expect(outgoingUserLabel({ outgoingUserId: '   ', outgoingUserName: '  ' })).toBe('未记录')
    expect(
      incomingUserLabel({
        incomingUserId: null,
        incomingUserName: null,
        acceptedAtUtc: '2026-09-14T01:00:00Z',
      }),
    ).toBe('未记录')
  })

  it('keeps 待接班 apart from both — 还没人接 ≠ 接了班但身份缺失', () => {
    expect(
      incomingPartyState({ incomingUserId: null, incomingUserName: null, acceptedAtUtc: null }),
    ).toBe('pending')
    expect(
      incomingUserLabel({ incomingUserId: null, incomingUserName: null, acceptedAtUtc: null }),
    ).toBe('待接班')
  })

  it('never puts the IAM principal id on screen in any state', () => {
    const labels = [
      outgoingUserLabel({ outgoingUserId: 'user-admin', outgoingUserName: null }),
      incomingUserLabel({
        incomingUserId: 'user-admin',
        incomingUserName: null,
        acceptedAtUtc: '2026-09-14T01:00:00Z',
      }),
      incomingUserLabel({
        incomingUserId: null,
        incomingUserName: null,
        acceptedAtUtc: '2026-09-14T01:00:00Z',
      }),
      incomingUserLabel({ incomingUserId: null, incomingUserName: null, acceptedAtUtc: null }),
    ]
    for (const label of labels) expect(label).not.toContain('user-admin')
  })

  /**
   * 状态层穷举（`*PartyState`）。调用方按状态分支，不重新推导规则，所以四个状态本身要各有一格。
   *
   * 这一格是从 `business-pda` 的 `useBusinessShiftHandover.test.ts` **搬过来**的：四态判据
   * 现在只住在本模块，覆盖也要跟着落在这里，不能两边各留一份（#3644 Q-B3）。
   */
  it('exposes all four states so callers branch instead of re-deriving the rule', () => {
    expect(outgoingPartyState({ outgoingUserId: 'u', outgoingUserName: '张三' })).toBe('named')
    expect(outgoingPartyState({ outgoingUserId: 'u', outgoingUserName: null })).toBe(
      'name-unresolved',
    )
    expect(outgoingPartyState({ outgoingUserId: null, outgoingUserName: null })).toBe('absent')

    expect(
      incomingPartyState({
        incomingUserId: 'u',
        incomingUserName: '李四',
        acceptedAtUtc: '2026-09-14T01:00:00Z',
      }),
    ).toBe('named')
    expect(
      incomingPartyState({
        incomingUserId: 'u',
        incomingUserName: null,
        acceptedAtUtc: '2026-09-14T01:00:00Z',
      }),
    ).toBe('name-unresolved')
    expect(
      incomingPartyState({
        incomingUserId: null,
        incomingUserName: null,
        acceptedAtUtc: '2026-09-14T01:00:00Z',
      }),
    ).toBe('absent')
    expect(
      incomingPartyState({ incomingUserId: null, incomingUserName: null, acceptedAtUtc: null }),
    ).toBe('pending')
  })

  it('trims the resolved name and treats whitespace-only names as unresolved', () => {
    expect(outgoingUserLabel({ outgoingUserId: 'user-a', outgoingUserName: '  张三  ' })).toBe(
      '张三',
    )
    expect(outgoingUserLabel({ outgoingUserId: 'user-a', outgoingUserName: '   ' })).toBe(
      '姓名未知',
    )
  })
})
