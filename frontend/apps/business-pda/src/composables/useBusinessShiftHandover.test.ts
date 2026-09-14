import { describe, expect, it } from 'vitest'
import {
  assertHandoverAccepted,
  formatAttachmentSize,
  formatHandoverTimestamp,
  incomingPartyState,
  incomingUserLabel,
  outgoingPartyState,
  outgoingUserLabel,
  toHandoverPhotoFileName,
} from './useBusinessShiftHandover'

describe('outgoingUserLabel / incomingUserLabel', () => {
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
   * 判据是**身份 id 在不在**，不是姓名在不在。
   *
   * 网关始终按认证 principal 注入 id，所以「已接班 + id 有值 + 姓名解不出」是常态；
   * 把它说成「未记录」等于在一张标着「已接班」的单子上断言「没有接班人」——与数据相反。
   * 把实现改回按 name 判据时，本格必红。
   */
  it('says 姓名未知 — NOT 未记录 — when the id is on record but the name is not resolvable', () => {
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
      incomingUserLabel({ incomingUserId: null, incomingUserName: null, acceptedAtUtc: null }),
    ]
    for (const label of labels) expect(label).not.toContain('user-admin')
  })

  it('exposes the four states so callers branch instead of re-deriving the rule', () => {
    expect(outgoingPartyState({ outgoingUserId: 'u', outgoingUserName: '张三' })).toBe('named')
    expect(outgoingPartyState({ outgoingUserId: 'u', outgoingUserName: null })).toBe(
      'name-unresolved',
    )
    expect(outgoingPartyState({ outgoingUserId: null, outgoingUserName: null })).toBe('absent')
    expect(
      incomingPartyState({ incomingUserId: null, incomingUserName: null, acceptedAtUtc: null }),
    ).toBe('pending')
  })
})

describe('formatHandoverTimestamp', () => {
  it('renders a full date-time for the detail face and a compact one for the 375px list', () => {
    // 期望值按钉死的 Asia/Shanghai 写；宿主时区变了本格也不该变（CI 跑在 UTC）。
    const value = '2026-09-14T13:53:47.529528+00:00'
    expect(formatHandoverTimestamp(value)).toBe('2026/9/14 21:53')
    expect(formatHandoverTimestamp(value, true)).toBe('09/14 21:53')
  })

  it('does not follow the host timezone (读数必须与设备/CI 时区无关)', () => {
    const value = '2026-09-14T16:30:00.000Z'
    // 同一时刻在 UTC 是 16:30、在 Asia/Shanghai 是次日 00:30；钉住后者。
    expect(formatHandoverTimestamp(value, true)).toBe('09/15 00:30')
    expect(formatHandoverTimestamp(value)).toBe('2026/9/15 00:30')
  })

  it('returns an empty string instead of inventing a time when it cannot parse', () => {
    // 调用方据此不渲染；印一个 Invalid Date 或假时间比不印更糟。
    expect(formatHandoverTimestamp(undefined)).toBe('')
    expect(formatHandoverTimestamp(null)).toBe('')
    expect(formatHandoverTimestamp('   ')).toBe('')
    expect(formatHandoverTimestamp('not-a-date')).toBe('')
  })
})

describe('formatAttachmentSize', () => {
  it('scales bytes / KB / MB', () => {
    expect(formatAttachmentSize(512)).toBe('512 B')
    expect(formatAttachmentSize(2048)).toBe('2.0 KB')
    expect(formatAttachmentSize(3 * 1024 * 1024)).toBe('3.0 MB')
  })

  it('does not pretend an unknown size is 0 B', () => {
    expect(formatAttachmentSize(undefined)).toBe('未知大小')
    expect(formatAttachmentSize(null)).toBe('未知大小')
    expect(formatAttachmentSize(-1)).toBe('未知大小')
    expect(formatAttachmentSize(Number.NaN)).toBe('未知大小')
  })
})

describe('toHandoverPhotoFileName', () => {
  const at = new Date('2026-09-14T01:02:03.000Z')

  it('forces the extension to match the declared content type', () => {
    // FileStorage 的 shift-handover-photo 用途按扩展名放行，名字与类型不符 complete 会被顶回。
    expect(toHandoverPhotoFileName('shot.heic', 'image/jpeg', at)).toBe('shot.jpg')
    expect(toHandoverPhotoFileName('shot.jpeg', 'image/png', at)).toBe('shot.png')
    expect(toHandoverPhotoFileName('shot', 'image/png', at)).toBe('shot.png')
  })

  it('synthesises a name when the WebView hands back a blank one', () => {
    expect(toHandoverPhotoFileName('', 'image/jpeg', at)).toBe(
      'handover-photo-2026-09-14T01-02-03-000Z.jpg',
    )
    expect(toHandoverPhotoFileName(undefined, 'image/png', at)).toBe(
      'handover-photo-2026-09-14T01-02-03-000Z.png',
    )
  })

  it('strips path separators out of the supplied name', () => {
    expect(toHandoverPhotoFileName('../../etc/passwd.png', 'image/png', at)).toBe(
      '....etcpasswd.png',
    )
  })
})

describe('assertHandoverAccepted', () => {
  it('accepts the gateway envelope that only carries accepted=true', () => {
    // BusinessConsoleAcceptedResponse 允许 operationReceipt 为空；那本身就是受理回执。
    expect(() => assertHandoverAccepted({ data: { accepted: true } }, '接班')).not.toThrow()
    expect(() =>
      assertHandoverAccepted(
        { data: { accepted: true, operationReceipt: { outcome: 'confirmed' } } },
        '接班',
      ),
    ).not.toThrow()
  })

  it('rejects an empty shell instead of reporting success on screen', () => {
    for (const response of [
      undefined,
      null,
      {},
      { data: null },
      { data: {} },
      { data: { accepted: false } },
      { data: { accepted: true, operationReceipt: { outcome: 'rejected' } } },
      { data: { accepted: true, operationReceipt: 'accepted' } },
    ]) {
      expect(() => assertHandoverAccepted(response, '交班提交')).toThrow('未返回有效回执')
    }
  })

  it('names the failed action in the copy', () => {
    expect(() => assertHandoverAccepted({}, '接班')).toThrow('接班未返回有效回执')
    expect(() => assertHandoverAccepted({}, '交班提交')).toThrow('交班提交未返回有效回执')
  })
})
