import { BusinessOperationUnconfirmedError } from '@nerv-iip/api-client'

const CANONICAL_GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

function normalizeCanonicalGuid(value: unknown) {
  if (typeof value !== 'string') return undefined
  const normalized = value.trim().toLowerCase()
  return CANONICAL_GUID.test(normalized) && normalized !== EMPTY_GUID ? normalized : undefined
}

export function confirmedMaintenanceCreateWorkOrderId(envelope: unknown) {
  if (!envelope || typeof envelope !== 'object' || Array.isArray(envelope)) {
    throw new BusinessOperationUnconfirmedError('维修工单创建未返回可核验的业务回执。')
  }
  const data = (envelope as { data?: unknown }).data
  if (!data || typeof data !== 'object' || Array.isArray(data)) {
    throw new BusinessOperationUnconfirmedError('维修工单创建未返回可核验的业务回执。')
  }
  const receipt = (data as { operationReceipt?: unknown }).operationReceipt
  if (!receipt || typeof receipt !== 'object' || Array.isArray(receipt)) {
    throw new BusinessOperationUnconfirmedError('维修工单创建缺少权威操作回执。')
  }
  const workOrderId = normalizeCanonicalGuid((data as { workOrderId?: unknown }).workOrderId)
  const resourceId = normalizeCanonicalGuid((receipt as { resourceId?: unknown }).resourceId)
  if (!workOrderId || !resourceId || workOrderId !== resourceId) {
    throw new BusinessOperationUnconfirmedError('维修工单创建回执的强标识不一致或不合法。')
  }
  return workOrderId
}
