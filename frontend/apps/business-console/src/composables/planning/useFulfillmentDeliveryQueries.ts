import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type {
  BusinessConsoleErpDeliveryOrderItem,
  BusinessConsoleErpReceivableItem,
} from '@nerv-iip/api-client'
import {
  getBusinessConsoleErpReceivableBySourceDocument,
  listBusinessConsoleErpReceivables,
  listBusinessConsoleErpDeliveryOrders,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, type Ref } from 'vue'
import type { BusinessContextFields } from '../shared/businessContextBinding'
import { matchDeliveryOrders } from './fulfillmentMatchers'
import { readCandidatePages } from './fulfillmentCandidatePages'
import { runNodeSource } from './fulfillmentErrors'

export const receiptSourceLimit = 5

export function useFulfillmentDeliveryQueries(
  ctx: BusinessContextFields,
  salesOrderNo: Readonly<Ref<string | undefined>>,
  hasScope: Readonly<Ref<boolean>>,
  canRead: (permission: string) => boolean,
) {
  // —— 6. 发货单（DeliveryOrder.salesOrderNo === salesOrderNo）——
  const deliveryQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'delivery',
      ctx.organizationId,
      ctx.environmentId,
      salesOrderNo.value ?? '',
    ],
    query: () => {
      const scope = {
        organizationId: ctx.organizationId,
        environmentId: ctx.environmentId,
        keyword: salesOrderNo.value,
      }
      return readCandidatePages<BusinessConsoleErpDeliveryOrderItem>((skip, take) =>
        listBusinessConsoleErpDeliveryOrders({
          query: { ...scope, skip, take },
          throwOnError: false,
        }),
      )
    },
    enabled: hasScope.value,
  }))

  const matchedDeliveries = computed(() =>
    matchDeliveryOrders(deliveryQuery.data.value?.items, salesOrderNo.value),
  )
  // 应收源单号 = 本销售订单已确认发货单的单号（后端以发货单号作应收源单号）。
  const deliveryNumbers = computed(() => [
    ...new Set(
      matchedDeliveries.value.flatMap((row) => (row.deliveryOrderNo ? [row.deliveryOrderNo] : [])),
    ),
  ])
  const checkedSources = computed(() =>
    salesOrderNo.value
      ? [salesOrderNo.value, ...deliveryNumbers.value.slice(0, receiptSourceLimit)]
      : [],
  )
  const receivableQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'receivables',
      ctx.organizationId,
      ctx.environmentId,
      ...checkedSources.value,
    ],
    enabled: canRead(P.erpFinanceRead) && checkedSources.value.length > 0,
    query: async () => {
      const scope = { organizationId: ctx.organizationId, environmentId: ctx.environmentId }
      const sources = [...checkedSources.value]
      const results: {
        source: string
        rows: BusinessConsoleErpReceivableItem[]
        limited: boolean
        error?: unknown
      }[] = []
      for (const source of sources) {
        const receipt = {
          source,
          rows: [] as BusinessConsoleErpReceivableItem[],
          limited: false,
          error: undefined as unknown,
        }
        try {
          const candidates = await readCandidatePages<BusinessConsoleErpReceivableItem>(
            (skip, take) =>
              listBusinessConsoleErpReceivables({
                query: { ...scope, keyword: source, skip, take },
                throwOnError: false,
              }),
          )
          receipt.rows = candidates.items.filter((row) => row.sourceDocumentNo === source)
          receipt.limited = candidates.limited
          if (receipt.limited && receipt.rows.length === 0) {
            const latest = await runNodeSource(async () => {
              const { data, error, response } =
                await getBusinessConsoleErpReceivableBySourceDocument({
                  query: { ...scope, sourceDocumentNo: source },
                  throwOnError: false,
                })
              // ERP 的按来源接口用业务 400 表示无记录，同一状态也用于参数校验。
              if (
                response?.status === 400 &&
                error &&
                typeof error === 'object' &&
                'success' in error &&
                error.success === false &&
                'message' in error &&
                error.message === `来源单据『${source}』的应收单不存在。`
              )
                return { data: undefined }
              return { data: data?.data, error: data?.success === false ? data : error, response }
            })
            if (latest?.sourceDocumentNo === source) receipt.rows.push(latest)
          }
        } catch (error) {
          receipt.error = error
        }
        results.push(receipt)
      }
      return results
    },
  }))
  return { deliveryQuery, matchedDeliveries, deliveryNumbers, checkedSources, receivableQuery }
}
