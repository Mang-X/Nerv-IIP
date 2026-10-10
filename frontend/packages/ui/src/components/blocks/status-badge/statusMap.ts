/** Semantic tone for a status — drives the StatusBadge colour via design tokens. */
export type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

/**
 * 把后端各种写法的状态码收敛成同一个查表键。
 *
 * 后端跨服务并不统一：MES/质量侧多是 kebab-case（`partially-shipped`），
 * ERP 采购/财务侧是 PascalCase（`PartiallyReceived`），还有 camelCase 与全小写。
 * 统一「转小写 + 去掉连字符/下划线/空白」之后，上面几种写法都落到同一个键，
 * 词表只需要维护一份，不用为每种拼法各写一条。
 */
export function normalizeStatusKey(value: string): string {
  return value.toLowerCase().replace(/[-_\s]/g, '')
}

/**
 * Localized (zh-Hans) label for a known status key.
 *
 * 键一律用 `normalizeStatusKey` 之后的形态（全小写、无连字符）。
 * 新增状态务必同步补这里，避免已知状态退化为中性占位。
 */
const STATUS_LABELS: Record<string, string> = {
  accepted: '已受理',
  active: '启用',
  approved: '已批准',
  available: '可用',
  // 采购订单 / 收货单的「收货状态」由网关按行项目收货量派生，四个取值见
  // BusinessServiceClients.ReceiptReadiness：no-lines / received / partially-received / awaiting-arrival。
  awaitingarrival: '待到货',
  blocked: '阻塞',
  cancelled: '已取消',
  closed: '已关闭',
  completed: '已完成',
  conditionalrelease: '条件放行',
  confirmed: '已确认',
  created: '已创建',
  creditheld: '信用冻结',
  degraded: '降级运行',
  disabled: '停用',
  dismissed: '已忽略',
  dispositioninprogress: '处置中',
  dispatched: '已派发',
  draft: '待审',
  effectivenessverified: '效果已验证',
  expired: '已过期',
  failed: '失败',
  held: '暂停',
  hold: '冻结',
  inprogress: '执行中',
  inventorypostingfailed: '库存过账失败',
  issued: '已下发',
  manual: '手工处理',
  nolines: '无行项目',
  open: '待处理',
  partiallyposted: '部分过账',
  partiallyreceived: '部分收货',
  partiallyshipped: '部分发货',
  passed: '通过',
  paused: '暂停',
  pending: '待处理',
  // 采购订单 / 采购订单变更的「待审批」（ERP PurchaseOrderStatus.PendingApproval）。
  pendingapproval: '待审批',
  pendingconfirmation: '待确认',
  planned: '已计划',
  posted: '已过账',
  published: '已发布',
  // 库存质量状态的四个规范值见后端 StockQualityStatus.cs：
  // unrestricted / quality / restricted / blocked（写入时 Normalize，读面必是这四个之一）。
  // `quality` 的别名是 inspection / quality-inspection，语义是「已收但未放行、等质检结论」，
  // 工厂里叫「待检库存」，所以用「待检」而不是「质检中」。
  quality: '待检',
  restricted: '受限使用',
  queued: '排队中',
  ready: '可开工',
  received: '已收货',
  rejected: '已拒绝',
  released: '已下达',
  requested: '已申请',
  returnrequested: '已申请退货',
  reworkpending: '待返工',
  running: '执行中',
  scheduled: '已排程',
  scheduleinvalidated: '排程已失效',
  scrapaccepted: '报废已受理',
  scrapped: '已报废',
  settled: '已结清',
  started: '已开工',
  submitted: '已提交',
  superseded: '已被替代',
  unavailable: '不可用',
  unrestricted: '非限制使用',
  warning: '预警',
  registered: '已登记',
  matched: '已匹配',
  executed: '已执行',
  recorded: '已记录',
  converted: '已转换',
  authorized: '已授权',
  warehousereceived: '仓库已收货',
  creditapproved: '贷项已批准',
  creditissued: '贷项已开具',
  creditdenied: '贷项已拒绝',
  paymentheld: '付款已冻结',
  voided: '已作废',
  applied: '已应用',
  waitingforparts: '待备件',
  verified: '已验证',
  returned: '已退回',
  skipped: '已跳过',
  withdrawn: '已撤回',
  revoked: '已吊销',
  split: '已拆分',
  merged: '已合并',
}

const TONE_BY_STATUS: Record<StatusTone, string[]> = {
  success: [
    'matched',
    'executed',
    'authorized',
    'creditapproved',
    'creditissued',
    'applied',
    'verified',
    'accepted',
    'active',
    'approved',
    'available',
    'closed',
    'completed',
    'confirmed',
    'effectivenessverified',
    'passed',
    'posted',
    'published',
    'ready',
    'received',
    'settled',
    'unrestricted',
  ],
  info: [
    'registered',
    'recorded',
    'converted',
    'warehousereceived',
    'dispatched',
    'dispositioninprogress',
    'inprogress',
    'issued',
    'manual',
    'partiallyreceived',
    'partiallyshipped',
    'released',
    'running',
    'scheduled',
    'started',
  ],
  danger: [
    'creditdenied',
    'voided',
    'revoked',
    'blocked',
    'cancelled',
    'creditheld',
    'disabled',
    'expired',
    'failed',
    'inventorypostingfailed',
    'rejected',
    'scrapaccepted',
    'scrapped',
    'unavailable',
  ],
  warning: [
    'paymentheld',
    'waitingforparts',
    'returned',
    'awaitingarrival',
    'conditionalrelease',
    'created',
    'degraded',
    'draft',
    'held',
    'hold',
    'open',
    'partiallyposted',
    'paused',
    'pending',
    'pendingapproval',
    'pendingconfirmation',
    'planned',
    'quality',
    'queued',
    'restricted',
    'requested',
    'returnrequested',
    'reworkpending',
    'scheduleinvalidated',
    'submitted',
  ],
  neutral: ['dismissed', 'nolines', 'superseded', 'skipped', 'withdrawn', 'split', 'merged'],
}

const STATUS_TO_TONE = new Map<string, StatusTone>()
for (const tone of Object.keys(TONE_BY_STATUS) as StatusTone[]) {
  for (const key of TONE_BY_STATUS[tone]) STATUS_TO_TONE.set(key, tone)
}

export interface ResolvedStatus {
  label: string
  tone: StatusTone
}

/**
 * 词表漏词只在开发期告警一次，生产构建里整段被摇树掉。
 *
 * 未知值显示中性「—」，但开发期仍必须有声音——
 * 否则新状态码只会在真机走查时才被人眼发现。同一个 key 只报一次，避免表格逐行刷屏。
 */
const warnedStatusKeys = new Set<string>()
function warnMissingStatusLabel(key: string, raw: string) {
  if (!import.meta.env?.DEV) return
  if (warnedStatusKeys.has(key)) return
  warnedStatusKeys.add(key)
  console.warn(
    `[NvStatusBadge] 词表缺失: ${raw}（归一键 ${key}），请补 statusMap.ts 的 STATUS_LABELS`,
  )
}

/**
 * Resolve a raw status value to a localized label + semantic tone.
 *
 * `warnOnMissing` 存在的理由：漏词告警要发现的是「已知状态缺少可读标签」。调用方自己传了
 * `label` 时，屏上是它的词、不是这里的占位值，漏词**没有可见后果**——照报只会
 * 把频道刷满假警报（实测：审批决策记录传了 `:label` 还在报 `approve` 缺词），
 * 真正的漏词反而被淹掉。所以由调用方声明「这次的词表结果是否会上屏」。
 */
export function resolveStatus(
  value?: string | null,
  options?: { warnOnMissing?: boolean },
): ResolvedStatus {
  const raw = (value ?? '').trim()
  const key = normalizeStatusKey(raw)
  const label = STATUS_LABELS[key]
  if (label === undefined && raw && options?.warnOnMissing !== false)
    warnMissingStatusLabel(key, raw)
  return {
    label: label ?? '—',
    tone: STATUS_TO_TONE.get(key) ?? 'neutral',
  }
}

/** 词表里已登记的状态键（供契约测试核对覆盖面）。 */
export const KNOWN_STATUS_KEYS = Object.keys(STATUS_LABELS)
