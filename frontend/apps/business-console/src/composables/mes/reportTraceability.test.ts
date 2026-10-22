import { describe, expect, it } from 'vitest'
import { firstTraceQuery, reportTraceSources } from './reportTraceability'
import { traceabilityReturnPath } from './traceWorkOrderLink'

const graph = {
  nodes: [
    { nodeId: 'PRPT-1', nodeType: 'ProductionReport' },
    { nodeId: 'PRPT-2', nodeType: 'ProductionReport' },
  ],
  edges: [
    { fromNodeId: 'PRPT-1', toNodeId: 'LOT-1', relationType: 'produced-lot' },
    { fromNodeId: 'PRPT-2', toNodeId: 'LOT-1', relationType: 'produced-lot' },
    { fromNodeId: 'PRPT-2', toNodeId: 'LOT-1', relationType: 'produced-serial' },
    { fromNodeId: 'PRPT-1', toNodeId: 'MAT-1', relationType: 'consumed-by-report' },
  ],
}
describe('来源报工身份', () => {
  it('保留多个真实来源并区分同值批次和序列', () => {
    expect(reportTraceSources(graph, { nodeId: 'LOT-1', nodeType: 'ProducedLotOrSerial' })).toEqual(
      [
        { reportNo: 'PRPT-1', nodeType: 'ProducedLot', nodeId: 'LOT-1' },
        { reportNo: 'PRPT-2', nodeType: 'ProducedLot', nodeId: 'LOT-1' },
        { reportNo: 'PRPT-2', nodeType: 'Serial', nodeId: 'LOT-1' },
      ],
    )
    expect(reportTraceSources(graph, { nodeId: 'LOT-1', nodeType: 'Serial' })).toEqual([
      { reportNo: 'PRPT-2', nodeType: 'Serial', nodeId: 'LOT-1' },
    ])
  })
  it('没有真实产出关系时不猜报工，Unknown 根不提供目标', () => {
    expect(reportTraceSources(graph, { nodeId: 'MAT-1', nodeType: 'ProducedLot' })).toEqual([])
    expect(reportTraceSources(graph, { nodeId: 'LOT-1', nodeType: 'BatchOrSerial' })).toEqual([])
  })
  it('分页外报工直接使用 ReportNo 而非生产记录主键', () => {
    expect(reportTraceSources(graph, { nodeId: 'PRPT-900', nodeType: 'ProductionReport' })).toEqual(
      [{ reportNo: 'PRPT-900', nodeType: 'ProductionReport', nodeId: 'PRPT-900' }],
    )
  })
  it('返回保留追溯查询且不能跳到外站或别的业务页', () => {
    const target = '/mes/traceability?mode=batch&batchOrSerial=LOT-1&workOrderId=WO-1'
    expect(traceabilityReturnPath(firstTraceQuery(target))).toBe(target)
    expect(
      traceabilityReturnPath(firstTraceQuery('https://other.example/mes/traceability')),
    ).toBeUndefined()
    expect(traceabilityReturnPath(firstTraceQuery('/mes/work-orders'))).toBeUndefined()
  })
})

const materialGraph = {
  nodes: [
    { nodeId: 'MAT-1', nodeType: 'Material' },
    { nodeId: 'MLOT-1', nodeType: 'MaterialLot' },
    { nodeId: 'MLOT-2', nodeType: 'MaterialLot' },
    { nodeId: 'MIR-1', nodeType: 'MaterialIssueRequest' },
    { nodeId: 'PRPT-1', nodeType: 'ProductionReport' },
    { nodeId: 'PRPT-2', nodeType: 'ProductionReport' },
  ],
  edges: [
    { fromNodeId: 'MAT-1', toNodeId: 'MLOT-1', relationType: 'has-lot' },
    { fromNodeId: 'MAT-1', toNodeId: 'MLOT-2', relationType: 'has-lot' },
    { fromNodeId: 'MIR-1', toNodeId: 'MLOT-1', relationType: 'received-lot' },
    { fromNodeId: 'MLOT-1', toNodeId: 'PRPT-1', relationType: 'consumed-by-report' },
    { fromNodeId: 'MLOT-1', toNodeId: 'PRPT-2', relationType: 'consumed-by-report' },
    { fromNodeId: 'MLOT-2', toNodeId: 'PRPT-2', relationType: 'consumed-by-report' },
  ],
}
describe('耗料来源身份', () => {
  it.each([
    ['Material', 'MAT-1'],
    ['MaterialLot', 'MLOT-1'],
    ['MaterialIssueRequest', 'MIR-1'],
  ])('%s 沿真实物料批耗料边保留多个报工来源', (nodeType, nodeId) => {
    expect(reportTraceSources(materialGraph, { nodeType, nodeId })).toEqual([
      { reportNo: 'PRPT-1', nodeType, nodeId },
      { reportNo: 'PRPT-2', nodeType, nodeId },
    ])
  })
  it('关系缺失或反向时不推导耗料来源，也不把同值产出作为耗料', () => {
    expect(reportTraceSources(materialGraph, { nodeType: 'Material', nodeId: 'MIR-1' })).toEqual([])
    expect(
      reportTraceSources(materialGraph, { nodeType: 'MaterialLot', nodeId: 'MLOT-404' }),
    ).toEqual([])
    expect(
      reportTraceSources(
        {
          nodes: materialGraph.nodes,
          edges: [
            { fromNodeId: 'PRPT-1', toNodeId: 'MLOT-1', relationType: 'consumed-by-report' },
            { fromNodeId: 'PRPT-2', toNodeId: 'MLOT-1', relationType: 'produced-lot' },
          ],
        },
        { nodeType: 'MaterialLot', nodeId: 'MLOT-1' },
      ),
    ).toEqual([])
  })
})
