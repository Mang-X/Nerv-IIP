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
 * **漏一条的代价是界面上直接印出后端英文状态码**，所以新增状态务必同步补这里。
 */
const STATUS_LABELS: Record<string, string> = {
  accepted: '已受理',
  active: '启用',
  approved: '已批准',
  available: '可用',
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
  open: '待处理',
  partiallyposted: '部分过账',
  partiallyreceived: '部分收货',
  partiallyshipped: '部分发货',
  passed: '通过',
  paused: '暂停',
  pending: '待处理',
  pendingconfirmation: '待确认',
  planned: '已计划',
  posted: '已过账',
  published: '已发布',
  // 库存质量状态的四个规范值见后端 StockQualityStatus.cs：
  // unrestricted / quality / restricted / blocked（写入时 Normalize，读面必是这四个之一）。
  // `quality` 的别名是 inspection / quality-inspection，语义是「已收但未放行、等质检结论」，
  // 工厂里叫「待检库存」，所以用「待检」而不是「质检中」。
  quality: '待检',
  // 注意：`quarantine` 不在库存那四个规范值里，它来自质量域 —— Quality 的
  // StockReleaseDimension 用它表示「进了质量冻结库位、等判定」（WorldHistorySeedService）。
  // 所以留着词条，但别理解成库存质量状态。
  quarantine: '隔离',
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
}

const TONE_BY_STATUS: Record<StatusTone, string[]> = {
  success: [
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
    'pendingconfirmation',
    'planned',
    'quality',
    'quarantine',
    'queued',
    'restricted',
    'requested',
    'returnrequested',
    'reworkpending',
    'scheduleinvalidated',
    'submitted',
  ],
  neutral: ['dismissed', 'superseded'],
}

const STATUS_TO_TONE = new Map<string, StatusTone>()
for (const tone of Object.keys(TONE_BY_STATUS) as StatusTone[]) {
  for (const key of TONE_BY_STATUS[tone]) STATUS_TO_TONE.set(key, tone)
}

export interface ResolvedStatus {
  label: string
  tone: StatusTone
}

/** Resolve a raw status value to a localized label + semantic tone. */
export function resolveStatus(value?: string | null): ResolvedStatus {
  const raw = (value ?? '').trim()
  const key = normalizeStatusKey(raw)
  return {
    label: STATUS_LABELS[key] ?? (raw || '未知'),
    tone: STATUS_TO_TONE.get(key) ?? 'neutral',
  }
}

/** 词表里已登记的状态键（供契约测试核对覆盖面）。 */
export const KNOWN_STATUS_KEYS = Object.keys(STATUS_LABELS)
