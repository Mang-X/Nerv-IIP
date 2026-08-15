import { describe, expect, it } from 'vitest'

import {
  resolveWorkCenterFamily,
  WORK_CENTER_FAMILIES,
  WORK_CENTER_FAMILY_LIST,
} from './workCenterFamilies'

describe('resolveWorkCenterFamily', () => {
  it('takes the master-data category over the code prefix', () => {
    // WC-CNC-01 的编码前缀会兜底到「机加」，但主数据说它是检测工位——以主数据为准。
    expect(resolveWorkCenterFamily('WC-CNC-01', 'inspection')).toEqual(
      WORK_CENTER_FAMILIES.inspection,
    )
    expect(resolveWorkCenterFamily('WC-CNC-01', '检测')).toEqual(WORK_CENTER_FAMILIES.inspection)
    expect(resolveWorkCenterFamily('WC-CNC-01', 'Surface-Treatment')).toEqual(
      WORK_CENTER_FAMILIES.surfaceTreatment,
    )
  })

  it('falls back to the configurable code prefix only when master data has no category', () => {
    expect(resolveWorkCenterFamily('WC-CNC-01')).toEqual(WORK_CENTER_FAMILIES.machining)
    expect(resolveWorkCenterFamily('WC-PK-02')).toEqual(WORK_CENTER_FAMILIES.packaging)
    expect(resolveWorkCenterFamily('WC-TS-01')).toEqual(WORK_CENTER_FAMILIES.inspection)
    // 长前缀优先：WC-SCALE-ROD 是装配线，不能被 WC-ROD（机加）抢走。
    expect(resolveWorkCenterFamily('WC-SCALE-ROD-01')).toEqual(WORK_CENTER_FAMILIES.assembly)
    expect(resolveWorkCenterFamily('WC-ROD-01')).toEqual(WORK_CENTER_FAMILIES.machining)
  })

  it('returns undefined instead of guessing for unknown work centers', () => {
    expect(resolveWorkCenterFamily('WC-AUX-01')).toBeUndefined()
    expect(resolveWorkCenterFamily(undefined)).toBeUndefined()
    expect(resolveWorkCenterFamily('WC-AUX-01', 'no-such-category')).toBeUndefined()
  })

  it('maps every family onto a colour slot that matches its meaning', () => {
    // 曾踩坑：包装借用 cut（冲压/切割）槽、检测借用 bend（折弯）槽，
    // 色槽名与工序语义对不上，读代码的人被直接误导。
    expect(WORK_CENTER_FAMILIES.packaging.key).toBe('pack')
    expect(WORK_CENTER_FAMILIES.inspection.key).toBe('insp')
    expect(WORK_CENTER_FAMILIES.assembly.key).toBe('assy')
    expect(WORK_CENTER_FAMILY_LIST.map((f) => f.key)).not.toContain('cut')
    expect(WORK_CENTER_FAMILY_LIST.map((f) => f.key)).not.toContain('bend')
  })

  it('gives every family its own colour slot', () => {
    const keys = WORK_CENTER_FAMILY_LIST.map((family) => family.key)
    expect(new Set(keys).size).toBe(keys.length)
  })
})
