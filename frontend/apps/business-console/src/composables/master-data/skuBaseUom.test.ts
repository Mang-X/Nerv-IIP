import { describe, expect, it } from 'vitest'
import { shallowRef } from 'vue'

import { toBaseUomBySku } from './skuBaseUom'

// 单位是物料主档的事实（钢材 kg / 油品 l / 计件件号才是 pcs）。这套口径原先在
// useSkuNames / useInventoryScope / useErpPickerCatalog / useEquipmentPickerCatalog 里
// 各抄一遍，任一处忘了 trim 就会出现「同一物料 A 页带得出单位、B 页带不出」。
describe('toBaseUomBySku', () => {
  it('按编码索引基本单位，编码与单位都 trim', () => {
    const map = toBaseUomBySku([
      { code: ' RM-BAR-01 ', baseUomCode: ' kg ' },
      { code: 'FG-A', baseUomCode: 'pcs' },
    ])

    expect(map.value.get('RM-BAR-01')).toBe('kg')
    expect(map.value.get('FG-A')).toBe('pcs')
  })

  it('编码或单位缺失的行整行跳过，绝不猜一个通用单位', () => {
    const map = toBaseUomBySku([
      { code: 'NO-UOM' },
      { code: 'BLANK-UOM', baseUomCode: '   ' },
      { code: '  ', baseUomCode: 'kg' },
      { code: null, baseUomCode: null },
    ])

    expect(map.value.size).toBe(0)
    expect(map.value.get('NO-UOM')).toBeUndefined()
  })

  it('接受 getter，随源数据到达重算（目录是异步读面）', () => {
    const skus = shallowRef<{ code?: string; baseUomCode?: string }[]>([])
    const map = toBaseUomBySku(() => skus.value)

    expect(map.value.size).toBe(0)
    skus.value = [{ code: 'FG-B', baseUomCode: 'kg' }]
    expect(map.value.get('FG-B')).toBe('kg')
  })
})
