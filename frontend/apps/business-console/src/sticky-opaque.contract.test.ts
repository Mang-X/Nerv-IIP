import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 吸顶/吸边元素的底色必须不透明——除非它本来就是毛玻璃。
 *
 * 第六轮走查在**排产工作台**拍到：待排池表头写的是 `bg-muted/90`，滚动时
 * 下面的行从那 10% 里透上来，表头那一行同时叠着「加入/工单/物料/交期」和
 * 穿帮的 `WO-2026-03007`、`P1 平台前滑柱总成（左）`、`高风险`。在演示屏上
 * 就是「坏掉了」的观感，而单看代码只是一个不起眼的 `/90`。
 *
 * 判据是**有没有 `backdrop-blur`**：
 *   · 有 → 毛玻璃，半透明是设计（AppHeader、移动端 AppShell 就是这么做的）
 *   · 无 → 纯粹的漏底，滚动内容会直接透上来
 *
 * 所以这里不禁半透明，只禁「半透明又不做模糊」的吸顶底色。
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

/** class 属性值（单/双引号皆可）。 */
const CLASS_RE = /class="([^"]*)"|class='([^']*)'/g
const TRANSLUCENT_BG_RE = /\bbg-[a-z-]+\/\d+/

describe('吸顶元素底色契约', () => {
  /**
   * 光有不透明底色不够——**还得有 z-index**。
   *
   * 第一版只修了透明度，复验截图里表头的文字不再透上来了，可紧迫度那一列的
   * 「高风险」徽标**照样叠在表头上**：徽标自带背景、在 DOM 里又比表头晚绘制，
   * 而 `sticky` 不带 z-index 时不建立层叠上下文，压不住后面的兄弟节点。
   * 只测「底色不透明」会漏掉这一半。
   */
  it('sticky 且有底色时必须带 z-index，否则压不住带背景的行内元素', () => {
    const offenders: string[] = []
    for (const file of walk(srcDir)) {
      const src = readFileSync(file, 'utf8')
      let m: RegExpExecArray | null
      CLASS_RE.lastIndex = 0
      while ((m = CLASS_RE.exec(src))) {
        const classes = m[1] ?? m[2] ?? ''
        if (!/\bsticky\b/.test(classes)) continue
        if (!/\bbg-[a-z-]+/.test(classes)) continue
        if (/\bz-\d+\b|\bz-\[/.test(classes)) continue
        const line = src.slice(0, m.index).split('\n').length
        offenders.push(`${relative(srcDir, file)}:${line}`)
      }
    }
    expect(
      offenders,
      offenders.length
        ? `这些 sticky 元素有底色却没 z-index：${offenders.join('、')}。\n` +
            'sticky 不带 z-index 就不建立层叠上下文，行内带背景的徽标/按钮会叠在它上面' +
            '（走查在排产待排池实拍到「高风险」徽标压住表头）。补一个 z-10 之类即可。'
        : '',
    ).toEqual([])
  })

  it('sticky 的底色不透明，除非配了 backdrop-blur（毛玻璃）', () => {
    const offenders: string[] = []
    for (const file of walk(srcDir)) {
      const src = readFileSync(file, 'utf8')
      let m: RegExpExecArray | null
      CLASS_RE.lastIndex = 0
      while ((m = CLASS_RE.exec(src))) {
        const classes = m[1] ?? m[2] ?? ''
        if (!/\bsticky\b/.test(classes)) continue
        if (!TRANSLUCENT_BG_RE.test(classes)) continue
        if (/\bbackdrop-blur/.test(classes)) continue
        const line = src.slice(0, m.index).split('\n').length
        offenders.push(`${relative(srcDir, file)}:${line}`)
      }
    }
    expect(
      offenders,
      offenders.length
        ? `这些 sticky 元素的底色是半透明且没做模糊：${offenders.join('、')}。\n` +
            '滚动内容会从透明度里透上来（走查在排产待排池实拍到表头与行文字互相叠印）。' +
            '要么去掉 /NN 用不透明底色，要么补 backdrop-blur 做成毛玻璃。'
        : '',
    ).toEqual([])
  })
})
