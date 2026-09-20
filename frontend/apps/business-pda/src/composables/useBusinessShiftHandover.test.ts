import { describe, expect, it } from 'vitest'
import {
  incomingPartyState as coreIncomingPartyState,
  incomingUserLabel as coreIncomingUserLabel,
  outgoingPartyState as coreOutgoingPartyState,
  outgoingUserLabel as coreOutgoingUserLabel,
} from '@nerv-iip/business-core'
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

/**
 * PDA 侧只剩**冒烟**。四态判据（named / 姓名未知 / 未记录 / 待接班）自 #3644 起只住在
 * `@nerv-iip/business-core` 的 `mes/shiftHandoverParties`，穷举也只落在那份测试里。
 *
 * **删掉的那几条原本要证「四态判据正确」——那件事换了住处，所以断言也跟着搬走**
 * （搬到 `shiftHandoverParties.test.ts`，含新增的状态层四格穷举）。在这里留第二份
 * 不增加任何鉴别力（等价输入），却会随时间静默分叉成两套说法。
 *
 * **留下这一条要证的真不变量是另一件事**：本模块转出的就是 business-core 那几个函数**本身**，
 * 而不是一个同名的第二实现、也没有在转出时把符号接错。
 *
 * 为什么必须按**函数引用**（`toBe`）比而不是按行为比：一份行为完全正确的本地副本会让所有
 * 「四态说法对不对」的断言照常全绿——那正是这条要防的失效方向，只有引用相等能看见它。
 */
describe('shift-handover party labels are re-exported, not re-implemented', () => {
  it('re-exports the business-core implementations themselves', () => {
    expect(outgoingUserLabel).toBe(coreOutgoingUserLabel)
    expect(incomingUserLabel).toBe(coreIncomingUserLabel)
    expect(outgoingPartyState).toBe(coreOutgoingPartyState)
    expect(incomingPartyState).toBe(coreIncomingPartyState)
  })

  // 转出时把 outgoing/incoming 接反不报编译错（同签名形状），所以单独钉一格可观察后果：
  // 交班侧不看 `acceptedAtUtc`，接反后「未接班且无 id」会从「未记录」变成「待接班」。
  it('keeps the outgoing and incoming exports on their own fields', () => {
    expect(outgoingUserLabel({ outgoingUserId: null, outgoingUserName: null })).toBe('未记录')
    expect(
      incomingUserLabel({ incomingUserId: null, incomingUserName: null, acceptedAtUtc: null }),
    ).toBe('待接班')
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
