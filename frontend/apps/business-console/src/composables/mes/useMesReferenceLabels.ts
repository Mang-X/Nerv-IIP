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
  Warning: '预警',
}

export function useMesReferenceLabels() {
  function statusLabel(value?: string | null) {
    if (!value) return '未知'
    return statusLabels[value] ?? value
  }

  function emptyText(value?: string | null) {
    return value && value.trim().length > 0 ? value : '未指定'
  }

  return {
    emptyText,
    statusLabel,
  }
}
