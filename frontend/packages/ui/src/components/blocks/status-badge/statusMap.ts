/** Semantic tone for a status — drives the StatusBadge colour via design tokens. */
export type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

/** Localized (zh-Hans) label for a known status key. */
const STATUS_LABELS: Record<string, string> = {
  active: '启用',
  approved: '已批准',
  available: '可用',
  blocked: '阻塞',
  cancelled: '已取消',
  closed: '已关闭',
  completed: '已完成',
  'conditional-release': '条件放行',
  conditionalrelease: '条件放行',
  created: '已创建',
  disabled: '停用',
  failed: '失败',
  held: '暂停',
  inprogress: '执行中',
  'in-progress': '执行中',
  issued: '已下发',
  manual: '手工处理',
  open: '待处理',
  passed: '通过',
  paused: '暂停',
  pending: '待处理',
  planned: '已计划',
  queued: '排队中',
  ready: '可开工',
  rejected: '已拒绝',
  released: '已下达',
  running: '执行中',
  scheduled: '已排程',
  started: '已开工',
  submitted: '已提交',
  unavailable: '不可用',
  warning: '预警',
}

const TONE_BY_STATUS: Record<StatusTone, string[]> = {
  success: ['ready', 'completed', 'closed', 'passed', 'available', 'active', 'approved'],
  info: ['running', 'inprogress', 'in-progress', 'started', 'manual', 'released', 'issued', 'scheduled'],
  danger: ['blocked', 'failed', 'rejected', 'unavailable', 'cancelled', 'disabled'],
  warning: [
    'pending',
    'warning',
    'conditional-release',
    'conditionalrelease',
    'held',
    'paused',
    'open',
    'created',
    'planned',
    'queued',
    'submitted',
  ],
  neutral: [],
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
  const key = raw.toLowerCase()
  return {
    label: STATUS_LABELS[key] ?? (raw || '未知'),
    tone: STATUS_TO_TONE.get(key) ?? 'neutral',
  }
}
