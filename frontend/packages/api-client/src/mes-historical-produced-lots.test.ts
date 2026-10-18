import { describe, expect, it } from 'vitest'
import {
  listBusinessConsoleMesHistoricalProducedLots,
  listBusinessConsoleMesHistoricalProducedLotsQueryOptions,
  listBusinessConsoleMesReceivableProducedLotsQueryOptions,
  type BusinessConsoleMesHistoricalProducedLotListResponse,
} from '@nerv-iip/api-client'

// PublicContract: #4499 / #4461 spec r1; generated query options through the stable barrel.
describe('MES historical produced lots', () => {
  it('reads history with its own path and preserves output identity without remaining quantity', async () => {
    const history: BusinessConsoleMesHistoricalProducedLotListResponse = {
      items: [
        {
          producedLotNo: 'LOT-HISTORY',
          reportNo: 'REPORT-HISTORY',
          operationTaskId: 'OP-10',
          quantity: 10,
          createdAtUtc: '2026-10-18T00:00:00Z',
          serialNo: null,
          serialNumbers: ['SN-1', 'SN-2'],
        },
      ],
    }
    const paths: string[] = []
    const fetch: typeof globalThis.fetch = async (input) => {
      const request = input as Request
      paths.push(new URL(request.url).pathname)
      expect(new URL(request.url).searchParams.get('organizationId')).toBe('org-history')
      expect(new URL(request.url).searchParams.get('environmentId')).toBe('env-history')
      return Response.json({
        success: true,
        data: { items: request.url.includes('/traceability/') ? history.items : [] },
      })
    }
    const options = {
      baseUrl: 'http://gateway.local',
      fetch,
      path: { workOrderId: 'WO-HISTORY' },
      query: { organizationId: 'org-history', environmentId: 'env-history' },
    }
    const historical = listBusinessConsoleMesHistoricalProducedLotsQueryOptions(options)
    const receivable = listBusinessConsoleMesReceivableProducedLotsQueryOptions(options)
    expect(historical.key).not.toEqual(receivable.key)
    expect(historical.key[0]).toMatchObject({
      path: { workOrderId: 'WO-HISTORY' },
      query: { organizationId: 'org-history', environmentId: 'env-history' },
    })
    const response = await listBusinessConsoleMesHistoricalProducedLots({
      ...options,
      throwOnError: true,
    })
    expect(response.data.data).toEqual(history)
    expect(paths).toEqual([
      '/api/business-console/v1/mes/traceability/work-orders/WO-HISTORY/produced-lots',
    ])
  })
})
