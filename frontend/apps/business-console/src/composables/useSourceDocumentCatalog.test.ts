import {
  listBusinessConsoleErpQuotationsQueryOptions,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
} from '@nerv-iip/api-client'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useSourceDocumentCatalog } from './useSourceDocumentCatalog'

const colada = vi.hoisted(() => ({
  factory: undefined as undefined | (() => { enabled?: boolean }),
  data: undefined as unknown,
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const options = () => vi.fn(() => ({ key: [], query: vi.fn() }))
  return {
    ...(await importOriginal<object>()),
    listBusinessConsoleErpQuotationsQueryOptions: options(),
    listBusinessConsoleMaintenanceWorkOrdersQueryOptions: options(),
    listBusinessConsoleMesWorkOrdersQueryOptions: options(),
    listBusinessConsoleWmsInboundOrdersQueryOptions: options(),
  }
})

vi.mock('@pinia/colada', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  useQuery: vi.fn((optionsFactory: () => { enabled?: boolean }) => {
    colada.factory = optionsFactory
    optionsFactory()
    return { data: shallowRef(colada.data), isLoading: shallowRef(false) }
  }),
}))

// PublicContract: #3775 来源单据按类型取各域列表，提交值与下游比对的值一致。
describe('source document catalog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    colada.data = undefined
    vi.useFakeTimers()
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('searches the document list by number inside the business scope', async () => {
    const catalog = useSourceDocumentCatalog('mes-work-order', '')

    catalog.search.value = ' WO-07 '
    await vi.advanceTimersByTimeAsync(300)
    colada.factory!()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenLastCalledWith({
      query: { organizationId: 'org-001', environmentId: 'env-dev', take: 50, keyword: 'WO-07' },
    })
  })

  it('submits and shows the maintenance work order formal number (#3852)', () => {
    colada.data = {
      success: true,
      data: {
        items: [
          {
            workOrderId: '0199a1b2-0000-7000-8000-00000000abcd',
            workOrderNo: 'MWO-20260928-000001',
            deviceAssetId: 'CNC-01',
            status: 'in_progress',
          },
        ],
        total: 1,
      },
    }

    const catalog = useSourceDocumentCatalog('maintenance-work-order', '')
    catalog.search.value = 'abcd'

    // 维修工单列表不支持关键字：取一批在选择器里本地过滤，不把搜索词发给服务端。
    expect(listBusinessConsoleMaintenanceWorkOrdersQueryOptions).toHaveBeenLastCalledWith({
      query: { organizationId: 'org-001', environmentId: 'env-dev', take: 200 },
    })
    expect(catalog.options.value).toEqual([
      {
        value: 'MWO-20260928-000001',
        label: 'MWO-20260928-000001',
        hint: 'CNC-01 · 执行中',
      },
    ])
  })

  it('records the inspected document and shows maintenance work orders by readable number', () => {
    colada.data = {
      success: true,
      data: {
        items: [
          {
            id: 'rec-1',
            sourceType: 'maintenance',
            sourceDocumentId: 'MWO-20260928-000001',
            skuCode: 'SP-01',
          },
          {
            id: 'rec-0',
            sourceType: 'maintenance',
            sourceDocumentId: '0199a1b2-0000-7000-8000-00000000abcd',
            skuCode: 'SP-00',
          },
          { id: 'rec-2', sourceType: 'final', sourceDocumentId: 'WO-0007', skuCode: 'FG-01' },
          { id: 'rec-3', sourceType: 'final', sourceDocumentId: 'WO-0007', skuCode: 'FG-01' },
        ],
        total: 3,
      },
    }

    const catalog = useSourceDocumentCatalog('quality-inspection', '')

    // 同一张被检单据的多条检验记录只列一次；维修工单显示正式单号，早期只存了工单 ID 的记录 GUID 不上屏。
    expect(catalog.options.value.map(({ value, label }) => ({ value, label }))).toEqual([
      { value: 'MWO-20260928-000001', label: 'MWO-20260928-000001' },
      { value: '0199a1b2-0000-7000-8000-00000000abcd', label: '维修工单' },
      { value: 'WO-0007', label: 'WO-0007' },
    ])
  })

  it('offers only approved quotations that have not been converted yet', () => {
    colada.data = {
      success: true,
      data: {
        items: [
          { quotationNo: 'QUO-001', customerCode: 'CUST-01', status: 'Approved' },
          {
            quotationNo: 'QUO-002',
            customerCode: 'CUST-02',
            status: 'Approved',
            convertedSalesOrderNo: 'SO-009',
          },
        ],
        total: 2,
      },
    }

    const catalog = useSourceDocumentCatalog('erp-approved-quotation', 'QUO-100')

    expect(listBusinessConsoleErpQuotationsQueryOptions).toHaveBeenLastCalledWith({
      query: { organizationId: 'org-001', environmentId: 'env-dev', take: 50, status: 'Approved' },
    })
    // 已选但不在本页结果里的单号保留成占位项，不显示成「未选择」。
    expect(catalog.options.value.map((option) => option.value)).toEqual(['QUO-100', 'QUO-001'])
  })

  // PublicContract: #4255 / #3825 r1，授权工厂来源搜索不依赖、不改变入库页记忆。
  it('searches all authorized sites and selects a real inbound number while preserving the remembered site', async () => {
    const memoryKey =
      'nerv-iip.business-console.wms-work-scope.v1:user-001|org-001|env-dev|receipts'
    localStorage.setItem(memoryKey, 'site:SITE-A')
    colada.data = {
      success: true,
      data: { items: [{ inboundOrderNo: 'IN-20261009-0002', siteCode: 'SITE-B' }], total: 1 },
    }
    const selected = shallowRef('')
    const catalog = useSourceDocumentCatalog('wms-inbound-order', selected)
    catalog.search.value = ' IN-20261009 '
    await vi.advanceTimersByTimeAsync(300)
    expect(colada.factory!().enabled).toBe(true)
    expect(listBusinessConsoleWmsInboundOrdersQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        take: 50,
        keyword: 'IN-20261009',
        scopeKind: 'authorized-sites',
        scopeId: 'all',
      },
    })
    expect(catalog.options.value).toEqual([
      { value: 'IN-20261009-0002', label: 'IN-20261009-0002', hint: 'SITE-B' },
    ])
    selected.value = catalog.options.value[0]!.value
    expect(selected.value).toBe('IN-20261009-0002')
    expect(localStorage.getItem(memoryKey)).toBe('site:SITE-A')
    localStorage.removeItem(memoryKey)
  })
})
