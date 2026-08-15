import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * NvUI 受控绑定 guard（MAN-689 / #1257）。
 *
 * NvUI 的表单件转发 reka-ui 原语，受控入参统一是 `modelValue` /
 * `update:modelValue`。`checked` 不是任何 `Nv*` 组件的 prop —— 写成
 * `v-model:checked` / `:checked` / `@update:checked` 时 Vue 不会报错，属性会掉进
 * attrs 成为死属性、事件永不触发，UI 看着能勾但外部 state 永远不动
 * （排产池「生成首版」按钮永久禁用即由此而来）。typecheck 抓不到，靠本测试兜。
 *
 * 原生 `<input type="checkbox" :checked>` 合法，不在扫描范围内 —— 只扫 `<Nv*` 标签。
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

const files = walk(srcDir)

// 一个 `<NvXxx ...>` 开标签（含跨行属性）。
const NV_TAG_RE = /<Nv[A-Za-z0-9]*(\s[^>]*?)?\/?>/gs
const BAD_ATTR_RE = /(?:v-model:checked|@update:checked|(?<![\w-]):checked=|(?<![\w-])checked=)/

describe('NvUI 受控绑定：Nv* 组件只认 model-value', () => {
  it('found app source files to guard', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  it('没有任何 Nv* 组件使用 checked / v-model:checked / @update:checked', () => {
    const offenders: string[] = []
    for (const file of files) {
      const src = readFileSync(file, 'utf8')
      NV_TAG_RE.lastIndex = 0
      let m: RegExpExecArray | null
      while ((m = NV_TAG_RE.exec(src))) {
        if (!BAD_ATTR_RE.test(m[0])) continue
        const line = src.slice(0, m.index).split('\n').length
        offenders.push(`${relative(srcDir, file).replace(/\\/g, '/')}:${line}`)
      }
    }
    expect(
      offenders,
      'Nv* 表单件的受控入参是 model-value / update:model-value —— `checked` 会静默失效',
    ).toEqual([])
  })
})
