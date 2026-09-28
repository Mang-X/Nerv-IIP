import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { WMS_LINE_OWNER_TYPE_OPTIONS } from './businessLabels'

/**
 * WMS 入库单、出库单行的货主类型下拉必须只给出 Inventory 接受的值（#3930）。
 *
 * 为什么读后端源码：WMS 行上的 `ownerType` 在契约里是裸 string，前端拿不到机读的取值集合；
 * 它原样进入库存预留与过账，由 `StockOwnerType.Normalize` 判定，表外值（例如曾经的 `consignment`）
 * 会在过账时被拒成「库存归属类型无效」。能让两边漂移当场变红的权威来源只有那份别名表。
 */
const STOCK_OWNER_TYPE_SOURCE = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../../../backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockOwnerType.cs',
)

/** `StockOwnerType.Normalize` 接受的全部输入：规范值常量 + 别名表里的字面量键（大小写不敏感）。 */
function acceptedOwnerTypes(): Set<string> {
  const source = readFileSync(STOCK_OWNER_TYPE_SOURCE, 'utf8')
  const canonical = [...source.matchAll(/public const string \w+ = "([^"]+)";/g)].map((m) => m[1]!)
  const aliases = [...source.matchAll(/\["([^"]+)"\] = \w+,/g)].map((m) => m[1]!)
  expect(
    canonical.length,
    'StockOwnerType.cs 里没解析出任何规范值常量，扫描已失效',
  ).toBeGreaterThan(0)
  expect(aliases.length, 'StockOwnerType.cs 里没解析出任何字面量别名，扫描已失效').toBeGreaterThan(
    0,
  )
  return new Set([...canonical, ...aliases].map((value) => value.toLowerCase()))
}

describe('WMS 行货主类型下拉与库存归属取值的契约', () => {
  const accepted = acceptedOwnerTypes()

  it('解析出的取值集合有鉴别力：认得本公司别名，不认寄售这类表外值', () => {
    expect(accepted.has('company')).toBe(true)
    expect(accepted.has('owned')).toBe(true)
    expect(accepted.has('consignment')).toBe(false)
  })

  it('下拉里的每个值都是库存侧接受的货主类型', () => {
    expect(WMS_LINE_OWNER_TYPE_OPTIONS.length).toBeGreaterThan(0)
    for (const option of WMS_LINE_OWNER_TYPE_OPTIONS) {
      expect(accepted.has(option.value.toLowerCase()), `${option.label}（${option.value}）`).toBe(
        true,
      )
    }
  })

  it('下拉的值与中文名都不重复', () => {
    const values = WMS_LINE_OWNER_TYPE_OPTIONS.map((option) => option.value)
    const labels = WMS_LINE_OWNER_TYPE_OPTIONS.map((option) => option.label)
    expect(new Set(values).size).toBe(values.length)
    expect(new Set(labels).size).toBe(labels.length)
  })
})
