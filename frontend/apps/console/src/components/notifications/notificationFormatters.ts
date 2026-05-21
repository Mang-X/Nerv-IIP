import type { NotificationMessageResponse, NotificationResourceRef } from '@nerv-iip/api-client'

export function notificationBadgeVariant(value?: string | null) {
  const normalized = value?.toLowerCase()

  return normalized === 'critical' ||
    normalized === 'error' ||
    normalized === 'failed' ||
    normalized === 'warning'
    ? 'destructive'
    : normalized === 'open' || normalized === 'unread'
      ? 'secondary'
      : 'outline'
}

export function messageTitle(message: NotificationMessageResponse) {
  return message.title ?? message.summary ?? message.messageId ?? 'Untitled notification'
}

export function formatNotificationDate(value?: string | null) {
  if (!value) {
    return 'Not reported'
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
    return 'No resource'
  }

  const type = resource.resourceType ?? 'resource'
  const id = resource.resourceId ?? resource.fileId

  return id ? `${type}: ${id}` : type
}
