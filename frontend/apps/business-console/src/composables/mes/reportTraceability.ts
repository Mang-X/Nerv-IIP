import type { BusinessConsoleMesTraceabilityResponse } from '@nerv-iip/api-client'

export interface ReportTraceSource {
  reportNo: string
  nodeType:
    | 'ProductionReport'
    | 'ProducedLot'
    | 'Serial'
    | 'Material'
    | 'MaterialLot'
    | 'MaterialIssueRequest'
  nodeId: string
}

// 只消费图中报工与产出/耗料的真实关系；不同对象编号即使同值也分别保留。
export function reportTraceSources(
  graph: BusinessConsoleMesTraceabilityResponse | undefined,
  node: { nodeId?: string; nodeType?: string },
): ReportTraceSource[] {
  if (!node.nodeId) return []
  if (node.nodeType === 'ProductionReport')
    return [{ reportNo: node.nodeId, nodeType: 'ProductionReport', nodeId: node.nodeId }]
  if (
    node.nodeType === 'Material' ||
    node.nodeType === 'MaterialLot' ||
    node.nodeType === 'MaterialIssueRequest'
  ) {
    const nodeType = node.nodeType
    const lotIds =
      nodeType === 'MaterialLot'
        ? [node.nodeId]
        : (graph?.edges ?? [])
            .filter(
              (edge) =>
                edge.fromNodeId === node.nodeId &&
                edge.relationType === (nodeType === 'Material' ? 'has-lot' : 'received-lot'),
            )
            .map((edge) => edge.toNodeId)
    const sources: ReportTraceSource[] = []
    for (const edge of graph?.edges ?? []) {
      if (edge.relationType !== 'consumed-by-report' || !lotIds.includes(edge.fromNodeId)) continue
      const report = graph?.nodes?.find(
        (candidate) =>
          candidate.nodeType === 'ProductionReport' && candidate.nodeId === edge.toNodeId,
      )
      if (!report?.nodeId) continue
      if (!sources.some((source) => source.reportNo === report.nodeId))
        sources.push({ reportNo: report.nodeId, nodeType, nodeId: node.nodeId })
    }
    return sources
  }
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
