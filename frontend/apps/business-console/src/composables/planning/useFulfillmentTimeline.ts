import { receiptSourceLimit, useFulfillmentDeliveryQueries } from './useFulfillmentDeliveryQueries'
import { useFulfillmentWorkOrderQuery } from './useFulfillmentWorkOrderQuery'
import { useFulfillmentPlanningQueries } from './useFulfillmentPlanningQueries'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleErpDeliveryOrderItem,
  BusinessConsoleErpReceivableItem,
  BusinessConsoleErpSalesOrderItem,
  BusinessConsoleOrderUrgency,
} from '@nerv-iip/api-client'
import { listBusinessConsoleOrderUrgencies } from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useQuery } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'
import { bindBusinessContext, hasBusinessContext } from '../businessContextBinding'
import {
  describeMergedWorkOrder,
  describeMrpSuggestion,
  matchDemandSource,
  normalizeScope,
  type MesWorkOrderRecord,
  type MrpSuggestionRecord,
} from './fulfillmentMatchers'
import { FulfillmentNodeError, httpStatusOf, runNodeSource } from './fulfillmentErrors'
import {
  describeUrgencyLevel,
  resolveRecordNode,
  type FulfillmentNode,
  type FulfillmentNodeKey,
  unlinkedNode,
} from './fulfillmentNodes'

// ---------------------------------------------------------------------------
// 履约追踪时间线（骨架先行 / MAN-518 · Refs #959）
//
// 只串「已在 generated api-client 中确认存在稳定关联键」的跳数；查不到稳定键的
// 节点一律走「尚未建立关联」态，绝不按相似编号猜测，也不新增后端端点/网关聚合。
//
// 已接入（stable relation keys，逐条经 types.gen.ts 核实）：
//   1. sales-order          销售订单本体          key: salesOrderNo（行自身）
//   2. production-demand    生产需求(DemandSource) key: sourceReference === salesOrderNo（#958）
//   3. mrp-suggestion       MRP 建议              key: MrpPeggingItem.demandSourceReference === salesOrderNo
//                            （peggingType='demand' 行 → suggestionId）
//   4. schedule-urgency     APS/排程紧急度         key: OrderUrgency.businessReference === salesOrderNo（#1053）
//   5. mes-work-order       MES 工单              key: PlanningSuggestionItem.downstreamDocumentId
//                            （suggestionId 来自第 3 跳；合批工单对每张订单都有 pegging 行，
//                              两张订单都能各自走通到同一张工单）
//                            辅证: 详情的工单号与来源建议 ID
//   6. delivery-order       发货单                key: DeliveryOrder.salesOrderNo === salesOrderNo
//   7. receivable           应收                   key: 应收来源精确匹配销售单号或发货单号
//                            （后端 WmsOutboundOrderCompleted→CreateAccountReceivable 以发货单号为源单号）
//
// 尚未建立关联（generated 契约里没有到销售订单的持久关联字段，静态显示规则说明）：
//   production-report / quality-result / finished-goods-receipt /
//   finished-goods-inventory / wms-outbound / voucher
// ---------------------------------------------------------------------------

/**
 * 履约追踪时间线 composable。
 * 每个已接入节点是独立 query、独立失败域；空 scope（无销售订单号）不发任何请求。
 */
export function useFulfillmentTimeline(
  salesOrder: MaybeRefOrGetter<BusinessConsoleErpSalesOrderItem | null | undefined>,
) {
  const auth = useAuthStore()
  const canRead = (permission: string) =>
    auth.principal?.permissionCodes?.includes(permission) === true
  const context = useBusinessContextStore()
  const ctx = bindBusinessContext(
    reactive({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
    }),
  )

  const order = computed(() => toValue(salesOrder) ?? undefined)
  const salesOrderNo = computed(() => normalizeScope(order.value?.salesOrderNo))
  const hasScope = computed(() => hasBusinessContext(ctx) && Boolean(salesOrderNo.value))

  const {
    demandQuery,
    mrpRunsQuery,
    scanRunIds,
    peggingQuery,
    matchedPeggings,
    suggestionIds,
    mrpError,
    mrpLoading,
    suggestionQuery,
    matchedSuggestion,
    matchedWorkOrder,
  } = useFulfillmentPlanningQueries(ctx, salesOrderNo, hasScope, canRead)

  // —— 4. APS/排程紧急度（OrderUrgency.businessReference === salesOrderNo）——
  const urgencyQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'urgency',
      ctx.organizationId,
      ctx.environmentId,
      salesOrderNo.value ?? '',
    ],
    query: () =>
      runNodeSource<BusinessConsoleOrderUrgency[] | null>(async () => {
        const { data, error, response } = await listBusinessConsoleOrderUrgencies({
          query: {
            organizationId: ctx.organizationId,
            environmentId: ctx.environmentId,
            orderReferences: salesOrderNo.value ?? '',
          },
          throwOnError: false,
        })
        return {
          data: data?.success ? (data.data ?? null) : null,
          error: data?.success === false ? data : error,
          response,
        }
      }),
    enabled: hasScope.value,
  }))

  const { workOrderSuggestionId, mesScope, workOrderDetailQuery, matchedPlanRow } =
    useFulfillmentWorkOrderQuery(ctx, matchedWorkOrder, canRead)

  // 建议确认工单号，详情只补当前状态；详情失败仍保留已知工单号。
  const workOrderError = computed(() => mrpError.value ?? suggestionQuery.error.value)
  const workOrderLoading = computed(
    () =>
      mrpLoading.value || suggestionQuery.isLoading.value || workOrderDetailQuery.isLoading.value,
  )

  const { deliveryQuery, matchedDeliveries, deliveryNumbers, checkedSources, receivableQuery } =
    useFulfillmentDeliveryQueries(ctx, salesOrderNo, hasScope, canRead)

  const nodes = computed<FulfillmentNode[]>(() => {
    const so = salesOrderNo.value
    const demand = matchDemandSource(demandQuery.data.value?.items, so)
    const delivery = matchedDeliveries.value[0]
    const receipts = receivableQuery.data.value ?? []
    const receivable = receipts.flatMap((result) => result.rows)[0]
    const moreDeliveries = deliveryQuery.data.value?.limited
      ? [
          {
            label: '继续按销售订单查找发货单',
            to: { path: '/erp/sales/deliveries', query: { keyword: so } },
          },
        ]
      : []
    const financeLinks = [so, ...deliveryNumbers.value]
      .filter(
        (source): source is string =>
          Boolean(source) &&
          !receipts.some(
            (result) => result.source === source && httpStatusOf(result.error) === 403,
          ),
      )
      .map((source) => ({
        label: '按来源核对应收 · ' + source,
        to: { path: '/erp/finance/ar-ap', query: { keyword: source } },
      }))
    const retryableReceipt = receipts.find(
      (result) => result.error && httpStatusOf(result.error) !== 403,
    )
    const receiptFailure = retryableReceipt?.error ?? receipts.find((result) => result.error)?.error
    const receivableError = !canRead(P.erpFinanceRead)
      ? new FulfillmentNodeError(403)
      : (receivableQuery.error.value ??
        (!receivable ? (deliveryQuery.error.value ?? receiptFailure) : null))
    const receiptNote = receipts
      .map(({ source, rows, limited, error }) =>
        [
          source,
          rows
            .map((row) =>
              [
                row.receivableNo,
                row.openAmount != null ? '未结 ' + row.openAmount : '',
                row.currencyCode,
              ]
                .filter(Boolean)
                .join(' '),
            )
            .join('、') ||
            (error ? (httpStatusOf(error) === 403 ? '权限受限' : '读取失败') : '未查到应收'),
          limited ? '列表仅核对前 200 条候选，未命中时按完整源单号复核最近一张' : '',
        ]
          .filter(Boolean)
          .join(' · '),
      )
      .join('；')
    const rangeNote = [
      deliveryQuery.error.value ? '发货来源读取失败，可在发货节点重试。' : '',
      deliveryQuery.data.value?.limited ? '发货候选仅核对前 200 条' : '',
      deliveryNumbers.value.length > receiptSourceLimit
        ? '自动核对前 5 张发货单的应收，可按来源继续查询'
        : '',
    ]
      .filter(Boolean)
      .join('；')
    const suggestion = matchedSuggestion.value
    const pegging = suggestion
      ? matchedPeggings.value.find(
          (row) => row.suggestionId?.trim() === suggestion.suggestionId?.trim(),
        )
      : matchedPeggings.value[0]
    const mrpSuggestionRecord: MrpSuggestionRecord | undefined =
      pegging && !suggestionQuery.isLoading.value ? { pegging, suggestion } : undefined
    const mesWorkOrderRecord: MesWorkOrderRecord | undefined = matchedWorkOrder.value
      ? {
          workOrderNo: matchedWorkOrder.value.workOrderNo,
          ...(matchedPlanRow.value ? { planRow: matchedPlanRow.value } : {}),
        }
      : undefined

    const salesOrderNode: FulfillmentNode = order.value
      ? {
          key: 'sales-order',
          title: '销售订单',
          status: 'established',
          businessNo: order.value.salesOrderNo ?? undefined,
          detailStatus: order.value.status ?? undefined,
          drill: order.value.salesOrderNo
            ? { path: '/erp/sales/orders', query: { keyword: order.value.salesOrderNo } }
            : undefined,
        }
      : {
          key: 'sales-order',
          title: '销售订单',
          status: 'pending',
          ruleNote: '未选择销售订单。',
        }

    return [
      salesOrderNode,
      resolveRecordNode<BusinessConsoleDemandSourceItem>({
        key: 'production-demand',
        title: '生产需求',
        enabled: hasScope.value,
        loading: demandQuery.isLoading.value,
        error: !canRead(P.planningDemandsRead)
          ? new FulfillmentNodeError(403)
          : demandQuery.error.value,
        record: demand,
        present: (record) => ({
          businessNo: record.sourceReference ?? record.demandType ?? undefined,
          detailStatus: record.sourceStatus ?? undefined,
          drill: { path: '/planning', query: { demandSource: so } },
        }),
        pendingNote: demandQuery.data.value?.limited
          ? '前 200 条候选中未找到本单需求。'
          : '销售订单确认后会生成生产需求，当前查询未找到本单需求。',
      }),
      resolveRecordNode<MrpSuggestionRecord>({
        key: 'mrp-suggestion',
        title: 'MRP 建议',
        enabled: hasScope.value,
        loading: mrpLoading.value || suggestionQuery.isLoading.value,
        error: !canRead(P.planningMrpRead)
          ? new FulfillmentNodeError(403)
          : (mrpError.value ?? suggestionQuery.error.value),
        record: mrpSuggestionRecord,
        present: (record) => ({
          businessNo: describeMrpSuggestion(record),
          note: `已核对最近 ${scanRunIds.value.length} 次运行。`,
          detailStatus: record.suggestion?.status ?? undefined,
          updatedAt: record.suggestion?.requiredDate ?? undefined,
          drill: {
            path: '/planning',
            query: { suggestionId: record.pegging.suggestionId?.trim() || undefined },
          },
        }),
        // 空态不能绝对化：只扫了最近若干次运行，更早的运行没看过，说「尚未产生」是越界结论。
        pendingNote:
          scanRunIds.value.length > 0
            ? `最近 ${scanRunIds.value.length} 次 MRP 运行内没有找到与本单的关联（更早的运行未在查询范围内）。`
            : 'MRP 运行后会按生产需求生成建议并关联到本销售订单，当前尚未运行。',
      }),
      resolveRecordNode<MesWorkOrderRecord>({
        key: 'mes-work-order',
        title: 'MES 工单',
        enabled: hasScope.value,
        loading: workOrderLoading.value,
        error: !canRead(P.planningMrpRead) ? new FulfillmentNodeError(403) : workOrderError.value,
        record: mesWorkOrderRecord,
        present: (record) => ({
          businessNo: record.workOrderNo,
          detailStatus: record.planRow?.status ?? undefined,
          note: [
            describeMergedWorkOrder(record, so),
            workOrderDetailQuery.error.value || mesScope.scopeError.value
              ? '工单编号已确认，当前作业范围未能读取状态。'
              : '',
          ]
            .filter(Boolean)
            .join('；'),
          drill: canRead(P.mesWorkOrdersRead)
            ? { path: `/mes/work-orders/${encodeURIComponent(record.workOrderNo)}` }
            : undefined,
        }),
        pendingNote:
          suggestionIds.value.length > 0
            ? '最近核对的运行中未找到已承接的 MES 工单。'
            : '最近核对的运行中未找到本单的工单来源建议。',
      }),
      unlinkedNode('production-report'),
      unlinkedNode('quality-result'),
      unlinkedNode('finished-goods-receipt'),
      unlinkedNode('finished-goods-inventory'),
      resolveRecordNode<BusinessConsoleErpDeliveryOrderItem>({
        key: 'delivery-order',
        title: '发货单',
        links: moreDeliveries,
        enabled: hasScope.value,
        loading: deliveryQuery.isLoading.value,
        error: deliveryQuery.error.value,
        record: delivery,
        present: (record) => ({
          businessNo: record.deliveryOrderNo ?? undefined,
          links: [
            ...moreDeliveries,
            ...deliveryNumbers.value.map((source) => ({
              label: '查看发货单 · ' + source,
              to: { path: '/erp/sales/deliveries', query: { keyword: source } },
            })),
          ],
          note: [
            matchedDeliveries.value
              .map((row) =>
                [
                  row.deliveryOrderNo,
                  ...(row.lines ?? []).map(
                    (line) =>
                      line.skuCode +
                      ' 已发 ' +
                      (line.shippedQuantity ?? '—') +
                      ' / ' +
                      (line.quantity ?? '—') +
                      ' ' +
                      (line.uomCode ?? ''),
                  ),
                ].join(' · '),
              )
              .join('；'),
            rangeNote,
          ]
            .filter(Boolean)
            .join('；'),
          detailStatus: record.status ?? undefined,
          updatedAt: record.shippedAtUtc ?? record.releasedAtUtc ?? undefined,
        }),
        pendingNote: deliveryQuery.data.value?.limited
          ? '前 200 条候选中未找到本单发货单，可在发货列表继续按订单号核对。'
          : '当前查询未找到本单发货单。',
      }),
      {
        ...unlinkedNode('wms-outbound'),
        status: canRead(P.wmsShipmentsRead) ? 'unlinked' : 'restricted',
        ruleNote: 'ERP 发货请求使用发货单号创建出库单，可在作业范围内按单号核对出库状态。',
        links: canRead(P.wmsShipmentsRead)
          ? deliveryNumbers.value.map((source) => ({
              label: '核对出库 · ' + source,
              to: { path: '/wms/outbound', query: { keyword: source } },
            }))
          : [],
      },
      {
        ...resolveRecordNode<BusinessConsoleErpReceivableItem>({
          key: 'receivable',
          title: '应收',
          enabled: hasScope.value,
          loading: deliveryQuery.isLoading.value || receivableQuery.isLoading.value,
          error: receivableError,
          record: receivable,
          present: (record) => ({
            businessNo: record.receivableNo ?? undefined,
            detailStatusLabel:
              record.openAmount != null
                ? `未结 ${record.openAmount} ${record.currencyCode ?? ''}`.trim()
                : undefined,
            note: [receiptNote, rangeNote].filter(Boolean).join('；'),
          }),
          pendingNote: [receiptNote || '尚无可核对的发货来源。', rangeNote]
            .filter(Boolean)
            .join('；'),
        }),
        retryable: Boolean(receivable && retryableReceipt),
        links:
          httpStatusOf(receivableError) !== 403
            ? [
                ...receipts.flatMap((result) =>
                  result.rows.flatMap((row) =>
                    row.receivableNo
                      ? [
                          {
                            label: '核对账单 · ' + row.receivableNo,
                            to: {
                              path: '/erp/finance/ar-ap',
                              query: { keyword: row.receivableNo },
                            },
                          },
                        ]
                      : [],
                  ),
                ),
                ...financeLinks,
              ]
            : [],
      },
      unlinkedNode('voucher'),
    ]
  })

  /**
   * 参考指标（不在因果链上）。
   *
   * 排程紧急度按 businessReference = 销售单号直接计算交期紧迫度，**不依赖 MRP 是否运行**。
   * 它曾被摆进因果时间线中间，出现「MRP 建议尚未产生、下一格却已有 APS 结论」的假因果
   * （#1418 B1，owner 亲验点名），故拆出因果链单独成区，由 UI 明示「独立于上游环节计算」。
   */
  const referenceNodes = computed<FulfillmentNode[]>(() => {
    const so = salesOrderNo.value
    const urgency = urgencyQuery.data.value?.find(
      (item) => item.businessReference === so || item.orderId === so,
    )
    return [
      resolveRecordNode<BusinessConsoleOrderUrgency>({
        key: 'schedule-urgency',
        title: '排程紧急度',
        enabled: hasScope.value,
        loading: urgencyQuery.isLoading.value,
        error: urgencyQuery.error.value,
        record: urgency,
        present: (record) => ({
          businessNo: record.businessReference ?? record.orderId ?? undefined,
          detailStatusLabel: describeUrgencyLevel(record.level),
          updatedAt: record.calculatedAtUtc ?? undefined,
          drill: { path: '/scheduling' },
        }),
        pendingNote:
          '排程侧尚未按本销售单号计算紧急度。该指标按交期直接计算，与上方各环节进度无关。',
      }),
    ]
  })

  const pending = computed(
    () =>
      demandQuery.isLoading.value ||
      mrpLoading.value ||
      urgencyQuery.isLoading.value ||
      suggestionQuery.isLoading.value ||
      workOrderDetailQuery.isLoading.value ||
      deliveryQuery.isLoading.value ||
      receivableQuery.isLoading.value,
  )

  // MRP / 工单两跳各自串了多个请求，重试要把整跳重来，否则前一段的失败会一直卡住。
  function retryMrp() {
    if (!canRead(P.planningMrpRead) || !hasScope.value) return
    void mrpRunsQuery.refetch()
    if (scanRunIds.value.length) void peggingQuery.refetch()
    if (suggestionIds.value.length) void suggestionQuery.refetch()
  }

  function retryWorkOrder() {
    retryMrp()
    if (workOrderSuggestionId.value && canRead(P.mesWorkOrdersRead) && mesScope.scopeError.value)
      void mesScope.refreshScope()
    if (workOrderSuggestionId.value && canRead(P.mesWorkOrdersRead) && mesScope.scopeReady.value)
      void workOrderDetailQuery.refetch()
  }

  function retry(key: FulfillmentNodeKey) {
    switch (key) {
      case 'production-demand':
        if (canRead(P.planningDemandsRead)) void demandQuery.refetch()
        break
      case 'mrp-suggestion':
        retryMrp()
        break
      case 'schedule-urgency':
        void urgencyQuery.refetch()
        break
      case 'mes-work-order':
        retryWorkOrder()
        break
      case 'delivery-order':
        void deliveryQuery.refetch()
        break
      case 'receivable':
        if (deliveryQuery.error.value) void deliveryQuery.refetch()
        if (checkedSources.value.length && canRead(P.erpFinanceRead)) void receivableQuery.refetch()
        break
      default:
        break
    }
  }

  function refreshAll() {
    if (canRead(P.planningDemandsRead)) void demandQuery.refetch()
    retryWorkOrder()
    void urgencyQuery.refetch()
    void deliveryQuery.refetch()
    if (checkedSources.value.length && canRead(P.erpFinanceRead)) void receivableQuery.refetch()
  }

  return { nodes, referenceNodes, pending, hasScope, salesOrderNo, retry, refreshAll }
}
