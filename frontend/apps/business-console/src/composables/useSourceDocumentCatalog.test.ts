import {
  listBusinessConsoleErpQuotationsQueryOptions,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
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
const wmsScope = vi.hoisted(() => ({ kind: undefined as string | undefined }))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const options = () => vi.fn(() => ({ key: [], query: vi.fn() }))
  return {
    ...(await importOriginal<object>()),
    listBusinessConsoleErpQuotationsQueryOptions: options(),
    listBusinessConsoleMaintenanceWorkOrdersQueryOptions: options(),
    listBusinessConsoleMesProductionReportsQueryOptions: options(),
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

vi.mock('./useWmsWorkScope', () => ({
  useWmsWorkScope: () => ({
    hasSelection: {
      get value() {
        return !!wmsScope.kind
      },
    },
    scopeKind: {
      get value() {
        return wmsScope.kind
      },
    },
    scopeId: {
      get value() {
        return wmsScope.kind ? 'SITE-01' : undefined
      },
    },
  }),
}))

// PublicContract: #3775 来源单据按类型取各域列表，提交值与下游比对的值一致。
describe('source document catalog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    colada.data = undefined
    wmsScope.kind = undefined
    vi.useFakeTimers()
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('searches the document list by number inside the business scope', async () => {
    const catalog = useSourceDocumentCatalog('mes-production-report', '')

    catalog.search.value = ' RPT-07 '
    await vi.advanceTimersByTimeAsync(300)
    colada.factory!()

    expect(listBusinessConsoleMesProductionReportsQueryOptions).toHaveBeenLastCalledWith({
      query: { organizationId: 'org-001', environmentId: 'env-dev', take: 50, keyword: 'RPT-07' },
    })
  })

  it('submits the maintenance work-order id while showing its readable number', () => {
    colada.data = {
      success: true,
      data: {
        items: [{ workOrderId: '0199a1b2-0000-7000-8000-00000000abcd', deviceAssetId: 'CNC-01' }],
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
      { value: '0199a1b2-0000-7000-8000-00000000abcd', label: 'WO-0000ABCD', hint: 'CNC-01' },
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

  it('waits for the WMS work scope before listing inbound orders', () => {
    useSourceDocumentCatalog('wms-inbound-order', '')
    expect(colada.factory!().enabled).toBe(false)

    wmsScope.kind = 'site'
    expect(colada.factory!().enabled).toBe(true)
    expect(listBusinessConsoleWmsInboundOrdersQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        take: 50,
        scopeKind: 'site',
        scopeId: 'SITE-01',
      },
    })
  })
})
