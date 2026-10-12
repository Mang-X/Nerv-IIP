import type { IntegrationEventDeadLetterDetailResponse } from '@nerv-iip/api-client'
import type { LocationQuery } from 'vue-router'

export function queryText(query: LocationQuery, key: string) {
  const value = query[key]
  return (Array.isArray(value) ? value[0] : value) ?? ''
}

export function recoveryUrl(path: string, query: Record<string, string>) {
  const values = Object.entries(query).filter(([, value]) => value)
  return `${path}?${new URLSearchParams(values)}`
}

// 死信保留原序列化载荷（普通事件为 PascalCase，版本化 converter 为 camelCase）。
// 这是取证 JSON 边界：无法恢复对象时展示原因，不以当前页对象替代原事件对象。
export function readCostRecoveryContext(detail: IntegrationEventDeadLetterDetailResponse) {
  if (!detail.eventJson) return undefined
  let envelope: unknown
  try {
    envelope = JSON.parse(detail.eventJson)
  } catch {
    return undefined
  }
  if (!isRecord(envelope)) return undefined
  const payload = envelope.Payload ?? envelope.payload
  if (!isRecord(payload)) return undefined
  const text = (key: string) => {
    const value = payload[key] ?? payload[key[0]!.toLowerCase() + key.slice(1)]
    return typeof value === 'string' ? value : ''
  }
  const workOrderId = text('WorkOrderId')
  if (!workOrderId) return undefined
  return {
    workOrderId,
    operationTaskId: text('OperationTaskId'),
    workCenterId: text('WorkCenterId'),
    atUtc: text('CompletedAtUtc') || text('ReportedAtUtc'),
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
