import { readFileSync, readdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 停用/启用确认框「页面层单实例」契约（#1591）。
 *
 * `confirm-destroy.md` 规则 5：确认框声明在 `v-for` 外、由 `target` 指向当前行。此前确认框装在
 * `MasterDataRowActions` 内部随行渲染，**一页 N 行就是 N 个 `NvAlertDialog`**；而各页的组件测试
 * 都用 stub 把弹层抹平了，**测不出实例数**——所以这条结构缺陷能一直躺着。
 *
 * 本文件按目录扫描兜底：
 * 1. 行操作组件里不得再出现确认框；
 * 2. 用行操作的页面必须**恰好一次**渲染 `MasterDataLifecycleDialog`（多张表共用同一个）。
 *
 * 运行时的实例计数断言在 `lifecycleDialogSingleInstance.runtime.test.ts`（真挂一页数组件实例）。
 */
const pagesDir = dirname(fileURLToPath(import.meta.url))
const rowActionsPath = join(pagesDir, '../../components/masterData/MasterDataRowActions.vue')

function pageSources() {
  return readdirSync(pagesDir)
    .filter((name) => name.endsWith('.vue'))
    .map((name) => ({ name, source: readFileSync(join(pagesDir, name), 'utf8') }))
}

function countOccurrences(source: string, needle: string) {
  return source.split(needle).length - 1
}

describe('停用/启用确认框的页面层单实例契约', () => {
  it('行操作组件本身不再承载确认框', () => {
    const source = readFileSync(rowActionsPath, 'utf8')
    expect(source).not.toContain('NvAlertDialog')
  })

  it('用行操作的页面都渲染了页面层确认框', () => {
    const offenders = pageSources()
      .filter(({ source }) => source.includes('<MasterDataRowActions'))
      .filter(({ source }) => !source.includes('<MasterDataLifecycleDialog'))
      .map(({ name }) => name)

    expect(offenders).toEqual([])
  })

  it('每页最多渲染一个确认框实例——多张表共用同一个，不是每表一个', () => {
    const offenders = pageSources()
      .map(({ name, source }) => ({
        name,
        dialogs: countOccurrences(source, '<MasterDataLifecycleDialog'),
        triggers: countOccurrences(source, '<MasterDataRowActions'),
      }))
      .filter((page) => page.triggers > 0 && page.dialogs !== 1)

    expect(offenders).toEqual([])
  })

  it('确认框不得写在 v-for / 表格单元格插槽里（那就又变回按行实例化）', () => {
    const offenders = pageSources()
      .filter(({ source }) => source.includes('<MasterDataLifecycleDialog'))
      .filter(({ source }) => {
        const index = source.indexOf('<MasterDataLifecycleDialog')
        // 往前找最近的作用域插槽/循环开标签：确认框应当在它们之外。
        const before = source.slice(0, index)
        const lastCellSlot = before.lastIndexOf('#cell-')
        const lastTableClose = before.lastIndexOf('</NvDataTable>')
        return lastCellSlot > lastTableClose
      })
      .map(({ name }) => name)

    expect(offenders).toEqual([])
  })

  it('覆盖面回归基线：当前 8 个页面各有一个确认框', () => {
    const wired = pageSources()
      .filter(({ source }) => source.includes('<MasterDataLifecycleDialog'))
      .map(({ name }) => name)
      .sort()

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
