import type { BusinessConsoleMesTraceabilityResponse } from '@nerv-iip/api-client'

export interface ReportTraceSource {
  reportNo: string
  nodeType: 'ProductionReport' | 'ProducedLot' | 'Serial'
  nodeId: string
}

// 只消费图中报工与产出的真实关系；批号和序列号即使同值也分别保留。
export function reportTraceSources(
  graph: BusinessConsoleMesTraceabilityResponse | undefined,
  node: { nodeId?: string; nodeType?: string },
): ReportTraceSource[] {
  if (!node.nodeId) return []
  if (node.nodeType === 'ProductionReport')
    return [{ reportNo: node.nodeId, nodeType: 'ProductionReport', nodeId: node.nodeId }]
  if (!['ProducedLot', 'Serial', 'ProducedLotOrSerial'].includes(node.nodeType ?? '')) return []
  const sources: ReportTraceSource[] = []
  for (const edge of graph?.edges ?? []) {
    if (edge.toNodeId !== node.nodeId) continue
    const type =
      edge.relationType === 'produced-lot'
        ? 'ProducedLot'
        : edge.relationType === 'produced-serial'
          ? 'Serial'
          : undefined
    if (!type || (node.nodeType !== 'ProducedLotOrSerial' && node.nodeType !== type)) continue
    const report = graph?.nodes?.find(
      (candidate) =>
        candidate.nodeType === 'ProductionReport' && candidate.nodeId === edge.fromNodeId,
    )
    if (!report?.nodeId) continue
    if (!sources.some((source) => source.reportNo === report.nodeId && source.nodeType === type))
      sources.push({ reportNo: report.nodeId, nodeType: type, nodeId: node.nodeId })
  }
  return sources
}

export function firstTraceQuery(value: unknown): string {
  return typeof value === 'string'
    ? value
    : Array.isArray(value) && typeof value[0] === 'string'
      ? value[0]
      : ''
}

// 返回仅限本功能的追溯页，不接收外站或其他业务目标。
export function traceabilityReturn(value: unknown): string | undefined {
  const target = firstTraceQuery(value)
  if (target === '/mes/traceability' || target.startsWith('/mes/traceability?')) return target
  return undefined
}
