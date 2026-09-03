import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 半栏栅格内的 placeholder 不承载示例或必要信息。
 *
 * #2706 走查（1440×900）在 `maintenance/work-orders.vue` 新建抽屉抓到症状：
 * 「搜索设备台账或直接输入，如 DEV-SMT-01」在 `sm:grid-cols-2` 的半栏里被截成
 * 「…如 DEV」——用户恰好需要那个示例时看不到它。
 *
 * 真因不是宽度不够，是 placeholder 这个位置本身不该放示例：它在用户开始输入的
 * 那一刻就消失，且对比度天生偏低。示例与补充说明的承载体是 `NvFieldDescription`，
 * 它常驻、不随输入消失。半栏截断只是这个反模式最先暴露出来的地方。
 *
 * 判据取「半栏栅格内、渲染后 ≥16 字」这个保守值：
 *   · 半栏 = 祖先里有 `grid-cols-N`（N≥2，含 `sm:`/`@md:` 等前缀）的容器，
 *     且该格子自身没有 `col-span-2`/`col-span-full` 把整行占满；
 *   · 渲染后 = 动态绑定只量表达式里的字符串字面量，模板串先剔除 `${…}` 插值。
 *     否则 `` :placeholder="`自动合计 ${round2(x)}`" `` 会因为表达式长被误判，
 *     而它渲染出来只有「自动合计 0」——那是活的计算值，不是示例。
 *
 * 注意：不要用 `input.scrollWidth === clientWidth` 证明 placeholder 未被截断。
 * 空值未聚焦的 `<input>`，placeholder 根本不计入 `scrollWidth`，该等式恒成立。
 * #2706 走查已实证读数本身有效（填满字符时 2800 > 453），但它对 placeholder
 * 零鉴别力。所以这里拦的是写法，不是量出来的像素。
 */

const srcDir = dirname(fileURLToPath(import.meta.url))

/** 渲染后达到这个字数就判违规——半栏容不下，示例必须改由 `NvFieldDescription` 承载。 */
const PLACEHOLDER_LENGTH_LIMIT = 16

const GRID = /(?:^|[\s:])grid-cols-[2-9]/
const SPAN = /(?:^|[\s:])col-span-(?:full|[2-9])/
const TAG = /<(\/?)([A-Za-z][\w.-]*)((?:"[^"]*"|'[^']*'|[^>])*?)(\/?)>/g
const PLACEHOLDER = /(?:^|\s)(:?)placeholder\s*=\s*"([^"]*)"/
const CLASS = /(?:^|\s):?class\s*=\s*"([^"]*)"/g
/** 自闭合与否由 HTML 规则决定的原生空元素；自定义组件一律按成对标签处理。 */
const VOID_TAGS = new Set(['input', 'br', 'hr', 'img', 'meta', 'link', 'source', 'area', 'col'])

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

/** 静态值直接取；动态绑定取表达式里的字符串字面量（三元的每个分支都要量）。 */
function renderedTexts(dynamic: boolean, raw: string): string[] {
  const values = dynamic
    ? [...raw.matchAll(/'([^']*)'|`([^`]*)`/g)].map((m) => m[1] ?? m[2] ?? '')
    : [raw]
  return values.map((v) => v.replace(/\$\{[^}]*\}/g, '').trim()).filter(Boolean)
}

function findOffenders(source: string): Array<{ line: number; text: string }> {
  const offenders: Array<{ line: number; text: string }> = []
  const stack: Array<{ tag: string; grid: boolean; halfCell: boolean }> = []

  for (const m of source.matchAll(TAG)) {
    const [, closing, tag, attrs, selfClosing] = m
    if (closing) {
      const i = stack.findLastIndex((f) => f.tag === tag)
      if (i >= 0) stack.length = i
      continue
    }
    const classes = [...attrs.matchAll(CLASS)].map((c) => c[1]).join(' ')
    const halfCell = (stack.at(-1)?.grid ?? false) && !SPAN.test(classes)
    const inHalfCell = halfCell || stack.some((f) => f.halfCell)

    const ph = PLACEHOLDER.exec(attrs)
    if (ph && inHalfCell) {
      const line = source.slice(0, m.index).split('\n').length
      for (const text of renderedTexts(ph[1] === ':', ph[2])) {
        if ([...text].length >= PLACEHOLDER_LENGTH_LIMIT) offenders.push({ line, text })
      }
    }

    if (!selfClosing && !VOID_TAGS.has(tag.toLowerCase())) {
      stack.push({ tag, grid: GRID.test(classes), halfCell })
    }
  }
  return offenders
}

describe('半栏栅格 placeholder 契约', () => {
  it('半栏栅格内不出现承载示例的长 placeholder', () => {
    const offenders: string[] = []
    for (const file of walk(srcDir)) {
      for (const { line, text } of findOffenders(readFileSync(file, 'utf8'))) {
        offenders.push(`${relative(srcDir, file)}:${line}「${text}」`)
      }
    }
    expect(
      offenders,
      offenders.length
        ? `这些半栏栅格里的 placeholder 达到 ${PLACEHOLDER_LENGTH_LIMIT} 字：${offenders.join('、')}。\n` +
            'placeholder 会在用户开始输入时消失，装不下示例——把示例移到 <NvFieldDescription>，placeholder 只留极短提示或留空。'
        : '',
    ).toEqual([])
  })

  it('判据本身有鉴别力：能认出半栏长 placeholder，也不误伤整行与插值', () => {
    const violating = `
      <div class="grid gap-3 sm:grid-cols-2">
        <NvField><NvInput placeholder="搜索设备台账或直接输入，如 DEV-SMT-01" /></NvField>
      </div>`
    expect(findOffenders(violating).map((o) => o.text)).toEqual([
      '搜索设备台账或直接输入，如 DEV-SMT-01',
    ])

    const compliant = `
      <div class="grid gap-3 sm:grid-cols-2">
        <NvField class="sm:col-span-2"><NvInput placeholder="设备停下来了才填，如：主轴异响，无法运转" /></NvField>
        <NvField><NvInput :placeholder="\`自动合计 \${round2(autoSparePartCost)}\`" /></NvField>
        <NvField><NvInput placeholder="搜索设备台账" /></NvField>
      </div>
      <NvInput placeholder="搜索设备台账或直接输入，如 DEV-SMT-01" />`
    expect(findOffenders(compliant)).toEqual([])
  })
})
