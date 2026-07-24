import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleErpDeliveryOrderItem,
  BusinessConsoleErpReceivableSourceDocumentResponse,
  BusinessConsoleErpSalesOrderItem,
  BusinessConsoleOrderUrgency,
} from '@nerv-iip/api-client'
import {
  getBusinessConsoleErpReceivableBySourceDocument,
  listBusinessConsoleErpDeliveryOrders,
  listBusinessConsoleOrderUrgencies,
  listBusinessConsolePlanningDemands,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useQuery } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import { bindBusinessContext, hasBusinessContext } from './businessContextBinding'

// ---------------------------------------------------------------------------
// 履约追踪时间线（骨架先行 / MAN-518 · Refs #959）
//
// 只串「已在 generated api-client 中确认存在稳定关联键」的跳数；查不到稳定键的
// 节点一律走「尚未建立关联」态，绝不按相似编号猜测，也不新增后端端点/网关聚合。
//
// 已接入（stable relation keys，逐条经 types.gen.ts 核实）：
//   1. sales-order          销售订单本体          key: salesOrderNo（行自身）
//   2. production-demand    生产需求(DemandSource) key: sourceReference === salesOrderNo（#958）
//   3. schedule-urgency     APS/排程紧急度         key: OrderUrgency.businessReference === salesOrderNo（#1053）
//   4. delivery-order       发货单                key: DeliveryOrder.salesOrderNo === salesOrderNo
//   5. receivable           应收(by-source)       key: Receivable.sourceDocumentNo === deliveryOrderNo
//                            （后端 WmsOutboundOrderCompleted→CreateAccountReceivable 以发货单号为源单号）
//
// 尚未建立关联（generated 契约里没有到销售订单的持久关联字段，静态显示规则说明）：
//   mrp-suggestion / mes-work-order / production-report / quality-result /
//   finished-goods-receipt / finished-goods-inventory / wms-outbound / voucher
// ---------------------------------------------------------------------------

export type FulfillmentNodeKey =
  | 'sales-order'
  | 'production-demand'
  | 'mrp-suggestion'
  | 'schedule-urgency'
  | 'mes-work-order'
  | 'production-report'
  | 'quality-result'
  | 'finished-goods-receipt'
  | 'finished-goods-inventory'
  | 'delivery-order'
  | 'wms-outbound'
  | 'receivable'
  | 'voucher'

/**
 * 节点状态机（A1 规范：加载 / 空态 / 错误 / 403 / 409 / 超时可区分）：
 * - loading      加载中（该单源正在拉取）
 * - established  已确认（拿到可读业务编号 + 状态 + 下钻链接）
 * - pending      尚未产生（有稳定关联键，但上游还没生成该单据 → 空态 + 规则说明）
 * - unlinked     尚未建立关联（契约里没有到 SO 的稳定关联键 → 静态规则说明）
 * - restricted   权限受限（403，不泄露数据）
 * - failed       单源失败（409 / 超时 / 其它错误，仅影响本节点并展示重试）
 */
export type FulfillmentNodeStatus =
  | 'loading'
  | 'established'
  | 'pending'
  | 'unlinked'
  | 'restricted'
  | 'failed'

export type FulfillmentFailureKind = 'conflict' | 'timeout' | 'error'

export interface FulfillmentNode {
  key: FulfillmentNodeKey
  /** 节点名。 */
  title: string
  status: FulfillmentNodeStatus
  /** established：可读业务编号（绝不裸 GUID）。 */
  businessNo?: string
  /** established：单据当前状态。 */
  detailStatus?: string
  /** established：最近更新时间（ISO）。 */
  updatedAt?: string
  /** established：使用的关联键说明，供演示时核对来源。 */
  linkLabel?: string
  /** established：下钻到真实页面。 */
  drill?: RouteLocationRaw
  /** pending / unlinked：规则说明（该节点由什么产生 / 为何尚无稳定关联）。 */
  ruleNote?: string
  /** 数据源与新鲜度标注（失败不可伪装为空态）。 */
  source?: string
  /** failed：失败子类，用于区分 409 / 超时 / 其它。 */
  failureKind?: FulfillmentFailureKind
}

// 单源失败载体：把 HTTP 语义带到 useQuery.error，供状态机分类。
export class FulfillmentNodeError extends Error {
  constructor(
    readonly httpStatus: number | 'network',
    message?: string,
  ) {
    super(message)
    this.name = 'FulfillmentNodeError'
  }
}

interface SdkCallResult<T> {
  data?: T
  error?: unknown
  response?: Response
}

// 用 generated sdk 原始函数（throwOnError:false）拿到 response.status，才能区分 403/409/超时。
async function runNodeSource<T>(call: () => Promise<SdkCallResult<T>>): Promise<T | undefined> {
  let result: SdkCallResult<T>
  try {
    result = await call()
  } catch (cause) {
    // fetch 层抛错（超时 / 网络中断 / abort）— 归为 network 失败域。
    throw new FulfillmentNodeError('network', cause instanceof Error ? cause.message : undefined)
  }
  const status = result.response?.status ?? 0
  if (status === 403) {
    throw new FulfillmentNodeError(403, '无权查看该节点')
  }
  // 404 视为「尚未产生」（空态），不是失败。
  if (status === 404) {
    return undefined
  }
  if (result.error !== undefined || (status !== 0 && status >= 400)) {
    throw new FulfillmentNodeError(
      status || 'network',
      result.error instanceof Error ? result.error.message : undefined,
    )
  }
  return result.data
}

// 防御式读取错误上的 HTTP 状态（兼容 FulfillmentNodeError 与裸对象）。
function httpStatusOf(error: unknown): number | 'network' | undefined {
  if (error instanceof FulfillmentNodeError) return error.httpStatus
  if (typeof error === 'object' && error !== null) {
    const record = error as Record<string, unknown>
    const raw =
      record.status ?? record.statusCode ?? (record.response as Response | undefined)?.status
    if (typeof raw === 'number') return raw
  }
  return undefined
}

/** 把单源错误分类为 restricted（403）或 failed + 失败子类。 */
export function classifyFulfillmentFailure(error: unknown): {
  status: 'restricted' | 'failed'
  failureKind?: FulfillmentFailureKind
} {
  const status = httpStatusOf(error)
  if (status === 403) return { status: 'restricted' }
  if (status === 409) return { status: 'failed', failureKind: 'conflict' }
  if (status === 408 || status === 504 || status === 'network') {
    return { status: 'failed', failureKind: 'timeout' }
  }
  return { status: 'failed', failureKind: 'error' }
}

/** 规范化销售订单号：去空白，空串→undefined（空 scope 不发请求）。 */
export function normalizeScope(value: string | null | undefined): string | undefined {
  const trimmed = value?.trim()
  return trimmed ? trimmed : undefined
}

/** DemandSource 关联匹配：sourceReference === salesOrderNo（#958 桥接键）。 */
export function matchDemandSource(
  items: readonly BusinessConsoleDemandSourceItem[] | undefined,
  salesOrderNo: string | undefined,
): BusinessConsoleDemandSourceItem | undefined {
  if (!salesOrderNo) return undefined
  return items?.find((item) => item.sourceReference === salesOrderNo)
}

/** 发货单关联匹配：DeliveryOrder.salesOrderNo === salesOrderNo。 */
export function matchDeliveryOrders(
  items: readonly BusinessConsoleErpDeliveryOrderItem[] | undefined,
  salesOrderNo: string | undefined,
): BusinessConsoleErpDeliveryOrderItem[] {
  if (!salesOrderNo) return []
  return (items ?? []).filter((item) => item.salesOrderNo === salesOrderNo)
}

interface RecordNodeInput<T> {
  key: FulfillmentNodeKey
  title: string
  /** 是否具备发请求的 scope；false → 尚未产生（空 scope 不发请求）。 */
  enabled: boolean
  loading: boolean
  error: unknown
  /** 已匹配到的稳定关联记录；undefined 表示查完没有。 */
  record: T | undefined
  present: (record: T) => {
    businessNo?: string
    detailStatus?: string
    updatedAt?: string
    linkLabel?: string
    drill?: RouteLocationRaw
  }
  /** pending（尚未产生 / 等待上游）时的规则说明。 */
  pendingNote: string
  source: string
}

/** 已接入节点的状态机核心：把一次查询快照解析为节点视图。 */
export function resolveRecordNode<T>(input: RecordNodeInput<T>): FulfillmentNode {
  const base: FulfillmentNode = {
    key: input.key,
    title: input.title,
    status: 'pending',
    source: input.source,
  }
  if (input.error !== undefined && input.error !== null) {
    const classified = classifyFulfillmentFailure(input.error)
    return { ...base, status: classified.status, failureKind: classified.failureKind }
  }
  if (!input.enabled) {
    return { ...base, status: 'pending', ruleNote: input.pendingNote }
  }
  if (input.loading && input.record === undefined) {
    return { ...base, status: 'loading' }
  }
  if (input.record !== undefined) {
    return { ...base, status: 'established', ...input.present(input.record) }
  }
  return { ...base, status: 'pending', ruleNote: input.pendingNote }
}

interface UnlinkedNodeSpec {
  key: FulfillmentNodeKey
  title: string
  /** 为何尚无稳定关联（诚实说明，不猜测）。 */
  ruleNote: string
}

// 尚未建立关联的节点：generated 契约里没有到 SO 的持久关联字段。静态、无请求。
const UNLINKED_NODES: readonly UnlinkedNodeSpec[] = [
  {
    key: 'mrp-suggestion',
    title: 'MRP 建议',
    ruleNote:
      'MRP 建议按 SKU/工厂净需求生成，当前契约未暴露到销售订单的持久关联键（仅 runId/skuCode），不按相似编号猜测。',
  },
  {
    key: 'mes-work-order',
    title: 'MES 工单',
    ruleNote:
      'MES 工单以 SKU/生产版本排产，工单列表契约无销售订单来源字段，尚未建立到本单的稳定关联。',
  },
  {
    key: 'production-report',
    title: '生产报工',
    ruleNote: '生产报工/产出批次以工单为键，需先建立 销售订单→工单 的稳定关联后才能回溯。',
  },
  {
    key: 'quality-result',
    title: '质量结果 / NCR / hold',
    ruleNote: '质量检验任务的来源单据指向工单（sourceDocumentId），当前无到销售订单的持久关联键。',
  },
  {
    key: 'finished-goods-receipt',
    title: '完工入库',
    ruleNote: '完工入库请求以工单为键（requestNo/workOrderNo），尚未建立到本销售订单的稳定关联。',
  },
  {
    key: 'finished-goods-inventory',
    title: '成品批次与库存',
    ruleNote:
      '成品库存联动以完工入库单号（requestNo，#972）为键，需先接通上游工单关联才能回溯到本单。',
  },
  {
    key: 'wms-outbound',
    title: 'WMS 出库',
    ruleNote: 'WMS 出库单列表契约仅暴露出库单号与状态，无发货单/销售订单来源字段，暂不关联。',
  },
  {
    key: 'voucher',
    title: '凭证',
    ruleNote: '会计凭证按科目借贷过账，凭证列表契约无单据级来源字段，无法稳定关联到销售订单。',
  },
]

function unlinkedNode(spec: UnlinkedNodeSpec): FulfillmentNode {
  return {
    key: spec.key,
    title: spec.title,
    status: 'unlinked',
    ruleNote: spec.ruleNote,
    source: '契约暂无稳定关联键',
  }
}

/**
 * 履约追踪时间线 composable。
 * 每个已接入节点是独立 query、独立失败域；空 scope（无销售订单号）不发任何请求。
 */
export function useFulfillmentTimeline(
  salesOrder: MaybeRefOrGetter<BusinessConsoleErpSalesOrderItem | null | undefined>,
) {
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

  // —— 2. 生产需求（DemandSource.sourceReference === salesOrderNo）——
  const demandQuery = useQuery(() => ({
    key: ['fulfillment', 'demand', ctx.organizationId, ctx.environmentId, salesOrderNo.value ?? ''],
    query: () =>
      runNodeSource<{ items?: BusinessConsoleDemandSourceItem[] } | null>(async () => {
        const { data, error, response } = await listBusinessConsolePlanningDemands({
          query: { organizationId: ctx.organizationId, environmentId: ctx.environmentId },
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

  // —— 3. APS/排程紧急度（OrderUrgency.businessReference === salesOrderNo）——
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

  // —— 4. 发货单（DeliveryOrder.salesOrderNo === salesOrderNo）——
  const deliveryQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'delivery',
      ctx.organizationId,
      ctx.environmentId,
      salesOrderNo.value ?? '',
    ],
    query: () =>
      runNodeSource<{ items?: BusinessConsoleErpDeliveryOrderItem[] } | null>(async () => {
        const { data, error, response } = await listBusinessConsoleErpDeliveryOrders({
          query: {
            organizationId: ctx.organizationId,
            environmentId: ctx.environmentId,
            keyword: salesOrderNo.value,
            take: 50,
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

  const matchedDeliveries = computed(() =>
    matchDeliveryOrders(deliveryQuery.data.value?.items, salesOrderNo.value),
  )
  // 应收源单号 = 本销售订单已确认发货单的单号（后端以发货单号作应收源单号）。
  const receivableSourceNo = computed(
    () => matchedDeliveries.value[0]?.deliveryOrderNo ?? undefined,
  )

  // —— 5. 应收（Receivable.sourceDocumentNo === deliveryOrderNo）——
  const receivableQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'receivable',
      ctx.organizationId,
      ctx.environmentId,
      receivableSourceNo.value ?? '',
    ],
    query: () =>
      runNodeSource<BusinessConsoleErpReceivableSourceDocumentResponse | null>(async () => {
        const { data, error, response } = await getBusinessConsoleErpReceivableBySourceDocument({
          query: {
            organizationId: ctx.organizationId,
            environmentId: ctx.environmentId,
            sourceDocumentNo: receivableSourceNo.value ?? '',
          },
          throwOnError: false,
        })
        return {
          data: data?.success ? (data.data ?? null) : null,
          error: data?.success === false ? data : error,
          response,
        }
      }),
    enabled: hasBusinessContext(ctx) && Boolean(receivableSourceNo.value),
  }))

  const nodes = computed<FulfillmentNode[]>(() => {
    const so = salesOrderNo.value
    const demand = matchDemandSource(demandQuery.data.value?.items, so)
    const urgency = urgencyQuery.data.value?.find(
      (item) => item.businessReference === so || item.orderId === so,
    )
    const delivery = matchedDeliveries.value[0]
    const receivable = receivableQuery.data.value ?? undefined

    const salesOrderNode: FulfillmentNode = order.value
      ? {
          key: 'sales-order',
          title: '销售订单',
          status: 'established',
          businessNo: order.value.salesOrderNo ?? undefined,
          detailStatus: order.value.status ?? undefined,
          linkLabel: `salesOrderNo = ${order.value.salesOrderNo ?? '-'}`,
          drill: order.value.salesOrderNo
            ? { path: '/erp/sales/orders', query: { keyword: order.value.salesOrderNo } }
            : undefined,
          source: 'ERP · 销售订单读面',
        }
      : {
          key: 'sales-order',
          title: '销售订单',
          status: 'pending',
          ruleNote: '未选择销售订单。',
          source: 'ERP · 销售订单读面',
        }

    return [
      salesOrderNode,
      resolveRecordNode<BusinessConsoleDemandSourceItem>({
        key: 'production-demand',
        title: '生产需求',
        enabled: hasScope.value,
        loading: demandQuery.isLoading.value,
        error: demandQuery.error.value,
        record: demand,
        present: (record) => ({
          businessNo: record.sourceReference ?? record.demandType ?? undefined,
          detailStatus: record.sourceStatus ?? undefined,
          linkLabel: `sourceReference = ${record.sourceReference ?? '-'}`,
          drill: { path: '/planning' },
        }),
        pendingNote:
          '销售订单确认后由需求编排生成生产需求（DemandSource.sourceReference = 销售单号，#958），当前尚未产生。',
        source: 'Planning · 需求源读面',
      }),
      unlinkedNode(UNLINKED_NODES[0]!), // mrp-suggestion
      resolveRecordNode<BusinessConsoleOrderUrgency>({
        key: 'schedule-urgency',
        title: 'APS / 排程紧急度',
        enabled: hasScope.value,
        loading: urgencyQuery.isLoading.value,
        error: urgencyQuery.error.value,
        record: urgency,
        present: (record) => ({
          businessNo: record.businessReference ?? record.orderId ?? undefined,
          detailStatus: record.level ?? undefined,
          updatedAt: record.calculatedAtUtc ?? undefined,
          linkLabel: `businessReference = ${record.businessReference ?? '-'}`,
          drill: { path: '/scheduling' },
        }),
        pendingNote:
          '进入排程后由 APS 计算订单紧急度（OrderUrgency.businessReference = 销售单号，#1053），当前尚未生成。',
        source: 'Scheduling · 订单紧急度读面',
      }),
      unlinkedNode(UNLINKED_NODES[1]!), // mes-work-order
      unlinkedNode(UNLINKED_NODES[2]!), // production-report
      unlinkedNode(UNLINKED_NODES[3]!), // quality-result
      unlinkedNode(UNLINKED_NODES[4]!), // finished-goods-receipt
      unlinkedNode(UNLINKED_NODES[5]!), // finished-goods-inventory
      resolveRecordNode<BusinessConsoleErpDeliveryOrderItem>({
        key: 'delivery-order',
        title: '发货单',
        enabled: hasScope.value,
        loading: deliveryQuery.isLoading.value,
        error: deliveryQuery.error.value,
        record: delivery,
        present: (record) => ({
          businessNo: record.deliveryOrderNo ?? undefined,
          detailStatus: record.status ?? undefined,
          updatedAt: record.shippedAtUtc ?? record.releasedAtUtc ?? undefined,
          linkLabel: `salesOrderNo = ${record.salesOrderNo ?? '-'}`,
          drill: record.salesOrderNo
            ? { path: '/erp/sales/deliveries', query: { keyword: record.salesOrderNo } }
            : { path: '/erp/sales/deliveries' },
        }),
        pendingNote:
          '销售订单履约时生成发货单（DeliveryOrder.salesOrderNo = 销售单号），当前尚未产生。',
        source: 'ERP · 发货单读面',
      }),
      unlinkedNode(UNLINKED_NODES[6]!), // wms-outbound
      resolveRecordNode<BusinessConsoleErpReceivableSourceDocumentResponse>({
        key: 'receivable',
        title: '应收',
        enabled: hasBusinessContext(ctx) && Boolean(receivableSourceNo.value),
        loading: receivableQuery.isLoading.value,
        error: receivableQuery.error.value,
        record: receivable,
        present: (record) => ({
          businessNo: record.receivableNo ?? undefined,
          detailStatus: record.openAmount != null ? `未结 ${record.openAmount}` : undefined,
          updatedAt: record.createdAtUtc ?? undefined,
          linkLabel: `sourceDocumentNo = ${record.sourceDocumentNo ?? '-'}（发货单号）`,
          drill: { path: '/erp/finance/ar-ap' },
        }),
        pendingNote: receivableSourceNo.value
          ? 'WMS 出库完成后由后端按发货单号生成应收（Receivable.sourceDocumentNo = 发货单号），当前尚未生成。'
          : '需先生成发货单，才能按发货单号回溯应收（尚无可用源单号）。',
        source: 'ERP · 应收 by-source 读面',
      }),
      unlinkedNode(UNLINKED_NODES[7]!), // voucher
    ]
  })

  const pending = computed(
    () =>
      demandQuery.isLoading.value ||
      urgencyQuery.isLoading.value ||
      deliveryQuery.isLoading.value ||
      receivableQuery.isLoading.value,
  )

  function retry(key: FulfillmentNodeKey) {
    switch (key) {
      case 'production-demand':
        void demandQuery.refetch()
        break
      case 'schedule-urgency':
        void urgencyQuery.refetch()
        break
      case 'delivery-order':
        void deliveryQuery.refetch()
        break
      case 'receivable':
        void receivableQuery.refetch()
        break
      default:
        break
    }
  }

  function refreshAll() {
    void demandQuery.refetch()
    void urgencyQuery.refetch()
    void deliveryQuery.refetch()
    void receivableQuery.refetch()
  }

  return { nodes, pending, hasScope, salesOrderNo, retry, refreshAll }
}
