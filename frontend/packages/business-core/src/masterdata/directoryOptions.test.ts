import { describe, expect, it } from 'vitest'
import { isSystemIdentifier, resolveDirectoryLabel, toDirectoryOptions } from './directoryOptions'

describe('isSystemIdentifier / toDirectoryOptions', () => {
  it('keeps active business codes and drops GUID-shaped engineering identifiers', () => {
    const options = toDirectoryOptions([
      { code: 'EARLY', displayName: '早班', active: true },
      { code: 'a3f1c2d4-1111-2222-3333-444455556666', displayName: '内部行', active: true },
      { code: 'NIGHT', displayName: '夜班', active: true },
    ])
    // 顺序由中文 label 排序决定（见下方排序用例），这里只关心 GUID 行被剔掉。
    expect(options.map((o) => o.value).sort()).toEqual(['EARLY', 'NIGHT'])
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

describe('resolveDirectoryLabel', () => {
  const labels = new Map([
    ['DAY', '早班'],
    ['TEAM-ASSY-A', '装配一线早班组'],
  ])

  it('maps a known code to its Chinese display name', () => {
    expect(resolveDirectoryLabel('DAY', labels, '未排班')).toBe('早班')
    expect(resolveDirectoryLabel('  DAY  ', labels, '未排班')).toBe('早班')
  })

  it('echoes an unresolved BUSINESS code instead of claiming 未排班', () => {
    // 「目录里查不到」和「没排班」是两件事；吞成回落文案会把前者谎报成后者。
    expect(resolveDirectoryLabel('NIGHT', labels, '未排班')).toBe('NIGHT')
    expect(resolveDirectoryLabel('TEAM-B', labels, '未指派班组')).toBe('TEAM-B')
  })

  it('falls back for blank values', () => {
    expect(resolveDirectoryLabel('', labels, '未排班')).toBe('未排班')
    expect(resolveDirectoryLabel('   ', labels, '未排班')).toBe('未排班')
    expect(resolveDirectoryLabel(null, labels, '未排班')).toBe('未排班')
    expect(resolveDirectoryLabel(undefined, labels, '未排班')).toBe('未排班')
  })

  it('never puts a GUID-shaped engineering identifier on screen', () => {
    expect(resolveDirectoryLabel('a3f1c2d4-1111-2222-3333-444455556666', labels, '未排班')).toBe(
      '未排班',
    )
  })
})
