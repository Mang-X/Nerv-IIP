const statusLabels: Record<string, string> = {
  Accepted: '已受理',
  Active: '生效中',
  Blocked: '阻塞',
  Cancelled: '已取消',
  Completed: '已完成',
  Created: '已创建',
  DispositionAccepted: '处置已受理',
  Hold: '挂起',
  InProgress: '执行中',
  Open: '未恢复',
  PartiallyReceived: '部分接收',
  Paused: '暂停',
  Posted: '已入库',
  Queued: '待开工',
  Ready: '就绪',
  Received: '已接收',
  Recovered: '已恢复',
  Released: '已释放',
  ReturnAccepted: '退回已受理',
  ReworkPending: '返工待处理',
  ScrapAccepted: '报废已受理',
  Scrapped: '已报废',
  Requested: '已请求',
  Closed: '已关闭',
  Started: '已开工',
  Warning: '预警',
}

const normalizedStatusLabels = Object.fromEntries(
  Object.entries(statusLabels).flatMap(([key, label]) => [
    [key, label],
    [key.charAt(0).toLowerCase() + key.slice(1), label],
    [key.toLowerCase(), label],
  ]),
)

export const mesStatusOptions = [
  { value: 'all', label: '全部状态' },
  { value: 'accepted', label: '已受理' },
  { value: 'active', label: '生效中' },
  { value: 'created', label: '已创建' },
  { value: 'ready', label: '就绪' },
  { value: 'queued', label: '待开工' },
  { value: 'released', label: '已释放' },
  { value: 'started', label: '已开工' },
  { value: 'inProgress', label: '执行中' },
  { value: 'paused', label: '暂停' },
  { value: 'hold', label: '挂起' },
  { value: 'blocked', label: '阻塞' },
  { value: 'open', label: '未恢复' },
  { value: 'recovered', label: '已恢复' },
  { value: 'requested', label: '已请求' },
  { value: 'partiallyReceived', label: '部分接收' },
  { value: 'received', label: '已接收' },
  { value: 'posted', label: '已入库' },
  { value: 'reworkPending', label: '返工待处理' },
  { value: 'scrapAccepted', label: '报废已受理' },
  { value: 'returnAccepted', label: '退回已受理' },
  { value: 'dispositionAccepted', label: '处置已受理' },
  { value: 'completed', label: '已完成' },
  { value: 'closed', label: '已关闭' },
  { value: 'cancelled', label: '已取消' },
  { value: 'scrapped', label: '已报废' },
  { value: 'warning', label: '预警' },
]

export function useMesReferenceLabels() {
  function statusLabel(value?: string | null) {
    if (!value) return '未知'
    return normalizedStatusLabels[value] ?? normalizedStatusLabels[value.toLowerCase()] ?? value
  }

  function emptyText(value?: string | null) {
    return value && value.trim().length > 0 ? value : '未指定'
  }

  return {
    emptyText,
    statusLabel,
  }
}
