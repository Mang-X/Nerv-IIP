import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 抽屉/卡片内部的多列版式必须按**容器**宽度决定，不能按视口。
 *
 * 第六轮走查连撞两处，症状一模一样——文字被压成一列单字、控件顶出容器：
 *   · 设备健康卡：`sm:grid-cols-[minmax(0,220px)_...]`，卡实际只有约 360px，
 *     220px 固定列吃掉大半，右侧 dl 只剩 20 来 px，「暂无可追溯记录」竖排且溢出。
 *   · 维护工单「更换备件」行：`sm:grid-cols-[1fr_5rem_8rem_6rem_auto]`，
 *     行待在 512px 抽屉里，固定列加间距吃掉约 368px，物料选择器被压成「选…」。
 *
 * 共同真因：`sm:` / `md:` 是**视口**断点。视口 1366px 时规则一律生效，
 * 而元素真正待着的抽屉只有 512px、卡片只有 360px——断点问的是错误的宽度。
 *
 * 这条契约拦的是「在明显窄容器里用视口断点排多列」。判据取保守值：
 * 只看**带固定列宽（rem/px）的 grid-cols 模板**，纯 `1fr` 或 `repeat()` 不管——
 * 那些会自适应，不会把某一列压到读不了。
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

/**
 * 视口断点 + 自定义 grid 模板，且模板里含固定 rem/px 列宽。
 *
 * 前置的 `(?<![@\w-])` 不能省：容器断点写作 `@md:`，而 `\b` 在 `@` 与 `m` 之间**是成立的**
 * ——第一版就是这么写的，护栏当场把刚改好的 `@md:grid-cols-[...]` 判成违规。
 */
const VIEWPORT_FIXED_GRID = /(?<![@\w-])(?:sm|md|lg|xl|2xl):grid-cols-\[[^\]]*\d+(?:rem|px)[^\]]*\]/

/** 已知待在窄容器里的文件：抽屉内容、卡片组件。名单显式列出，避免误伤整页布局。 */
const NARROW_CONTAINERS = [/components\/equipment\/EquipmentHealthCard\.vue$/]

describe('窄容器版式契约', () => {
  it('已知窄容器里不用视口断点排带固定列宽的多列', () => {
    const offenders: string[] = []
    for (const file of walk(srcDir)) {
      const rel = relative(srcDir, file)
      if (!NARROW_CONTAINERS.some((re) => re.test(rel))) continue
      const src = readFileSync(file, 'utf8')
      src.split('\n').forEach((line, i) => {
        if (VIEWPORT_FIXED_GRID.test(line)) offenders.push(`${rel}:${i + 1}`)
      })
    }
    expect(
      offenders,
      offenders.length
        ? `这些窄容器用了视口断点排固定列宽：${offenders.join('、')}。\n` +
            '视口宽不代表容器宽——改用容器查询（父级加 `@container`，断点用 `@md:` 等）。'
        : '',
    ).toEqual([])
  })
})
