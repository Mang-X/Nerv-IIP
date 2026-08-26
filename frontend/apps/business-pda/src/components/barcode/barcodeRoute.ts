import type { BusinessConsoleBarcodeResolveCandidate } from '@nerv-iip/api-client'
import type { RouteLocationRaw } from 'vue-router'

export function barcodeCandidateRoute(
  candidate: BusinessConsoleBarcodeResolveCandidate,
): RouteLocationRaw | null {
  const strongId = (name: string) => {
    const value = candidate.strongIds?.[name]?.trim()
    return value || null
  }

  if (candidate.objectType === 'mes-work-order') {
    const workOrderId = strongId('workOrderId')
    return workOrderId ? { path: '/mes/report', query: { workOrderId } } : null
  }

  if (candidate.objectType === 'mes-operation') {
    const workOrderId = strongId('workOrderId')
    const operationTaskId = strongId('operationTaskId')
    return workOrderId && operationTaskId
      ? { path: '/mes/operation', query: { workOrderId, operationTaskId } }
      : null
  }

  return null
}
