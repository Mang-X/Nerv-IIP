import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { getBusinessConsoleWorkbenchSummaryQueryOptions } from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessWorkbenchSummary } from './useBusinessWorkbench'

const coladaState = vi.hoisted(() => ({
  queryFactory: undefined as undefined | (() => unknown),
  queryData: undefined as unknown,
}))

vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsoleWorkbenchSummaryQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleWorkbenchSummary' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    coladaState.queryFactory = optionsFactory
    optionsFactory()

    return {
      data: shallowRef(coladaState.queryData),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))

describe('business workbench summary composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryFactory = undefined
    coladaState.queryData = undefined
  })

  it('loads the workbench summary through the stable api-client export', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-workbench', environmentId: 'env-workbench' })
    coladaState.queryData = {
      success: true,
      data: {
        kpis: [{ key: 'releasedWorkOrders', label: 'Released work orders', value: 4, source: 'BusinessMES', status: 'available' }],
        todos: { status: 'available', total: 2, items: [{ source: 'BusinessApproval', itemType: 'purchase-order', status: 'pending' }] },
        messages: { status: 'available', total: 3, unread: 1, items: [{ messageId: 'msg-1', status: 'unread', severity: 'warning' }] },
        alerts: { status: 'available', total: 1, critical: 1, items: [{ alarmEventId: 'alarm-1', deviceAssetId: 'DEV-01', alarmCode: 'TEMP_HIGH', severity: 'critical' }] },
        sourceStatuses: [
          { source: 'BusinessMES', status: 'available' },
          { source: 'BusinessInventory', status: 'unsupported', permissionCode: 'business.inventory.ledger.read' },
        ],
      },
    }

    const workbench = useBusinessWorkbenchSummary()

    expect(getBusinessConsoleWorkbenchSummaryQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-workbench',
        environmentId: 'env-workbench',
        take: 8,
      },
    })
    expect(workbench.summary.value?.kpis).toHaveLength(1)
    expect(workbench.availableKpis.value.map((kpi) => kpi.value)).toEqual([4])
    expect(workbench.todoItems.value).toHaveLength(1)
    expect(workbench.messageItems.value).toHaveLength(1)
    expect(workbench.alertItems.value).toHaveLength(1)
    expect(workbench.sourceStatuses.value.map((status) => status.status)).toEqual(['available', 'unsupported'])
  })

  it('rebuilds the query with the latest business context values', () => {
    const context = useBusinessContextStore()
    const workbench = useBusinessWorkbenchSummary()

    context.patchContext({ organizationId: 'org-updated', environmentId: 'env-updated' })
    coladaState.queryFactory?.()

    expect(workbench.summary.value).toBeUndefined()
    expect(getBusinessConsoleWorkbenchSummaryQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-updated',
        environmentId: 'env-updated',
        take: 8,
      },
    })
  })
})
