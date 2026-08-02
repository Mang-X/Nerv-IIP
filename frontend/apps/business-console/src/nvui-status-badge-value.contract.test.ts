import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * `NvStatusBadge` 的 `value` 收**裸码值**，`label` 才收现成文案。
 *
 * 把翻译结果再喂给翻译器，是这个仓库反复犯的同一种错——第六轮走查一次就抓到六处：
 *   · 维护工单：`:value="priorityLabel(row.priority)"`（拿到的是「紧急」「高」「中」）
 *   · 维护工单：`:value="warrantyStatusLabel(...)"`（缺值时是占位符「—」）
 *   · 点检记录 / ERP 总览 / WMS 收货门禁：同款
 *
 * 后果不是白屏——`resolveStatus` 查不到就回吐原值，屏上恰好还是对的中文，
 * **人眼看不出来**。露出来的只有开发期一句「词表缺失: 紧急」，而那条频道之前
 * 又被假警报刷满了。所以这里改成静态拦截：翻译函数的返回值不许进 `value`。
 *
 * 正确写法是两个都给：
 *
 *     <NvStatusBadge :value="row.priority" :label="priorityLabel(row.priority)" />
 *
 * `value` 供 tone 解析（词表命中就有正确色），`label` 定文案。
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

/** `<NvStatusBadge …>` 开标签，含跨行属性。 */
const OPEN_TAG_RE = /<NvStatusBadge(\s[^>]*?)?\/?>/gs
/** `:value="someLabel(...)"` / `:value="xxxText(...)"` —— 翻译函数的返回值。 */
const LABEL_CALL_RE = /:value="\s*[A-Za-z0-9_.]*(?:Label|Text|Caption)\s*\(/

describe('NvStatusBadge value 契约', () => {
  it('value 不接翻译函数的返回值，现成文案走 label', () => {
    const offenders: string[] = []
    for (const file of walk(srcDir)) {
      const src = readFileSync(file, 'utf8')
      let m: RegExpExecArray | null
      OPEN_TAG_RE.lastIndex = 0
      while ((m = OPEN_TAG_RE.exec(src))) {
        const attrs = m[1] ?? ''
        if (!LABEL_CALL_RE.test(attrs)) continue
        const line = src.slice(0, m.index).split('\n').length
        offenders.push(`${relative(srcDir, file)}:${line}`)
      }
    }
    expect(
      offenders,
      offenders.length
        ? `这些 NvStatusBadge 把翻译结果塞进了 value：${offenders.join('、')}。\n` +
            'value 收裸码值（供 tone 解析），现成文案走 label：' +
            ':value="row.priority" :label="priorityLabel(row.priority)"。'
        : '',
    ).toEqual([])
  })
})
