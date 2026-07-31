import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleErpDeliveryOrderItem,
  BusinessConsoleErpReceivableSourceDocumentResponse,
  BusinessConsoleErpSalesOrderItem,
  BusinessConsoleMesProductionPlanRow,
  BusinessConsoleMrpPeggingItem,
  BusinessConsoleMrpRunItem,
  BusinessConsoleOrderUrgency,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import {
  getBusinessConsoleErpReceivableBySourceDocument,
  getBusinessConsolePlanningMrpPegging,
  listBusinessConsoleErpDeliveryOrders,
  listBusinessConsoleMesProductionPlans,
  listBusinessConsoleOrderUrgencies,
  listBusinessConsolePlanningDemands,
  listBusinessConsolePlanningMrpRuns,
  listBusinessConsolePlanningSuggestions,
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
//   3. mrp-suggestion       MRP 建议              key: MrpPeggingItem.demandSourceReference === salesOrderNo
//                            （peggingType='demand' 行 → suggestionId）
//   4. schedule-urgency     APS/排程紧急度         key: OrderUrgency.businessReference === salesOrderNo（#1053）
//   5. mes-work-order       MES 工单              key: PlanningSuggestionItem.downstreamDocumentId
//                            （suggestionId 来自第 3 跳；合批工单对每张订单都有 pegging 行，
//                              两张订单都能各自走通到同一张工单）
//                            辅证: MesProductionPlanRow.sourceDocumentId === suggestionId
//   6. delivery-order       发货单                key: DeliveryOrder.salesOrderNo === salesOrderNo
//   7. receivable           应收(by-source)       key: Receivable.sourceDocumentNo === deliveryOrderNo
//                            （后端 WmsOutboundOrderCompleted→CreateAccountReceivable 以发货单号为源单号）
//
// 尚未建立关联（generated 契约里没有到销售订单的持久关联字段，静态显示规则说明）：
//   production-report / quality-result / finished-goods-receipt /
//   finished-goods-inventory / wms-outbound / voucher
// ---------------------------------------------------------------------------

/**
 * MRP pegging 只能按 runId 查，而运行历史没有「按需求源找运行」的读面。
 * 因此从最新一次运行开始向前扫这么多次运行，命中即停——既能覆盖「刚跑完又跑一次」，
 * 又不会把整段运行历史都拉一遍。扫完仍无命中即如实空态，绝不按相似编号猜测。
 */
export const FULFILLMENT_MRP_RUN_SCAN_LIMIT = 5

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

/**
 * MRP pegging 关联匹配：只认 `peggingType = 'demand'` 且
 * `demandSourceReference === salesOrderNo` 的行（scheduled-receipt 行的引用是
 * `系统:单据类型:单号` 复合串，不是销售单号，必须排除）。
 *
 * 合批建议对每一张被合并的订单都保留一行 demand pegging，所以两张订单各自都能命中
 * 同一个 suggestionId——这正是合批场景下两条时间线都能点亮的依据。
 */
export function matchDemandPeggings(
  items: readonly BusinessConsoleMrpPeggingItem[] | undefined,
  salesOrderNo: string | undefined,
): BusinessConsoleMrpPeggingItem[] {
  if (!salesOrderNo) return []
  return (items ?? []).filter(
    (item) =>
      item.peggingType?.trim().toLowerCase() === 'demand' &&
      item.demandSourceReference?.trim() === salesOrderNo &&
      Boolean(item.suggestionId?.trim()),
  )
}

/** 从命中的 pegging 行取去重后的建议标识（保持命中顺序）。 */
export function peggingSuggestionIds(items: readonly BusinessConsoleMrpPeggingItem[]): string[] {
  const ids: string[] = []
  for (const item of items) {
    const id = item.suggestionId?.trim()
    if (id && !ids.includes(id)) ids.push(id)
  }
  return ids
}

/** 下游引用码值大小写/分隔符两侧口径不一（BusinessMes / business-mes），统一归一化后比较。 */
function normalizeReferenceToken(value: string | null | undefined): string {
  return (value ?? '').toLowerCase().replace(/[^a-z0-9]/g, '')
}

/** 建议本体匹配：suggestionId 命中本单 pegging 的建议集合。 */
export function matchPlanningSuggestion(
  items: readonly BusinessConsolePlanningSuggestionItem[] | undefined,
  suggestionIds: readonly string[],
): BusinessConsolePlanningSuggestionItem | undefined {
  if (suggestionIds.length === 0) return undefined
  return (items ?? []).find((item) => {
    const id = item.suggestionId?.trim()
    return Boolean(id) && suggestionIds.includes(id!)
  })
}

/**
 * 建议 → MES 工单：接受建议时后端把工单号回写为建议的下游引用
 * （downstreamService=BusinessMes / downstreamDocumentType=WorkOrder / downstreamDocumentId=工单号）。
 * 只认这三项齐备的行，绝不把别的下游单据（采购申请）当工单。
 */
export function matchSuggestionWorkOrderNo(
  items: readonly BusinessConsolePlanningSuggestionItem[] | undefined,
  suggestionIds: readonly string[],
): string | undefined {
  if (suggestionIds.length === 0) return undefined
  for (const item of items ?? []) {
    const id = item.suggestionId?.trim()
    if (!id || !suggestionIds.includes(id)) continue
    if (normalizeReferenceToken(item.downstreamService) !== 'businessmes') continue
    if (normalizeReferenceToken(item.downstreamDocumentType) !== 'workorder') continue
    const workOrderNo = item.downstreamDocumentId?.trim()
    if (workOrderNo) return workOrderNo
  }
  return undefined
}

/**
 * 生产计划行（即带来源引用的 MES 工单）匹配：`sourceDocumentId === suggestionId`。
 *
 * 合批工单的 `sourceDemandReference` 只是需求引用集合里的**第一条**，可能是别的订单号，
 * 因此它只能作为补充命中口径，绝不能拿来把本单排除掉。
 */
export function matchProductionPlanRow(
  rows: readonly BusinessConsoleMesProductionPlanRow[] | undefined,
  suggestionIds: readonly string[],
  salesOrderNo: string | undefined,
): BusinessConsoleMesProductionPlanRow | undefined {
  return (rows ?? []).find((row) => {
    const sourceDocumentId = row.sourceDocumentId?.trim()
    if (sourceDocumentId && suggestionIds.includes(sourceDocumentId)) return true
    return Boolean(salesOrderNo) && row.sourceDemandReference?.trim() === salesOrderNo
  })
}

/** MRP 建议节点的记录：命中的 pegging 行 + （若仍在建议列表里）建议本体。 */
export interface MrpSuggestionRecord {
  pegging: BusinessConsoleMrpPeggingItem
  suggestion?: BusinessConsolePlanningSuggestionItem
}

/** MES 工单节点的记录：工单号（人读）+ 该工单的来源引用行（补状态）。 */
export interface MesWorkOrderRecord {
  workOrderNo: string
  planRow?: BusinessConsoleMesProductionPlanRow
}

/**
 * MRP 建议没有人读单号（suggestionId 是 GUID，与需求与计划工作台一致不上屏），
 * 因此用「物料 × 数量」这一组业务事实自识别。
 */
export function describeMrpSuggestion(record: MrpSuggestionRecord): string {
  const skuCode =
    record.suggestion?.skuCode?.trim() ||
    record.pegging.parentSkuCode?.trim() ||
    record.pegging.componentSkuCode?.trim() ||
    ''
  const quantity = record.suggestion?.quantity ?? record.pegging.quantity
  const uomCode = record.suggestion?.uomCode?.trim() ?? ''
  const amount = quantity == null ? '' : `${quantity}${uomCode ? ` ${uomCode}` : ''}`
  return [skuCode, amount].filter(Boolean).join(' × ') || '计划建议'
}

/**
 * 工单关联键说明。合批工单的首条需求引用可能是别的订单号——这时明说「与…等订单合批」，
 * 不让人误以为这张工单只为本单而开。
 */
export function describeWorkOrderLink(
  record: MesWorkOrderRecord,
  salesOrderNo: string | undefined,
): string {
  const base = `demandSourceReference = ${salesOrderNo ?? '-'}（MRP 建议 → 工单 ${record.workOrderNo}）`
  const primaryDemand = record.planRow?.sourceDemandReference?.trim()
  return primaryDemand && salesOrderNo && primaryDemand !== salesOrderNo
    ? `${base}；该工单为合批工单，同时承接 ${primaryDemand} 等订单`
    : base
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
  title: string
  /** 为何尚无稳定关联（诚实说明，不猜测）。 */
  ruleNote: string
}

// 尚未建立关联的节点：这些读面到本单还缺一条稳定关联键，或读面本身尚未接进这条时间线。
// 静态、无请求；按 key 索引，避免顺序调整时错位。
const UNLINKED_NODES: Readonly<Partial<Record<FulfillmentNodeKey, UnlinkedNodeSpec>>> = {
  'production-report': {
    title: '生产报工',
    ruleNote: '生产报工与产出批次以工单为键，可从上方工单继续下钻；本时间线尚未直接汇总。',
  },
  'quality-result': {
    title: '质量结果 / NCR / hold',
    ruleNote: '质量检验任务的来源单据指向工单，可从上方工单继续下钻；本时间线尚未直接汇总。',
  },
  'finished-goods-receipt': {
    title: '完工入库',
    ruleNote: '完工入库请求以工单号为键，可从上方工单继续下钻；本时间线尚未直接汇总。',
  },
  'finished-goods-inventory': {
    title: '成品批次与库存',
    ruleNote: '成品库存联动以完工入库单号为键，需先经完工入库才能回溯到本单。',
  },
  'wms-outbound': {
    title: 'WMS 出库',
    ruleNote: 'WMS 出库单列表契约仅暴露出库单号与状态，无发货单/销售订单来源字段，暂不关联。',
  },
  voucher: {
    title: '凭证',
    ruleNote: '会计凭证按科目借贷过账，凭证列表契约无单据级来源字段，无法稳定关联到销售订单。',
  },
}

function unlinkedNode(key: FulfillmentNodeKey): FulfillmentNode {
  const spec = UNLINKED_NODES[key]
  return {
    key,
    title: spec?.title ?? key,
    status: 'unlinked',
    ruleNote: spec?.ruleNote,
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

  // —— 3. MRP 建议（MrpPeggingItem.demandSourceReference === salesOrderNo）——
  // pegging 只能按 runId 查，先取运行列表（后端按创建时间倒序），再从最新一次向前扫。
  const mrpRunsQuery = useQuery(() => ({
    key: ['fulfillment', 'mrp-runs', ctx.organizationId, ctx.environmentId],
    query: () =>
      runNodeSource<{ items?: BusinessConsoleMrpRunItem[] } | null>(async () => {
        const { data, error, response } = await listBusinessConsolePlanningMrpRuns({
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

  const scanRunIds = computed(() =>
    (mrpRunsQuery.data.value?.items ?? [])
      .map((run) => run.runId?.trim())
      .filter((runId): runId is string => Boolean(runId))
      .slice(0, FULFILLMENT_MRP_RUN_SCAN_LIMIT),
  )

  const peggingQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'pegging',
      ctx.organizationId,
      ctx.environmentId,
      salesOrderNo.value ?? '',
      scanRunIds.value.join('|'),
    ],
    query: async () => {
      for (const runId of scanRunIds.value) {
        const data = await runNodeSource<{ items?: BusinessConsoleMrpPeggingItem[] } | null>(
          async () => {
            const { data, error, response } = await getBusinessConsolePlanningMrpPegging({
              path: { runId },
              query: { organizationId: ctx.organizationId, environmentId: ctx.environmentId },
              throwOnError: false,
            })
            return {
              data: data?.success ? (data.data ?? null) : null,
              error: data?.success === false ? data : error,
              response,
            }
          },
        )
        const matched = matchDemandPeggings(data?.items, salesOrderNo.value)
        if (matched.length > 0) return matched
      }
      return [] as BusinessConsoleMrpPeggingItem[]
    },
    enabled: hasScope.value && scanRunIds.value.length > 0,
  }))

  const matchedPeggings = computed(() => peggingQuery.data.value ?? [])
  const suggestionIds = computed(() => peggingSuggestionIds(matchedPeggings.value))
  // MRP 这一跳由「运行列表 + pegging 扫描」两个请求共同构成，任一失败都属该节点失败域。
  const mrpError = computed(() => mrpRunsQuery.error.value ?? peggingQuery.error.value)
  const mrpLoading = computed(() => mrpRunsQuery.isLoading.value || peggingQuery.isLoading.value)

  // —— 建议本体（拿人读的 SKU/数量/状态与下游工单引用；suggestionId 是 GUID，不上屏）——
  const suggestionQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'planning-suggestions',
      ctx.organizationId,
      ctx.environmentId,
      suggestionIds.value.join('|'),
    ],
    query: () =>
      runNodeSource<{ items?: BusinessConsolePlanningSuggestionItem[] } | null>(async () => {
        const { data, error, response } = await listBusinessConsolePlanningSuggestions({
          query: { organizationId: ctx.organizationId, environmentId: ctx.environmentId },
          throwOnError: false,
        })
        return {
          data: data?.success ? (data.data ?? null) : null,
          error: data?.success === false ? data : error,
          response,
        }
      }),
    enabled: hasScope.value && suggestionIds.value.length > 0,
  }))

  const matchedSuggestion = computed(() =>
    matchPlanningSuggestion(suggestionQuery.data.value?.items, suggestionIds.value),
  )
  const workOrderNo = computed(() =>
    matchSuggestionWorkOrderNo(suggestionQuery.data.value?.items, suggestionIds.value),
  )

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

  // —— 5. MES 工单：来源引用行（生产计划读面即「带来源引用的工单」）补齐工单当前状态 ——
  // keyword 优先用 suggestionId（对合批工单也只有这一条能精确命中），退回销售单号兜底。
  const planKeyword = computed(() => suggestionIds.value[0] ?? salesOrderNo.value)
  const productionPlanQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'mes-production-plan',
      ctx.organizationId,
      ctx.environmentId,
      planKeyword.value ?? '',
    ],
    query: () =>
      runNodeSource<{ items?: BusinessConsoleMesProductionPlanRow[] } | null>(async () => {
        const { data, error, response } = await listBusinessConsoleMesProductionPlans({
          query: {
            organizationId: ctx.organizationId,
            environmentId: ctx.environmentId,
            keyword: planKeyword.value,
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
    enabled: hasBusinessContext(ctx) && Boolean(planKeyword.value),
  }))

  const matchedPlanRow = computed(() =>
    matchProductionPlanRow(
      productionPlanQuery.data.value?.items,
      suggestionIds.value,
      salesOrderNo.value,
    ),
  )

  // 工单这一跳串了 pegging → 建议 → 生产计划行三段，任一段失败都属该节点失败域。
  const workOrderError = computed(
    () => mrpError.value ?? suggestionQuery.error.value ?? productionPlanQuery.error.value,
  )
  const workOrderLoading = computed(
    () =>
      mrpLoading.value || suggestionQuery.isLoading.value || productionPlanQuery.isLoading.value,
  )

  // —— 6. 发货单（DeliveryOrder.salesOrderNo === salesOrderNo）——
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

  // —— 7. 应收（Receivable.sourceDocumentNo === deliveryOrderNo）——
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
    const pegging = matchedPeggings.value[0]
    const mrpSuggestionRecord: MrpSuggestionRecord | undefined = pegging
      ? { pegging, ...(matchedSuggestion.value ? { suggestion: matchedSuggestion.value } : {}) }
      : undefined
    const mesWorkOrderRecord: MesWorkOrderRecord | undefined = workOrderNo.value
      ? {
          workOrderNo: workOrderNo.value,
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
      resolveRecordNode<MrpSuggestionRecord>({
        key: 'mrp-suggestion',
        title: 'MRP 建议',
        enabled: hasScope.value,
        loading: mrpLoading.value,
        error: mrpError.value,
        record: mrpSuggestionRecord,
        present: (record) => ({
          businessNo: describeMrpSuggestion(record),
          detailStatus: record.suggestion?.status ?? undefined,
          updatedAt: record.suggestion?.requiredDate ?? undefined,
          linkLabel: `demandSourceReference = ${record.pegging.demandSourceReference ?? '-'}`,
          drill: { path: '/planning' },
        }),
        pendingNote:
          'MRP 运行后按需求源生成建议，并把本销售订单 peg 到建议上（pegging.demandSourceReference = 销售单号），当前尚未产生。',
        source: 'Planning · MRP 运行与 pegging 读面',
      }),
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
      resolveRecordNode<MesWorkOrderRecord>({
        key: 'mes-work-order',
        title: 'MES 工单',
        enabled: hasScope.value,
        loading: workOrderLoading.value,
        error: workOrderError.value,
        record: mesWorkOrderRecord,
        present: (record) => ({
          businessNo: record.workOrderNo,
          detailStatus: record.planRow?.status ?? undefined,
          updatedAt: record.planRow?.plannedStartUtc ?? undefined,
          linkLabel: describeWorkOrderLink(record, so),
          drill: { path: `/mes/work-orders/${encodeURIComponent(record.workOrderNo)}` },
        }),
        pendingNote:
          '计划员在需求与计划工作台接受 MRP 建议后才会开出 MES 工单（建议的下游引用即工单号），当前尚未开单。',
        source: 'Planning · 建议下游引用 + MES 工单来源引用',
      }),
      unlinkedNode('production-report'),
      unlinkedNode('quality-result'),
      unlinkedNode('finished-goods-receipt'),
      unlinkedNode('finished-goods-inventory'),
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
      unlinkedNode('wms-outbound'),
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
      unlinkedNode('voucher'),
    ]
  })

  const pending = computed(
    () =>
      demandQuery.isLoading.value ||
      mrpLoading.value ||
      urgencyQuery.isLoading.value ||
      suggestionQuery.isLoading.value ||
      productionPlanQuery.isLoading.value ||
      deliveryQuery.isLoading.value ||
      receivableQuery.isLoading.value,
  )

  // MRP / 工单两跳各自串了多个请求，重试要把整跳重来，否则前一段的失败会一直卡住。
  function retryMrp() {
    void mrpRunsQuery.refetch()
    void peggingQuery.refetch()
  }

  function retryWorkOrder() {
    retryMrp()
    void suggestionQuery.refetch()
    void productionPlanQuery.refetch()
  }

  function retry(key: FulfillmentNodeKey) {
    switch (key) {
      case 'production-demand':
        void demandQuery.refetch()
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
        void receivableQuery.refetch()
        break
      default:
        break
    }
  }

  function refreshAll() {
    void demandQuery.refetch()
    retryWorkOrder()
    void urgencyQuery.refetch()
    void deliveryQuery.refetch()
    void receivableQuery.refetch()
  }

  return { nodes, pending, hasScope, salesOrderNo, retry, refreshAll }
}
