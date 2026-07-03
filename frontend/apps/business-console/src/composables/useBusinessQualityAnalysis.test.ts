import { describe, expect, it } from 'vitest'

import {
  QUALITY_ANALYSIS_FACADE_AUDIT,
  buildQualityAnalysisSummary,
} from './useBusinessQualityAnalysis'

describe('business quality analysis summary', () => {
  it('builds truthful defect Pareto and dimension summaries from the returned NCR window', () => {
    const summary = buildQualityAnalysisSummary(
      [
        {
          id: 'ncr-1',
          code: 'NCR-001',
          status: 'open',
          skuCode: 'SKU-A',
          sourceType: 'operation',
          defectReason: '尺寸超差',
          defectQuantity: 2,
        },
        {
          id: 'ncr-2',
          code: 'NCR-002',
          status: 'closed',
          skuCode: 'SKU-A',
          sourceType: 'operation',
          defectReason: '尺寸超差',
          defectQuantity: 3,
        },
        {
          id: 'ncr-3',
          code: 'NCR-003',
          status: 'dispositioned',
          skuCode: 'SKU-B',
          sourceType: 'receiving',
          defectReason: '外观划伤',
          defectQuantity: 1,
        },
        {
          id: 'ncr-4',
          code: 'NCR-004',
          status: 'open',
          sourceType: 'receiving',
          defectQuantity: 4,
        },
      ],
      10,
    )

    expect(summary.sampledNcrCount).toBe(4)
    expect(summary.totalNcrCount).toBe(10)
    expect(summary.openNcrCount).toBe(2)
    expect(summary.totalDefectQuantity).toBe(10)
    expect(summary.sampleNotice).toContain('4 / 10')
    expect(summary.sampleNotice).toContain('不是全量趋势')

    expect(summary.defectPareto.slice(0, 2)).toEqual([
      {
        label: '尺寸超差',
        count: 2,
        defectQuantity: 5,
        sharePercent: 50,
      },
      {
        label: '未填',
        count: 1,
        defectQuantity: 4,
        sharePercent: 40,
      },
    ])
    expect(summary.bySku[0]).toMatchObject({ label: 'SKU-A', count: 2, defectQuantity: 5 })
    expect(summary.bySourceType[0]).toMatchObject({ label: 'operation', count: 2, defectQuantity: 5 })
  })

  it('documents the current facade gap instead of exposing static CAPA state', () => {
    expect(QUALITY_ANALYSIS_FACADE_AUDIT).toEqual([
      expect.objectContaining({ capability: '质量趋势 / 缺陷统计聚合', businessConsoleFacade: '缺口' }),
      expect.objectContaining({ capability: 'CAPA 列表 / 详情 / 状态追踪', businessConsoleFacade: '缺口' }),
      expect.objectContaining({ capability: 'NCR 处置与关闭', businessConsoleFacade: '已接入' }),
    ])
  })
})
