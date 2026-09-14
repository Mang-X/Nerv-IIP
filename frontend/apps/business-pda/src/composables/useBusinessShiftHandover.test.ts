import { describe, expect, it } from 'vitest'
import {
  assertHandoverAccepted,
  formatAttachmentSize,
  incomingUserLabel,
  isSystemIdentifier,
  outgoingUserLabel,
  toDirectoryOptions,
  toHandoverPhotoFileName,
} from './useBusinessShiftHandover'

describe('isSystemIdentifier / toDirectoryOptions', () => {
  it('keeps active business codes and drops GUID-shaped engineering identifiers', () => {
    const options = toDirectoryOptions([
      { code: 'EARLY', displayName: '早班', active: true },
      { code: 'a3f1c2d4-1111-2222-3333-444455556666', displayName: '内部行', active: true },
      { code: 'NIGHT', displayName: '夜班', active: true },
    ])
    // 顺序由中文 label 排序决定（见下方排序用例），这里只关心 GUID 行被剔掉。
    expect([...options.map((o) => o.value)].sort()).toEqual(['EARLY', 'NIGHT'])
  })

  it('drops inactive and blank-code rows', () => {
    const options = toDirectoryOptions([
      { code: 'MIDDLE', displayName: '中班', active: false },
      { code: '   ', displayName: '空码', active: true },
      { code: 'EARLY', displayName: '早班', active: true },
    ])
    expect(options.map((o) => o.value)).toEqual(['EARLY'])
  })

  it('falls back to the code when the display name is itself a GUID', () => {
    const options = toDirectoryOptions([
      { code: 'TEAM-A', displayName: 'a3f1c2d4-1111-2222-3333-444455556666', active: true },
    ])
    expect(options).toEqual([{ value: 'TEAM-A', label: 'TEAM-A' }])
  })

  it('treats a missing `active` flag as selectable (读面不回 active 时不能整表消失)', () => {
    expect(toDirectoryOptions([{ code: 'EARLY', displayName: '早班' }])).toEqual([
      { value: 'EARLY', label: '早班' },
    ])
  })

  it('sorts by Chinese label so the picker order is stable', () => {
    const options = toDirectoryOptions([
      { code: 'C', displayName: '中班', active: true },
      { code: 'A', displayName: '早班', active: true },
      { code: 'B', displayName: '夜班', active: true },
    ])
    expect(options.map((o) => o.label)).toEqual(['夜班', '早班', '中班'])
  })

  it('recognises braced/parenthesised GUID spellings too', () => {
    expect(isSystemIdentifier('{a3f1c2d4-1111-2222-3333-444455556666}')).toBe(true)
    expect(isSystemIdentifier('(a3f1c2d4-1111-2222-3333-444455556666)')).toBe(true)
    expect(isSystemIdentifier('TEAM-WB-MC-A')).toBe(false)
    expect(isSystemIdentifier(undefined)).toBe(false)
  })
})

describe('outgoingUserLabel / incomingUserLabel', () => {
  it('never echoes a raw user id when the directory could not resolve a name', () => {
    expect(outgoingUserLabel({ outgoingUserName: null })).toBe('未记录')
    expect(outgoingUserLabel({ outgoingUserName: '  ' })).toBe('未记录')
    expect(outgoingUserLabel({ outgoingUserName: '张三' })).toBe('张三')
  })

  it('tells 待接班 apart from 已接班但解析不出名字', () => {
    // 两种都没有 incomingUserName，但成因完全不同：一个还没人接，一个接了解析不出。
    expect(incomingUserLabel({ incomingUserName: null, acceptedAtUtc: null })).toBe('待接班')
    expect(
      incomingUserLabel({ incomingUserName: null, acceptedAtUtc: '2026-09-14T01:00:00Z' }),
    ).toBe('未记录')
    expect(
      incomingUserLabel({ incomingUserName: '李四', acceptedAtUtc: '2026-09-14T01:00:00Z' }),
    ).toBe('李四')
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
