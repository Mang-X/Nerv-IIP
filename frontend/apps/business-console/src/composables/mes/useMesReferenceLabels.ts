const statusLabels: Record<string, string> = {
  Accepted: '已受理',
  Active: '生效中',
  Blocked: '阻塞',
  Completed: '已完成',
  InProgress: '执行中',
  Open: '未恢复',
  Paused: '暂停',
  Queued: '待开工',
  Ready: '就绪',
  Recovered: '已恢复',
  Released: '已释放',
  Requested: '已请求',
  Closed: '已关闭',
  Warning: '预警',
}

const normalizedStatusLabels = Object.fromEntries(
  Object.entries(statusLabels).flatMap(([key, label]) => [
    [key, label],
    [key.toLowerCase(), label],
  ]),
)

export const mesStatusOptions = [
  { value: 'all', label: '全部状态' },
  { value: 'accepted', label: '已受理' },
  { value: 'active', label: '生效中' },
  { value: 'ready', label: '就绪' },
  { value: 'queued', label: '待开工' },
  { value: 'released', label: '已释放' },
  { value: 'inProgress', label: '执行中' },
  { value: 'paused', label: '暂停' },
  { value: 'blocked', label: '阻塞' },
  { value: 'open', label: '未恢复' },
  { value: 'recovered', label: '已恢复' },
  { value: 'requested', label: '已请求' },
  { value: 'completed', label: '已完成' },
  { value: 'closed', label: '已关闭' },
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
