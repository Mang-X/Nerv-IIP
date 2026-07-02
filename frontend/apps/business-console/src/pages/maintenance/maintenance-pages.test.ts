import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import WorkOrdersPage from './work-orders.vue'

const routeState = vi.hoisted(() => ({
  query: {
    deviceAssetId: 'DEV-PRESS-01',
    sourceAlarmId: 'ALARM-9001',
  } as Record<string, string>,
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')

  return {
    ...actual,
    useRoute: () => reactive({ query: routeState.query }),
  }
})

vi.mock('@/composables/useBusinessMaintenance', () => ({
  useMaintenanceWorkOrders: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    workOrders: computed(() => []),
    workOrdersError: shallowRef(),
    workOrdersPending: shallowRef(false),
    workOrdersTotal: computed(() => 0),
    refreshWorkOrders: vi.fn(),
    createWorkOrder: vi.fn(),
    createWorkOrderPending: shallowRef(false),
    createWorkOrderError: shallowRef(),
    completeWorkOrder: vi.fn(),
    completeWorkOrderPending: shallowRef(false),
    completeWorkOrderError: shallowRef(),
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
}

describe('maintenance pages', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
    routeState.query = {
      deviceAssetId: 'DEV-PRESS-01',
      sourceAlarmId: 'ALARM-9001',
    }
  })

  it('prefills maintenance work order creation from equipment alarm context', async () => {
    mount(WorkOrdersPage, {
      attachTo: document.body,
      global: { stubs },
    })
    await flushPromises()

    expect(document.body.textContent).toContain('新建维护工单')
    expect(document.body.querySelector<HTMLInputElement>('#mwo-device')?.value).toBe('DEV-PRESS-01')
    expect(document.body.querySelector<HTMLInputElement>('#mwo-alarm')?.value).toBe('ALARM-9001')
  })
})
