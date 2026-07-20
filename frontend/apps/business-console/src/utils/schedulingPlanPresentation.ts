const statusLabels = {
  preview: '预览',
  generated: '已生成',
  released: '已发布',
  superseded: '已取代',
  revoked: '已撤销',
} as const

export function schedulingPlanStatusLabel(status?: string | null) {
  if (status === null || status === undefined) return '未知'
  return statusLabels[status as keyof typeof statusLabels] ?? status
}

export function schedulingPlanStatusTone(
  status?: string | null,
): 'success' | 'warning' | 'neutral' {
  if (status === 'released') return 'success'
  if (status === 'generated') return 'warning'
  return 'neutral'
}

export function schedulingPlanTerminalReleaseReason(status?: string | null) {
  if (status === 'released') return '方案已发布'
  if (status === 'superseded') return '方案已被后续方案取代'
  if (status === 'revoked') return '方案已撤销'
  return undefined
}
