import type { BusinessConsoleNotificationMessageItem } from '@nerv-iip/api-client'

export function messageTitle(message: BusinessConsoleNotificationMessageItem) {
  return message.title ?? message.summary ?? '通知'
}

const resourceLabels: Record<string, string> = {
  'schedule-plan': '排产计划',
  'demand-change-request': '需求变更',
  'approval-chain': '审批流程',
  'operation-task': '操作任务',
  'wcs-task': '仓储任务',
  'measuring-device': '计量设备',
  'connector-host': '连接节点',
  'notification-dead-letter-backlog': '通知积压',
  sku: '物料',
}

export function resourceTypeLabel(value: string) {
  return resourceLabels[value] ?? value
}
