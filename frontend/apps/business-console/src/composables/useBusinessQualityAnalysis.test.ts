import {
  queryBusinessConsoleQualityProcessCapabilityQueryOptions,
  queryBusinessConsoleQualitySpcControlChartQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import {
  QUALITY_ANALYSIS_FACADE_AUDIT,
  buildQualityAnalysisSummary,
  useQualitySpcAnalysis,
} from './useBusinessQualityAnalysis'

const coladaState = vi.hoisted(() => ({
  queryFactoriesById: new Map<string, () => unknown>(),
  queryDataById: new Map<string, unknown>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  queryBusinessConsoleQualityProcessCapabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'queryBusinessConsoleQualityProcessCapability' }],
    query: vi.fn(),
  })),
  queryBusinessConsoleQualitySpcControlChartQueryOptions: vi.fn(() => ({
    key: [{ _id: 'queryBusinessConsoleQualitySpcControlChart' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryFactoriesById.set(id, optionsFactory)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))

describe('business quality analysis summary', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.clearAllMocks()
    coladaState.queryFactoriesById.clear()
    coladaState.queryDataById.clear()
  })

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
      expect.objectContaining({ capability: 'SPC Xbar-R / 过程能力', businessConsoleFacade: '已接入' }),
      expect.objectContaining({ capability: 'CAPA 列表 / 详情 / 状态追踪', businessConsoleFacade: '缺口' }),
      expect.objectContaining({ capability: 'NCR 处置与关闭', businessConsoleFacade: '已接入' }),
    ])
  })

  it('queries SPC chart and capability only with the required business scope', () => {
    coladaState.queryDataById.set('queryBusinessConsoleQualitySpcControlChart', {
      success: true,
      data: {
        skuCode: 'SKU-A',
        characteristicCode: 'DIAMETER',
        workCenterId: 'WC-01',
        subgroupSize: 5,
        controlLimits: {
          locked: false,
          centerLine: 10.5,
          xbarUpperControlLimit: 11.2,
          xbarLowerControlLimit: 9.8,
        },
        ruleViolations: [{ rule: 'trend-increasing', message: '连续上升趋势' }],
      },
    })
    coladaState.queryDataById.set('queryBusinessConsoleQualityProcessCapability', {
      success: true,
      data: {
        sampleCount: 8,
        cp: 1.23,
        cpk: 1.11,
      },
    })

    const spc = useQualitySpcAnalysis({
      skuCode: 'SKU-A',
      characteristicCode: 'DIAMETER',
      workCenterId: 'WC-01',
      subgroupSize: 5,
      take: 40,
    })

    expect(queryBusinessConsoleQualitySpcControlChartQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: 'SKU-A',
        characteristicCode: 'DIAMETER',
        workCenterId: 'WC-01',
        subgroupSize: 5,
        take: 40,
      },
    })
    expect(queryBusinessConsoleQualityProcessCapabilityQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: 'SKU-A',
        characteristicCode: 'DIAMETER',
        workCenterId: 'WC-01',
        take: 40,
      },
    })
    expect(spc.spcChart.value?.ruleViolations).toHaveLength(1)
    expect(spc.capability.value?.cpk).toBe(1.11)
  })
})
