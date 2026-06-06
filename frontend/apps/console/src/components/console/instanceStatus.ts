import type { StatusTone } from '@nerv-iip/ui'

// 实例上报状态 / 健康值是后端原始字符串；映射到 StatusBadge 的 tone，
// 与旧 Badge variant（destructive/success/secondary）保持等价语义。
export function instanceTone(status?: string | null): StatusTone {
  const s = status?.toLowerCase()
  if (s === 'failed' || s === 'unhealthy' || s === 'stopped' || s === 'cancelled' || s === 'canceled') {
    return 'danger'
  }
  if (s === 'running' || s === 'healthy') {
    return 'success'
  }
  return 'neutral'
}

const STATUS_LABELS: Record<string, string> = {
  running: '运行中',
  healthy: '健康',
  unhealthy: '不健康',
  stopped: '已停止',
  failed: '失败',
  cancelled: '已取消',
  canceled: '已取消',
  starting: '启动中',
  pending: '待处理',
  degraded: '降级',
  unknown: '未知',
}

export function instanceStatusLabel(status?: string | null) {
  if (!status) return '未知'
  return STATUS_LABELS[status.toLowerCase()] ?? status
}
