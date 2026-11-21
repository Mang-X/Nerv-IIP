import { BusinessOperationUnconfirmedError } from '@nerv-iip/api-client'
import { normalizeCanonicalGuid } from './maintenancePublicIds'

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
