import type { BusinessConsoleOrderUrgency } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'
import {
  chunkReferencesByParamBudget,
  indexOrderUrgenciesByReference,
  URGENCY_REFERENCE_PARAM_BUDGET,
} from './useOrderUrgency'

function urgency(orderId: string, businessReference: string, level: string) {
  return { orderId, businessReference, level } as BusinessConsoleOrderUrgency
}

describe('indexOrderUrgenciesByReference', () => {
  it('keeps the most urgent order for a shared upstream business reference', () => {
    const normal = urgency('WO-002', 'SO-001', 'normal')
    const critical = urgency('WO-001', 'SO-001', 'critical')

    const indexed = indexOrderUrgenciesByReference([normal, critical])

    expect(indexed.get('SO-001')).toBe(critical)
    expect(indexed.get('WO-001')).toBe(critical)
    expect(indexed.get('WO-002')).toBe(normal)
  })
})

// #1418 B4 根因回证：网关对 orderReferences 有 4000 字符上限，需求池 1000+ 单号
// 整串 join 必超限 → 整请求 400 → 全列「未计算」。分片必须保证每片 join 后不超预算。
describe('chunkReferencesByParamBudget', () => {
  it('单片放得下时不拆分', () => {
    expect(chunkReferencesByParamBudget(['SO-1', 'SO-2'], 20)).toEqual([['SO-1', 'SO-2']])
  })

  it('每片 join 后都不超过预算', () => {
    const references = Array.from(
      { length: 1300 },
      (_, i) => `SO-2026-${String(i).padStart(5, '0')}`,
    )
    const chunks = chunkReferencesByParamBudget(references)

    expect(chunks.length).toBeGreaterThan(1)
    for (const chunk of chunks) {
      expect(chunk.join(',').length).toBeLessThanOrEqual(URGENCY_REFERENCE_PARAM_BUDGET)
    }
    // 分片不丢、不重、保序。
    expect(chunks.flat()).toEqual(references)
  })

  it('单个超长引用独占一片而不是死循环', () => {
    const long = 'X'.repeat(50)
    expect(chunkReferencesByParamBudget(['SO-1', long, 'SO-2'], 10)).toEqual([
      ['SO-1'],
      [long],
      ['SO-2'],
    ])
  })

  it('空输入返回空数组', () => {
    expect(chunkReferencesByParamBudget([])).toEqual([])
  })
})
