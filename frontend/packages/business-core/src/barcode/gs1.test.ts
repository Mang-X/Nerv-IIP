import { describe, expect, it } from 'vitest'
import { parseGs1 } from './gs1'

const GS = String.fromCharCode(29)

describe('parseGs1', () => {
  it('解析 (01)GTIN (17)效期 (10)批号：GS 分隔变长字段', () => {
    const raw = `010614141000015517261231` + `10` + `LOT-A42` + GS + `21` + `SN9`
    const f = parseGs1(raw)
    expect(f).not.toBeNull()
    expect(f!.gtin).toBe('06141410000155')
    expect(f!.expiryDate).toBe('2026-12-31')
    expect(f!.lotNo).toBe('LOT-A42')
    expect(f!.serialNo).toBe('SN9')
  })

  it('(17) 效期 DD=00 取当月最后一天', () => {
    const f = parseGs1('17260200' + '10BATCH1')
    expect(f!.expiryDate).toBe('2026-02-28')
  })

  it('(11) 生产日期解析', () => {
    const f = parseGs1('11260701' + '10L1')
    expect(f!.productionDate).toBe('2026-07-01')
  })

  it('批号为末尾变长字段（无 GS 结束）读到串尾', () => {
    const f = parseGs1('1726123110LOT-TAIL-9')
    expect(f!.expiryDate).toBe('2026-12-31')
    expect(f!.lotNo).toBe('LOT-TAIL-9')
  })

  it('去除符号学标识符 ]C1 / ]d2 前缀', () => {
    expect(parseGs1(']C1' + '10ABC')!.lotNo).toBe('ABC')
    expect(parseGs1(']d2' + '17261231')!.expiryDate).toBe('2026-12-31')
  })

  it('前导 FNC1 被跳过', () => {
    expect(parseGs1(GS + '10XYZ')!.lotNo).toBe('XYZ')
  })

  it('非 GS1 码（无可识别 AI）返回 null', () => {
    expect(parseGs1('IB-2026-0001')).toBeNull()
    expect(parseGs1('')).toBeNull()
    expect(parseGs1(null)).toBeNull()
  })

  it('非法 (17) 效期不写入 expiryDate 但仍解析出批号', () => {
    const f = parseGs1('17261340' + GS + '10L9')
    expect(f).not.toBeNull()
    expect(f!.expiryDate).toBeUndefined()
    expect(f!.lotNo).toBe('L9')
  })
})
