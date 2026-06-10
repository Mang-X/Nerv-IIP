import { describe, expect, it } from 'vitest'

import { mergeReferenceOptions } from './masterDataReference'

const FALLBACK = [
  { value: 'none', label: '不管理' },
  { value: 'lot', label: '按批次追踪' },
]

describe('mergeReferenceOptions', () => {
  it('后端英文名 → 用常量中文覆盖（含 system-managed 改不动的码）', () => {
    const out = mergeReferenceOptions([{ code: 'none', displayName: 'No Batch Tracking', active: true }], FALLBACK)
    expect(out).toEqual([{ value: 'none', label: '不管理' }])
  })

  it('后端已是中文名 → 尊重（工厂在字典里的改名不被覆盖）', () => {
    const out = mergeReferenceOptions([{ code: 'none', displayName: '我的自定义名', active: true }], FALLBACK)
    expect(out[0]!.label).toBe('我的自定义名')
  })

  it('常量无该 code 的英文 → 原样用后端名', () => {
    const out = mergeReferenceOptions([{ code: 'xyz', displayName: 'Custom Unit', active: true }], FALLBACK)
    expect(out[0]!.label).toBe('Custom Unit')
  })

  it('过滤停用 / 空 code；实时为空整体回退常量', () => {
    expect(mergeReferenceOptions([{ code: 'none', displayName: 'x', active: false }], FALLBACK)).toEqual(FALLBACK)
    expect(mergeReferenceOptions([{ code: '', displayName: 'y', active: true }], FALLBACK)).toEqual(FALLBACK)
    expect(mergeReferenceOptions([], FALLBACK)).toEqual(FALLBACK)
  })
})
