import { describe, expect, it } from 'vitest'
import { reportTraceSources, traceabilityReturn } from './reportTraceability'

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
    expect(traceabilityReturn(target)).toBe(target)
    expect(traceabilityReturn('https://other.example/mes/traceability')).toBeUndefined()
    expect(traceabilityReturn('/mes/work-orders')).toBeUndefined()
  })
})
