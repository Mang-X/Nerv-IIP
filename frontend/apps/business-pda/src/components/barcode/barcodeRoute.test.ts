import type { BusinessConsoleBarcodeResolveCandidate } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'

import { barcodeCandidateRoute } from './barcodeRoute'

function candidate(
  objectType: string,
  strongIds: Record<string, string>,
): BusinessConsoleBarcodeResolveCandidate {
  return { objectType, strongIds }
}

describe('barcodeCandidateRoute', () => {
  it('maps a work order strong ID to the server-revalidating report page', () => {
    expect(barcodeCandidateRoute(candidate('mes-work-order', { workOrderId: ' WO-1 ' }))).toEqual({
      path: '/mes/report',
      query: { workOrderId: 'WO-1' },
    })
  })

  it('requires both strong IDs before routing an MES operation', () => {
    expect(
      barcodeCandidateRoute(
        candidate('mes-operation', { workOrderId: 'WO-1', operationTaskId: 'OP-1' }),
      ),
    ).toEqual({
      path: '/mes/operation',
      query: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
    })
    expect(
      barcodeCandidateRoute(candidate('mes-operation', { operationTaskId: 'OP-1' })),
    ).toBeNull()
    expect(barcodeCandidateRoute(candidate('mes-operation', { workOrderId: 'WO-1' }))).toBeNull()
  })

  it('does not route targets that cannot revalidate the strong ID or blank IDs', () => {
    expect(
      barcodeCandidateRoute(candidate('equipment-device', { deviceAssetId: 'DEV-1' })),
    ).toBeNull()
    expect(
      barcodeCandidateRoute(
        candidate('mes-material-issue-request', { materialIssueRequestId: 'MI-1' }),
      ),
    ).toBeNull()
    expect(barcodeCandidateRoute(candidate('mes-work-order', { workOrderId: '  ' }))).toBeNull()
  })
})
