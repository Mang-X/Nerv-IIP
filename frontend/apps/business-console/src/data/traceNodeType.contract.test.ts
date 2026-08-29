import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
  normalizeCode,
  TRACE_NODE_TYPE_LABELS,
  TRACE_NODE_TYPE_OPEN_SET_KEYS,
} from './businessLabels'

/**
 * 追溯图「类型」列的词表与后端节点类型全集之间的完备性契约。
 *
 * 为什么读后端源码：追溯节点类型不进 OpenAPI（`nodeType` 是裸 string），前端拿不到任何机读的
 * 取值集合。此前两边各写各的词表，只在四个键上碰巧对得上，其余节点在界面上印 `ProductionReport`
 * 这样的英文码，而两边都绿。能让「后端新增一类节点」当场变红的权威来源，只有那个封闭类型本身。
 *
 * **「后端会不会绕开这张表另发一种类型」不在本文件的职责里**——那由编译器管：
 * `MesTraceabilityNodeType` 构造函数私有、字符串无到本类型的隐式转换，调用点写不出表外的**字面量**。
 * 本文件只管两件事：表里的每一项都有中文说法；词表里没有表外的死键。
 * 加上第三条，看住那个封闭性上仅剩的口子。
 */

const BACKEND_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../../backend')
const MES_WEB_SRC = join(BACKEND_ROOT, 'services/Business/Mes/src/Nerv.IIP.Business.Mes.Web')

/**
 * 跨服务承重的节点类型词表。检验结论那个值被 BusinessGateway 的追溯门面用来按
 * `business.mes.quality.read` 裁剪节点，故它下沉到了公共 Contracts，MES 侧只能引用不能写字面量
 * （#2686 / PR #2693，后端词表漂移门禁扫 services 下的 Application 与 Seed 目录）。
 * 于是封闭类型的字段取值有两种形态：字面量，和指向本文件的常量引用。
 */
const CONTRACTS_MES_VOCABULARY = join(
  BACKEND_ROOT,
  'common/Contracts/Nerv.IIP.Contracts.Mes/MesTraceability.cs',
)
const NODE_TYPE_DECLARATION = join(
  MES_WEB_SRC,
  'Application/Queries/Workbench/MesWorkbenchQueries.cs',
)

const declarationSource = readFileSync(NODE_TYPE_DECLARATION, 'utf8')

/** 封闭类型 `MesTraceabilityNodeType` 的类型体。 */
function nodeTypeBody(): string {
  const start = declarationSource.indexOf('public sealed record MesTraceabilityNodeType')
  expect(start, 'MesTraceabilityNodeType 已被改名或移走').toBeGreaterThanOrEqual(0)
  return declarationSource.slice(start, declarationSource.indexOf('\n}', start))
}

/** `Contracts.Mes` 里公开的节点类型词表常量：符号名 → 取值。 */
function contractsVocabulary(): Map<string, string> {
  const source = readFileSync(CONTRACTS_MES_VOCABULARY, 'utf8')
  const constants = [...source.matchAll(/public const string (\w+) = "([^"]+)";/g)]
  expect(
    constants.length,
    `${relative(BACKEND_ROOT, CONTRACTS_MES_VOCABULARY)} 里一个词表常量都没解析出来，扫描已失效`,
  ).toBeGreaterThan(0)
  return new Map(constants.map((m) => [m[1]!, m[2]!]))
}

/** `MesTraceabilityNodeType` 上登记的受控节点类型取值。 */
function declaredNodeTypes(): string[] {
  const body = nodeTypeBody()

  // 字段取值有两种合法形态：wire 字面量，或指向 Contracts 词表常量的符号引用。
  const fields = [
    ...body.matchAll(/public static readonly MesTraceabilityNodeType \w+ = new\(([^)]*)\);/g),
  ]
  // 只用取值正则会有一种假绿：字段写法一变，它一条都匹配不上，missing 恒为空。
  // 所以拿一条更宽松的声明正则做校准，两者数量必须一致。
  const declarations = body.match(/public static readonly MesTraceabilityNodeType /g) ?? []
  expect(
    fields.length,
    `节点类型字段解析不全（认出 ${fields.length} / 声明 ${declarations.length}），扫描已失效`,
  ).toBe(declarations.length)
  expect(fields.length).toBeGreaterThan(0)

  const vocabulary = contractsVocabulary()
  const values: string[] = []
  const unresolved: string[] = []
  for (const field of fields) {
    const initializer = field[1]!.trim()
    const literal = /^"([^"]*)"$/.exec(initializer)
    if (literal) {
      values.push(literal[1]!)
      continue
    }
    const symbol = /^(?:\w+\.)*(\w+)$/.exec(initializer)
    const resolved = symbol ? vocabulary.get(symbol[1]!) : undefined
    if (resolved !== undefined) {
      values.push(resolved)
      continue
    }
    // **不许在这里 continue 掉**：解析不出取值就等于这一类节点脱离守护，
    // 必须红。给自己留「跳过」分支就是给自己开恒绿口子。
    unresolved.push(initializer)
  }
  expect(
    unresolved,
    `这些字段的取值解析不出来，词表完备性无从判定（既不是字面量，也不是 ` +
      `${relative(BACKEND_ROOT, CONTRACTS_MES_VOCABULARY)} 里的常量）：${unresolved.join('、')}`,
  ).toEqual([])

  return values
}

function csharpFiles(dir: string): string[] {
  const out: string[] = []
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'bin' || entry.name === 'obj') continue
    const full = join(dir, entry.name)
    if (entry.isDirectory()) out.push(...csharpFiles(full))
    else if (entry.name.endsWith('.cs')) out.push(full)
  }
  return out
}

describe('追溯节点类型词表契约', () => {
  it('后端登记的每一类节点都有中文说法', () => {
    const missing = declaredNodeTypes().filter(
      (type) => !(normalizeCode(type) in TRACE_NODE_TYPE_LABELS),
    )
    expect(
      missing,
      `这些节点类型会在追溯页「类型」列上印英文码，请到 TRACE_NODE_TYPE_LABELS 补中文：${missing.join('、')}`,
    ).toEqual([])
  })

  it('词表里没有后端不会发的死键', () => {
    const known = new Set([
      ...declaredNodeTypes().map(normalizeCode),
      ...TRACE_NODE_TYPE_OPEN_SET_KEYS,
    ])
    const dead = Object.keys(TRACE_NODE_TYPE_LABELS).filter((key) => !known.has(key))
    expect(dead, `这些键后端从不发，留着只会让人以为词表已对齐：${dead.join('、')}`).toEqual([])
  })

  it('自由文本通道只有一个入口、一个调用点', () => {
    // 封闭性靠编译器：构造函数私有、字符串无到本类型的隐式转换，`default` / `new()` / `null`
    // 在 Nullable + TreatWarningsAsErrors 下都是编译错误。绕得过它的办法是在本类型上再开一个
    // 收外部值的入口——静态工厂、公开构造，或者一个 `string → 本类型` 的隐式转换。三种都编译得过，
    // 也都不会改变调用点数，所以先数入口，再数调用点，两头都得是一。
    //
    // 这条断言认的是**声明的写法**，因此拦得住什么要说清楚：下面三条正则覆盖 public/internal、
    // 跨行、任意形参类型，以及到本类型的转换运算符；拦不住的是 private/protected 入口
    // （那出不了这个类型，无害）与非上述形态的写法。它不是全称封闭，是把已知的拆护栏姿势钉住。
    // 真正的结构性正解是把自由文本通道整个拆掉、让节点类型降成 enum——见 issue 登记项。
    const body = nodeTypeBody()
    const entryPoints = [
      ...(body.match(/(?:public|internal)\s+static\s+MesTraceabilityNodeType\s+\w+\s*\(/g) ?? []),
      ...(body.match(/(?:public|internal)\s+MesTraceabilityNodeType\s*\(/g) ?? []),
      ...(body.match(/operator\s+MesTraceabilityNodeType\s*\(/g) ?? []),
    ].map((entry) => entry.replace(/\s+/g, ' '))
    expect(
      entryPoints,
      `MesTraceabilityNodeType 上收外部值的入口应当只有 FromSourceDocumentType 一个，` +
        `实际：${entryPoints.join('、')}。多一个入口，节点类型就又能被任意字符串扩大。`,
    ).toEqual(['public static MesTraceabilityNodeType FromSourceDocumentType('])

    const callSites: string[] = []
    for (const file of csharpFiles(MES_WEB_SRC)) {
      const source = readFileSync(file, 'utf8')
      for (const m of source.matchAll(/MesTraceabilityNodeType\.FromSourceDocumentType\(/g)) {
        // 行号只进失败信息，不进断言——否则本文件之上任何无关改动都会误红。
        callSites.push(
          `${relative(MES_WEB_SRC, file)}:${source.slice(0, m.index).split('\n').length}`,
        )
      }
    }
    expect(
      callSites.map((site) => site.slice(0, site.lastIndexOf(':'))),
      `自由文本节点类型通道的调用点应当只有需求计划来源那一个，实际：${callSites.join('、')}。` +
        '要发新的受控节点类型，请到 MesTraceabilityNodeType 加静态字段。',
    ).toEqual(['Application/Queries/Workbench/MesWorkbenchQueries.cs'])
  })
})
