import type { RouteLocationRaw } from 'vue-router'
import { urgencyLevelPresentation } from '../urgency/useUrgencyDisplayMode'
import { classifyFulfillmentFailure, type FulfillmentFailureKind } from './fulfillmentErrors'

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

export function unlinkedNode(key: FulfillmentNodeKey): FulfillmentNode {
  const spec = UNLINKED_NODES[key]
  return {
    key,
    title: spec?.title ?? key,
    status: 'unlinked',
    ruleNote: spec?.ruleNote,
  }
}
