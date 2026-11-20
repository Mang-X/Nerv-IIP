import { useMesPrincipalWorkScope } from './useBusinessMes'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleErpDeliveryOrderItem,
  BusinessConsoleErpReceivableItem,
  BusinessConsoleErpSalesOrderItem,
  BusinessConsoleMesProductionPlanRow,
  BusinessConsoleMrpPeggingItem,
  BusinessConsoleMrpRunItem,
  BusinessConsoleOrderUrgency,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import {
  getBusinessConsoleErpReceivableBySourceDocument,
  listBusinessConsoleErpReceivables,
  getBusinessConsolePlanningMrpPegging,
  listBusinessConsoleErpDeliveryOrders,
  getBusinessConsoleMesWorkOrderDetail,
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
import { urgencyLevelPresentation } from './useUrgencyDisplayMode'

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
 * MRP pegging 只能按 runId 查，而运行历史没有「按需求源找运行」的读面。
 * 因此从最新一次运行开始向前扫这么多次运行，**每一次都看、不提前收工**——
 * 接受建议后再跑一次 MRP，工单仍挂在旧建议上，只看最新一次就会把工单节点误判成空态。
 * 窗口保持小，避免把整段运行历史都拉一遍；扫完仍无命中即如实空态（文案里说明只看了最近
 * 若干次运行，不下「尚未产生」这种越界结论），绝不按相似编号猜测。
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
  /** established：单据当前状态**原始码值**——节点组件会拿它过状态词表。 */
  detailStatus?: string
  /**
   * established：已本地化好的状态文案，绕开状态词表直接上屏。
   *
   * 紧急度分级（特急/紧急/高风险/关注/正常）的词表归 `urgencyLevelPresentation`
   * 所有，不在共享 `STATUS_LABELS` 里。此前这里把它翻译完的中文塞进 `detailStatus`，
   * 节点组件照例拿去过 `resolveStatus()`——那函数吃的是裸码值，于是每次都报
   * 「词表缺失: 高风险」，只是回吐原值恰好还是对的中文，屏上看不出来。
   * 两套词表各归各位，别把翻译结果再喂给翻译器。
   */
  detailStatusLabel?: string
  /** established：最近更新时间（ISO）。 */
  updatedAt?: string
  /** established：补充说明（如合批工单同时承接的其它订单）。 */
  note?: string
  /** established：下钻到真实页面。 */
  drill?: RouteLocationRaw
  retryable?: boolean
  links?: { label: string; to: RouteLocationRaw }[]
  /** pending / unlinked：规则说明（该节点由什么产生 / 为何尚无稳定关联）。 */
  ruleNote?: string
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
  return items?.find(
    (item) => item.sourceReference === salesOrderNo && item.demandType === 'sales-order',
  )
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

/**
 * 下游引用码值大小写/分隔符两侧口径不一（`BusinessMes` / `business-mes`），统一归一化后比较。
 *
 * 导出供别处复用：计划工作台原来各写一份**严格字面量**比较，于是「对该单排产」按钮对种子
 * 数据（后端写的是 `business-mes`）一个都不渲染——功能做了却谁都看不见（#1262 / MAN-694，
 * 第五轮走查实测）。这类码值口径不一致在本仓库反复出现，比较逻辑只该有一处。
 */
export function normalizeReferenceToken(value: string | null | undefined): string {
  return (value ?? '').toLowerCase().replace(/[^a-z0-9]/g, '')
}

/** 该计划建议是否已承接成 MES 工单——两侧码值归一后判定，不做字面量比较。 */
export function isMesWorkOrderDownstream(
  service: string | null | undefined,
  documentType: string | null | undefined,
): boolean {
  return (
    normalizeReferenceToken(service) === 'businessmes' &&
    normalizeReferenceToken(documentType) === 'workorder'
  )
}

/**
 * 建议本体匹配：按 `suggestionIds` 的顺序（扫描窗内**新运行在前**）优先取新的那条，
 * 而不是按服务端返回顺序——重跑 MRP 后，本单最新一次的建议才是计划员看到的那条。
 */
export function matchPlanningSuggestion(
  items: readonly BusinessConsolePlanningSuggestionItem[] | undefined,
  suggestionIds: readonly string[],
): BusinessConsolePlanningSuggestionItem | undefined {
  for (const suggestionId of suggestionIds) {
    const matched = (items ?? []).find((item) => item.suggestionId?.trim() === suggestionId)
    if (matched) return matched
  }
  return undefined
}

/**
 * 建议 → MES 工单：接受建议时后端把工单号回写为建议的下游引用
 * （downstreamService=BusinessMes / downstreamDocumentType=WorkOrder / downstreamDocumentId=工单号）。
 * 只认这三项齐备的行，绝不把别的下游单据（采购申请）当工单。
 *
 * **重跑 MRP 后不能只看最新一条建议**：接受建议后再跑一次 MRP，本单会多出一条尚未被接受的
 * 新建议，而工单挂在**旧**建议上。所以这里遍历扫描窗内命中的全部建议，取第一条真正带工单
 * 引用的——否则工单明明在，节点却显示空态。
 */
export function matchSuggestionWorkOrder(
  items: readonly BusinessConsolePlanningSuggestionItem[] | undefined,
  suggestionIds: readonly string[],
): { suggestionId: string; workOrderNo: string } | undefined {
  for (const suggestionId of suggestionIds) {
    for (const item of items ?? []) {
      if (item.suggestionId?.trim() !== suggestionId) continue
      if (normalizeReferenceToken(item.downstreamService) !== 'businessmes') continue
      if (normalizeReferenceToken(item.downstreamDocumentType) !== 'workorder') continue
      const workOrderNo = item.downstreamDocumentId?.trim()
      if (workOrderNo) return { suggestionId, workOrderNo }
    }
  }
  return undefined
}

/**
 * 生产计划行（即带来源引用的 MES 工单）匹配：**只认** `sourceDocumentId === 解析出工单号的那条建议`。
 *
 * 不做「按销售单号兜底」：行上根本没有工单号（`productionPlanId` 就是 `sourceDocumentId`），
 * 无从校验兜底命中的行是不是解析出来的那张工单——合批 / 一单多工单时很容易把**别的工单**的
 * 状态贴到本节点上。宁可不贴状态，也不贴错状态。
 *
 * 反过来，合批工单的 `sourceDemandReference` 只是需求引用集合里的**第一条**，可能是别的订单号，
 * 因此它绝不能用来把本单排除掉——只在关联键说明里如实标注「合批」。
 */
export function matchProductionPlanRow(
  rows: readonly BusinessConsoleMesProductionPlanRow[] | undefined,
  suggestionId: string | undefined,
): BusinessConsoleMesProductionPlanRow | undefined {
  if (!suggestionId) return undefined
  return (rows ?? []).find((row) => row.sourceDocumentId?.trim() === suggestionId)
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
 * 合批说明。合批工单的首条需求引用可能是别的订单号——这时明说「同时承接…等订单」，
 * 不让人误以为这张工单只为本单而开。
 */
export function describeMergedWorkOrder(
  record: MesWorkOrderRecord,
  salesOrderNo: string | undefined,
): string | undefined {
  const primaryDemand = record.planRow?.sourceDemandReference?.trim()
  return primaryDemand && salesOrderNo && primaryDemand !== salesOrderNo
    ? `该工单为合批工单，同时承接 ${primaryDemand} 等订单`
    : undefined
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
  links?: FulfillmentNode['links']
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
    detailStatusLabel?: string
    updatedAt?: string
    note?: string
    drill?: RouteLocationRaw
  }
  /** pending（尚未产生 / 等待上游）时的规则说明。 */
  pendingNote: string
}

// 列表按包含关系搜索。逐页核对完整来源键，并给单次打开的时间线设定读取上限。
const pageSize = 50
const maxPages = 4
const receiptSourceLimit = 5

async function readCandidatePages<T>(
  read: (
    skip: number,
    take: number,
  ) => Promise<
    SdkCallResult<{ success?: boolean; data?: { items?: T[] | null; total?: number } | null }>
  >,
) {
  const items: T[] = []
  for (let page = 0; page < maxPages; page++) {
    const result = await runNodeSource(async () => {
      const { data, error, response } = await read(page * pageSize, pageSize)
      return { data: data?.data, error: data?.success === false ? data : error, response }
    })
    const rows = result?.items ?? []
    items.push(...rows)
    if (rows.length < pageSize || (result?.total != null && items.length >= result.total))
      return { items, limited: false }
  }
  return { items, limited: true }
}

/** 已接入节点的状态机核心：把一次查询快照解析为节点视图。 */
export function resolveRecordNode<T>(input: RecordNodeInput<T>): FulfillmentNode {
  const base: FulfillmentNode = {
    ...(input.links ? { links: input.links } : {}),
    key: input.key,
    title: input.title,
    status: 'pending',
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
    ruleNote: '生产报工与产出批次按工单记录，可从上方工单继续查看；本时间线尚未直接汇总。',
  },
  'quality-result': {
    title: '质量结果 / NCR / 冻结',
    ruleNote: '质量检验任务按工单生成，可从上方工单继续查看；本时间线尚未直接汇总。',
  },
  'finished-goods-receipt': {
    title: '完工入库',
    ruleNote: '完工入库按工单办理，可从上方工单继续查看；本时间线尚未直接汇总。',
  },
  'finished-goods-inventory': {
    title: '成品批次与库存',
    ruleNote: '成品库存由完工入库带入，需先完成完工入库才能回溯到本单。',
  },
  'wms-outbound': {
    title: 'WMS 出库',
    ruleNote: '按发货单号在仓储作业范围内核对。',
  },
  voucher: {
    title: '凭证',
    ruleNote: '会计凭证按科目汇总过账，无法对应到某一张销售订单。',
  },
}

/**
 * 排程紧急度徽标的中文文案。
 *
 * 后端 `level` 是英文枚举（`highrisk` / `urgent` / …）。履约抽屉曾把原值直接摆上去，
 * 演示里就是一枚英文徽标（#1418，owner 亲验截图时点名）。这里复用紧急度徽标同一张
 * 中文映射，两处口径不会各说各话。
 */
export function describeUrgencyLevel(level?: string | null): string | undefined {
  return level ? urgencyLevelPresentation(level).label : undefined
}

function unlinkedNode(key: FulfillmentNodeKey): FulfillmentNode {
  const spec = UNLINKED_NODES[key]
  return {
    key,
    title: spec?.title ?? key,
    status: 'unlinked',
    ruleNote: spec?.ruleNote,
  }
}

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

  // —— 2. 生产需求（DemandSource.sourceReference === salesOrderNo）——
  const demandQuery = useQuery(() => ({
    key: ['fulfillment', 'demand', ctx.organizationId, ctx.environmentId, salesOrderNo.value ?? ''],
    query: () => {
      const scope = {
        organizationId: ctx.organizationId,
        environmentId: ctx.environmentId,
        keyword: salesOrderNo.value,
      }
      return readCandidatePages<BusinessConsoleDemandSourceItem>((skip, take) =>
        listBusinessConsolePlanningDemands({
          query: { ...scope, skip, take },
          throwOnError: false,
        }),
      )
    },
    enabled: hasScope.value && canRead(P.planningDemandsRead),
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
    enabled: canRead(P.planningMrpRead) && hasScope.value,
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
    // 扫描窗内**每一次**运行都要看，命中不提前收工：接受建议后再跑 MRP，本单会多出一条
    // 尚未被接受的新建议，而工单挂在旧建议上——只看最新一次运行就会把工单节点误判成空态。
    // 结果按「新运行在前」排列，展示取最新、找工单则沿列表往回找。
    query: async () => {
      const scope = { organizationId: ctx.organizationId, environmentId: ctx.environmentId }
      const source = salesOrderNo.value
      const collected: BusinessConsoleMrpPeggingItem[] = []
      for (const runId of scanRunIds.value) {
        const data = await runNodeSource<{ items?: BusinessConsoleMrpPeggingItem[] } | null>(
          async () => {
            const { data, error, response } = await getBusinessConsolePlanningMrpPegging({
              path: { runId },
              query: scope,
              throwOnError: false,
            })
            return {
              data: data?.success ? (data.data ?? null) : null,
              error: data?.success === false ? data : error,
              response,
            }
          },
        )
        collected.push(...matchDemandPeggings(data?.items, source))
      }
      return collected
    },
    enabled: canRead(P.planningMrpRead) && hasScope.value && scanRunIds.value.length > 0,
  }))

  const matchedPeggings = computed(() => peggingQuery.data.value ?? [])
  const suggestionIds = computed(() => peggingSuggestionIds(matchedPeggings.value))
  // MRP 这一跳由「运行列表 + pegging 扫描」两个请求共同构成，任一失败都属该节点失败域。
  const mrpError = computed(() => mrpRunsQuery.error.value ?? peggingQuery.error.value)
  const mrpLoading = computed(() => mrpRunsQuery.isLoading.value || peggingQuery.isLoading.value)

  // —— 建议本体（拿人读的 SKU/数量与下游工单引用；suggestionId 是 GUID，不上屏）——
  //
  // 服务端过滤只到 `status`：建议读面的契约没有 suggestionId / keyword / runId 参数
  // （见 types.gen.ts 的 ListBusinessConsolePlanningSuggestionsData），
  // 想把「按 id 取一条」下推到服务端目前做不到。这里用 `status: 'Accepted'` 做真实的
  // 服务端收敛——只有被接受的建议才会回写下游工单引用，未接受的拉回来也没用；
  // 待读面支持按 id / 需求源过滤后应换成精确查询。
  const suggestionQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'planning-suggestions',
      'accepted',
      ctx.organizationId,
      ctx.environmentId,
      suggestionIds.value.join('|'),
    ],
    query: () =>
      runNodeSource<{ items?: BusinessConsolePlanningSuggestionItem[] } | null>(async () => {
        const { data, error, response } = await listBusinessConsolePlanningSuggestions({
          query: {
            organizationId: ctx.organizationId,
            environmentId: ctx.environmentId,
            status: 'Accepted',
          },
          throwOnError: false,
        })
        return {
          data: data?.success ? (data.data ?? null) : null,
          error: data?.success === false ? data : error,
          response,
        }
      }),
    enabled: canRead(P.planningMrpRead) && hasScope.value && suggestionIds.value.length > 0,
  }))

  const matchedSuggestion = computed(() =>
    matchPlanningSuggestion(suggestionQuery.data.value?.items, suggestionIds.value),
  )
  const matchedWorkOrder = computed(() =>
    matchSuggestionWorkOrder(suggestionQuery.data.value?.items, suggestionIds.value),
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

  // 按已接受建议携带的工单号读取详情，并核对来源建议后再补状态。
  const workOrderSuggestionId = computed(() => matchedWorkOrder.value?.suggestionId)
  const mesScope = useMesPrincipalWorkScope(
    ctx,
    P.mesWorkOrdersRead,
    () => canRead(P.mesWorkOrdersRead) && Boolean(matchedWorkOrder.value),
  )
  const workOrderDetailQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'work-order-detail',
      mesScope.principalIdentity.value,
      mesScope.selectedScope.value?.kind ?? '',
      mesScope.selectedScope.value?.id ?? '',
      ctx.organizationId,
      ctx.environmentId,
      matchedWorkOrder.value?.workOrderNo ?? '',
    ],
    enabled:
      mesScope.scopeReady.value &&
      canRead(P.mesWorkOrdersRead) &&
      hasBusinessContext(ctx) &&
      Boolean(matchedWorkOrder.value),
    query: () =>
      runNodeSource(async () => {
        const { data, error, response } = await getBusinessConsoleMesWorkOrderDetail({
          path: { workOrderId: matchedWorkOrder.value!.workOrderNo },
          query: {
            organizationId: ctx.organizationId,
            environmentId: ctx.environmentId,
            scopeKind: mesScope.selectedScope.value?.kind,
            scopeId: mesScope.selectedScope.value?.id,
          },
          throwOnError: false,
        })
        return { data: data?.data, error: data?.success === false ? data : error, response }
      }),
  }))
  const matchedPlanRow = computed(() => {
    const detail = workOrderDetailQuery.data.value
    const source = detail?.sourcePlanReference
    if (
      !canRead(P.mesWorkOrdersRead) ||
      !mesScope.scopeReady.value ||
      !detail ||
      detail.workOrderId !== matchedWorkOrder.value?.workOrderNo ||
      !source ||
      source.sourceDocumentId !== workOrderSuggestionId.value
    )
      return undefined
    return {
      status: detail.status,
      sourceDocumentId: source.sourceDocumentId,
      sourceDemandReference: source.sourceDemandReference,
    }
  })

  // 建议确认工单号，详情只补当前状态；详情失败仍保留已知工单号。
  const workOrderError = computed(() => mrpError.value ?? suggestionQuery.error.value)
  const workOrderLoading = computed(
    () =>
      mrpLoading.value || suggestionQuery.isLoading.value || workOrderDetailQuery.isLoading.value,
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
      .filter((value): value is string => Boolean(value))
      .map((source) => ({
        label: '按来源核对应收 · ' + source,
        to: { path: '/erp/finance/ar-ap', query: { keyword: source } },
      }))
    const receiptFailure = receipts.find((result) => result.error)?.error
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
          error: !canRead(P.erpFinanceRead)
            ? new FulfillmentNodeError(403)
            : (receivableQuery.error.value ??
              (!receivable ? (deliveryQuery.error.value ?? receiptFailure) : null)),
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
        retryable: Boolean(
          receivable &&
          receipts.some((result) => result.error && httpStatusOf(result.error) !== 403),
        ),
        links:
          canRead(P.erpFinanceRead) && httpStatusOf(receivableQuery.error.value) !== 403
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
