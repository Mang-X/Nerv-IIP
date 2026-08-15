import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * NvField 方向必须走 `orientation` prop，不能用 class 手搓成横排。
 *
 * 起因是第六轮走查在 NCR 处置抽屉里拍到的：「本人代表 MRB 评审通过该处置方案」
 * 这行标签被压成**每行一个字**竖着排，勾选框被挤到抽屉外面。
 *
 * 真因不在页面的 flex 写法，而在 `nvFieldVariants` 的默认档：
 *   vertical: 'flex-col *:w-full …'
 * `*:w-full` 给**每个直接子元素**钉死 `width:100%`。页面补一句 `class="flex-row"`
 * 只改了主轴方向，`*:w-full` 原样保留——于是一行里并排两个 100% 宽的子项：
 * 标签被压缩到最小内容宽（中文任意位置可断行，就成了一列单字），
 * 而勾选框撑着自己那份 100% 顶出容器。
 *
 * `horizontal` 档本来就是为这个场景准备的（`flex-row items-center` +
 * 标签 `flex-auto`，且**不带** `*:w-full`）。所以这里禁的是 class 手搓，
 * 而不是横排本身。
 */

const srcDir = dirname(fileURLToPath(import.meta.url))

function walk(dir: string): string[] {
  const out: string[] = []
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'node_modules' || e.name === 'dist') continue
    const full = join(dir, e.name)
    if (e.isDirectory()) out.push(...walk(full))
    else if (e.name.endsWith('.vue')) out.push(full)
  }
  return out
}

/** `<NvField ... >` 开标签，含跨行属性。 */
const OPEN_TAG_RE = /<NvField(\s[^>]*?)?\/?>/gs

describe('NvField 方向契约', () => {
  it('页面不用 class 手搓横排，改用 orientation="horizontal"', () => {
    const offenders: string[] = []
    for (const file of walk(srcDir)) {
      const src = readFileSync(file, 'utf8')
      let m: RegExpExecArray | null
      OPEN_TAG_RE.lastIndex = 0
      while ((m = OPEN_TAG_RE.exec(src))) {
        const attrs = m[1] ?? ''
        // 只看写进 class 的方向类；orientation prop 怎么写都放行。
        const classAttr = /\bclass="([^"]*)"/.exec(attrs)?.[1] ?? ''
        if (!/\bflex-row\b/.test(classAttr)) continue
        const line = src.slice(0, m.index).split('\n').length
        offenders.push(`${relative(srcDir, file)}:${line}`)
      }
    }
    expect(
      offenders,
      offenders.length
        ? `这些 NvField 用 class 手搓了横排：${offenders.join('、')}。\n` +
            'vertical 档带 `*:w-full`（每个子元素 width:100%），只补 flex-row 改不掉它，' +
            '结果标签被压成一列单字、控件顶出容器。改用 orientation="horizontal"。'
        : '',
    ).toEqual([])
  })
})
