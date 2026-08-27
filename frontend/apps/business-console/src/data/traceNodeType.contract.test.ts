import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
  normalizeCode,
  TRACE_NODE_TYPE_LABELS,
  TRACE_NODE_TYPE_OPEN_SET_KEYS,
} from './businessLabels'

/**
 * 追溯图「类型」列的词表与后端节点类型集合之间的完备性契约。
 *
 * 为什么读后端源码：追溯节点类型不进 OpenAPI（`nodeType` 是裸 string），前端拿不到任何
 * 机读的取值集合。此前两边各写各的词表，只在四个键上碰巧对得上，其余节点在界面上印
 * `ProductionReport` 这样的英文码，而两边都绿。能让「后端新增一类节点」当场变红的
 * 权威来源，只有 MES 读面里那份常量表本身。
 *
 * 契约两条：
 * 1. 常量表里的每个节点类型都有中文说法（后端加一类而词表没跟进即红）；
 * 2. 没有发节点的调用点绕开常量表另写字面量（否则常量表就不再是全集，第 1 条随之失效）。
 */

const MES_TRACEABILITY_QUERIES = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../../../backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs',
)

const source = readFileSync(MES_TRACEABILITY_QUERIES, 'utf8')

/** `MesTraceabilityNodeTypes` 常量表里登记的节点类型取值。 */
function declaredNodeTypes(): string[] {
  const start = source.indexOf('public static class MesTraceabilityNodeTypes')
  expect(start, 'MesTraceabilityNodeTypes 常量表已被改名或移走').toBeGreaterThanOrEqual(0)
  const open = source.indexOf('{', start)
  const close = source.indexOf('\n}', open)
  const body = source.slice(open, close)
  return [...body.matchAll(/public const string \w+ = "([^"]+)";/g)].map((m) => m[1]!)
}

/** 发节点的调用点：`new MesTraceabilityNode(nodeId, nodeType, …)` 与本地 `AddNode(…)` 助手。 */
const EMISSION_RE = /(?:new MesTraceabilityNode|AddNode)\(\s*[^,;()]+?\s*,\s*([^,;()]+?)\s*,/g

/**
 * 允许不引用常量表的 nodeType 表达式，逐条都要说得出理由：
 * - `string nodeType` / `nodeType`：`AddNode` 助手的形参与它对构造函数的转发，值来自调用点；
 * - `SourceDocumentType`：需求计划来源节点的类型取自工单上持久化的自由文本，是开放集合。
 */
const ALLOWED_PASS_THROUGH = new Set([
  'string nodeType',
  'nodeType',
  'detail.SourcePlanReference.SourceDocumentType',
])

describe('追溯节点类型词表契约', () => {
  it('后端登记的每一类节点都有中文说法', () => {
    const nodeTypes = declaredNodeTypes()
    expect(nodeTypes.length).toBeGreaterThanOrEqual(10)

    const missing = nodeTypes.filter((type) => !(normalizeCode(type) in TRACE_NODE_TYPE_LABELS))
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

  it('没有调用点绕开常量表另写节点类型', () => {
    const offenders: string[] = []
    let matched = 0
    EMISSION_RE.lastIndex = 0
    let m: RegExpExecArray | null
    while ((m = EMISSION_RE.exec(source))) {
      matched += 1
      const expression = m[1]!.replace(/\s+/g, ' ').trim()
      if (ALLOWED_PASS_THROUGH.has(expression)) continue
      if (/MesTraceabilityNodeTypes\.\w+/.test(expression)) continue
      const line = source.slice(0, m.index).split('\n').length
      offenders.push(`${line}: ${expression}`)
    }

    // 正则一旦被源码改动带偏就会一条都匹配不上，那时上面的循环恒绿——这里先证明它还在看东西。
    expect(matched, '发节点的调用点一个都没扫到，说明扫描已失效').toBeGreaterThanOrEqual(20)
    expect(
      offenders,
      `这些调用点没走 MesTraceabilityNodeTypes，常量表因此不再是节点类型全集：${offenders.join('；')}`,
    ).toEqual([])
  })
})
