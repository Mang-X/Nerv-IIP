import type {
  BusinessConsoleMesTraceabilityNode,
  BusinessConsoleMesTraceabilityResponse,
} from '@nerv-iip/api-client'

/** 工序只能沿公开图中的所属关系定位，不从输入工单或显示编号推断。 */
export function traceWorkOrderTarget(
  node: BusinessConsoleMesTraceabilityNode,
  graph: BusinessConsoleMesTraceabilityResponse | undefined,
): { workOrderId?: string; operationTaskId?: string; reason?: string } {
  if (node.nodeType !== 'WorkOrder' && node.nodeType !== 'OperationTask') return {}
  if (!node.nodeId || node.status === 'Unknown') return { reason: '未找到可查看的工单事实' }
  if (node.nodeType === 'WorkOrder') return { workOrderId: node.nodeId }

  const workOrderIds = new Set(
    (graph?.edges ?? []).flatMap((edge) => {
      const id =
        edge.relationType === 'has-operation' && edge.toNodeId === node.nodeId
          ? edge.fromNodeId
          : edge.relationType === 'belongs-to-work-order' && edge.fromNodeId === node.nodeId
            ? edge.toNodeId
            : undefined
      return id &&
        graph?.nodes?.some(
          (candidate) =>
            candidate.nodeType === 'WorkOrder' &&
            candidate.nodeId === id &&
            candidate.status !== 'Unknown',
        )
        ? [id]
        : []
    }),
  )
  if (workOrderIds.size === 0) return { reason: '未找到工序所属工单关系' }
  if (workOrderIds.size !== 1) return { reason: '工序关联多个工单，无法确定所属工单' }
  return { workOrderId: [...workOrderIds][0], operationTaskId: node.nodeId }
}

/** 返回入口仅消费本功能内的追溯地址，不接受任意路由或外部 URL。 */
export function traceabilityReturnPath(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  return value === '/mes/traceability' || value.startsWith('/mes/traceability?') ? value : undefined
}
