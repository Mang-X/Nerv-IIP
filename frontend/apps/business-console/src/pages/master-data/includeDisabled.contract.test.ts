import { readFileSync, readdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 「包含停用」跨页契约（#1594）。
 *
 * 服务端 `IncludeDisabled` 默认 false：列表页只要不给这个开关，停用后的行就从列表消失，
 * 而 `MasterDataRowActions` 的「启用」只在 `row.active === false` 时出现——行没了，启用入口
 * 永远触达不到，软删除退化成单向操作。这个坑最初是逐页漏掉的（只有物料页有开关），
 * 所以这里按目录扫描兜底：**新增一个主数据列表页却忘了接开关，本用例就红**。
 *
 * 判定口径：页面渲染了 `MasterDataRowActions`（= 有停用/启用行操作）就必须接
 * `IncludeDisabledFilter`，否则它的「启用」入口不可达。不用行操作的页面不在此列。
 */
const pagesDir = dirname(fileURLToPath(import.meta.url))

function pageSources() {
  return readdirSync(pagesDir)
    .filter((name) => name.endsWith('.vue'))
    .map((name) => ({ name, source: readFileSync(join(pagesDir, name), 'utf8') }))
}

/**
 * 判定「真的渲染了」而不是「只 import 了」——只匹配标识符的话，页面把模板里的开关删掉、
 * 留着 import 就能骗过门禁（本用例最初就是这么写的，变异测试当场证伪）。
 */
function rendersFilter(source: string) {
  return source.includes('<IncludeDisabledFilter')
}

describe('主数据列表页的「包含停用」开关', () => {
  it('凡是带停用/启用行操作的页面，都必须提供「包含停用」开关', () => {
    const offenders = pageSources()
      .filter(({ source }) => source.includes('MasterDataRowActions'))
      .filter(({ source }) => !rendersFilter(source))
      .map(({ name }) => name)

    expect(offenders).toEqual([])
  })

  it('接了开关的页面都把它绑到了过滤器上（不是个摆设）', () => {
    const offenders = pageSources()
      .filter((page) => rendersFilter(page.source))
      .filter(({ source }) => !source.includes('useIncludeDisabledFilter('))
      .map(({ name }) => name)

    expect(offenders).toEqual([])
  })

  it('覆盖面回归基线：当前 8 个页面接了开关', () => {
    const wired = pageSources()
      .filter((page) => rendersFilter(page.source))
      .map(({ name }) => name)
      .sort()

    // 物料页（skus）此前就有开关，本次是把另外 7 页补齐；数量下降即为退化。
    expect(wired).toEqual([
      'devices.vue',
      'facilities.vue',
      'organization.vue',
      'partners.vue',
      'reference-data.vue',
      'scheduling.vue',
      'skus.vue',
      'units.vue',
    ])
  })
})
