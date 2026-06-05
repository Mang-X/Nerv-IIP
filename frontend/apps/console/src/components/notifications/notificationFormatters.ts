import type { NotificationMessageResponse, NotificationResourceRef } from '@nerv-iip/api-client'
import type { StatusTone } from '@nerv-iip/ui'

// 通知严重级 / 状态值是后端原始字符串；这里映射到 StatusBadge 的 tone，
// 与旧 Badge variant（destructive/secondary/outline）保持等价语义。
export function notificationTone(value?: string | null): StatusTone {
  const normalized = value?.toLowerCase()

  if (
    normalized === 'critical' ||
    normalized === 'error' ||
    normalized === 'failed' ||
    normalized === 'failure' ||
    normalized === 'warning'
  ) {
    return 'danger'
  }

  if (normalized === 'open' || normalized === 'unread') {
    return 'warning'
  }

  return 'neutral'
}

const SEVERITY_LABELS: Record<string, string> = {
  critical: '严重',
  error: '错误',
  failed: '失败',
  failure: '失败',
  warning: '告警',
  warn: '告警',
  info: '信息',
  normal: '普通',
}

const STATUS_LABELS: Record<string, string> = {
  unread: '未读',
  read: '已读',
  open: '待处理',
  closed: '已关闭',
  acknowledged: '已确认',
  failed: '失败',
  unknown: '未知',
}

export function notificationSeverityLabel(value?: string | null) {
  if (!value) return '普通'
  return SEVERITY_LABELS[value.toLowerCase()] ?? value
}

export function notificationStatusLabel(value?: string | null) {
  if (!value) return '未知'
  return STATUS_LABELS[value.toLowerCase()] ?? value
}

export function messageTitle(message: NotificationMessageResponse) {
  return message.title ?? message.summary ?? message.messageId ?? '未命名通知'
}

export function formatNotificationDate(value?: string | null) {
  if (!value) {
    return '未上报'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

export function formatResource(resource?: NotificationResourceRef | null) {
  if (!resource) {
    return '无关联资源'
  }

  const type = resource.resourceType ?? 'resource'
  const id = resource.resourceId ?? resource.fileId

  return id ? `${type}: ${id}` : type
}
