import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 取样式源文的两个坑(踩过,别改回去):
 * 1. `import CSS from './scheduling.css?raw'` 在本仓的 vite-plus 里返回**空串**——CSS 插件
 *    先接管了该请求,`?raw` 不生效,测试会假绿(色板解析出 0 项,去重断言全部空过)。
 * 2. `new URL('./scheduling.css', import.meta.url)` 会被 Vite 的资源处理**重写成资源 URL**,
 *    再 `fileURLToPath` 就抛 "The URL must be of scheme file"。
 * 所以先把本文件自身的 URL 转成路径再 join,不出现那个会被重写的字面量模式。
 */
const CSS = readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'scheduling.css'), 'utf8')

/**
 * 工序分色色板去重门禁(#1399 M1)。
 *
 * 背景:色槽是"补了名、抄了色"——`assy` 抄 `cut`、`insp` 抄 `bend`、`pack` 与 `cut` 只差
 * 10 色相 0.02 明度。实机图例上「装配」与「包装」并排肉眼分不出,调度员会读错工序族。
 * 这不是靠 code review 能守住的:改色时人眼看不出 oklch 数值挨得多近。
 *
 * 所以按色相角实测断言。阈值 35° 是本轮实测(1680×950 明/暗双主题、10% 不透明度填充 +
 * 实色描边)下并排色块仍可分辨的下限。
 */

/** 最小可分辨色相差(度)。 */
const MIN_HUE_DISTANCE = 35

/** 业务侧 `workCenterFamilies.ts` 声明的六个工序族 + 预览/未来用的 cut/bend。 */
const EXPECTED_SLOTS = ['cut', 'bend', 'weld', 'mach', 'paint', 'pack', 'assy', 'insp'] as const

interface Oklch {
  l: number
  c: number
  h: number
}

function parsePalette(): Map<string, Oklch> {
  const out = new Map<string, Oklch>()
  const re =
    /--nv-scheduling-category-([a-z]+)\s*:\s*oklch\(\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\)/g
  for (const m of CSS.matchAll(re)) {
    out.set(m[1], { l: Number(m[2]), c: Number(m[3]), h: Number(m[4]) })
  }
  return out
}

/** 色相是角度,359 与 1 相差 2 而不是 358。 */
function hueDistance(a: number, b: number): number {
  const d = Math.abs(a - b) % 360
  return d > 180 ? 360 - d : d
}

describe('工序分色色板', () => {
  const palette = parsePalette()

  it('八个工序色槽都有定义,且都是 oklch 字面量', () => {
    expect([...palette.keys()].sort()).toEqual([...EXPECTED_SLOTS].sort())
  })

  it('任意两个色槽的色相差不小于 35°(装配/包装曾经肉眼同色)', () => {
    const slots = [...palette.entries()]
    const tooClose: string[] = []
    for (let i = 0; i < slots.length; i += 1) {
      for (let j = i + 1; j < slots.length; j += 1) {
        const [nameA, a] = slots[i]
        const [nameB, b] = slots[j]
        const distance = hueDistance(a.h, b.h)
        if (distance < MIN_HUE_DISTANCE) {
          tooClose.push(`${nameA}(h=${a.h}) ↔ ${nameB}(h=${b.h}) 仅差 ${distance.toFixed(1)}°`)
        }
      }
    }
    expect(tooClose).toEqual([])
  })

  it('没有两个色槽是完全相同的颜色', () => {
    const seen = new Map<string, string>()
    const duplicates: string[] = []
    for (const [name, v] of palette) {
      const key = `${v.l}/${v.c}/${v.h}`
      const previous = seen.get(key)
      if (previous) duplicates.push(`${previous} 与 ${name} 颜色完全相同(${key})`)
      else seen.set(key, name)
    }
    expect(duplicates).toEqual([])
  })
})
