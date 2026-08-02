import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h, nextTick, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import { useMaintenanceSelfWorkOrders } from './useMaintenanceSelfWorkOrders'

const sdk = vi.hoisted(() => ({
  list: vi.fn(),
  workers: vi.fn(),
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  return {
    ...actual,
    listBusinessConsoleMaintenanceWorkOrders: sdk.list,
    listBusinessConsoleWorkers: sdk.workers,
  }
})

const WORK_ORDER_ID = '019f0000-0000-7000-8000-000000000401'

function workerResponse() {
  return {
    data: {
      success: true,
      data: {
        pageIndex: 1,
        pageSize: 1,
        totalCount: 1,
        items: [
          {
            userId: 'principal-1',
            employeeNo: 'E-001',
            displayName: '张维修',
            employmentStatus: 'active',
            active: true,
            teams: [],
            skills: [],
            snapshotVersion: 'worker-v1',
          },
        ],
      },
    },
  }
}

function listResponse() {
  return {
    data: {
      success: true,
      data: {
        items: [
          {
            workOrderId: WORK_ORDER_ID,
            deviceAssetId: 'DEV-CNC-01',
            priority: 'high',
            status: 'accepted',
            openedAtUtc: '2026-08-02T01:00:00.000Z',
            version: 7,
            assignedTechnicianUserId: 'principal-1',
          },
        ],
        total: 1,
        skip: 0,
        take: 20,
      },
    },
  }
}

describe('maintenance real Pinia Colada component-scope identity', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    sdk.workers.mockResolvedValue(workerResponse())
  })

  it('never projects a successful component instance cache into a rebuilt instance that gets 403', async () => {
    sdk.list.mockResolvedValueOnce(listResponse()).mockRejectedValueOnce(new Error('403 Forbidden'))
    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore(pinia)
    auth.principal = {
      principalId: 'principal-1',
      principalType: 'User',
      loginName: 'technician01',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.masterdata.resources.read',
      ],
    } as never

    const visible = ref(true)
    const instance = ref(1)
    const Harness = defineComponent({
      setup() {
        const queue = useMaintenanceSelfWorkOrders()
        return () =>
          h(
            'div',
            {
              'data-success': queue.hasSuccessfulResponse.value,
              'data-failed': queue.hasFailedResponse.value,
            },
            queue.items.value[0]?.workOrderId ?? 'no-row',
          )
      },
    })
    const Root = defineComponent({
      setup: () => () => (visible.value ? h(Harness, { key: instance.value }) : null),
    })
    const wrapper = mount(Root, {
      global: {
        plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000, staleTime: 5_000 } }]],
      },
    })

    await flushPromises()
    expect(wrapper.text()).toBe(WORK_ORDER_ID)
    expect(wrapper.get('[data-success="true"]')).toBeDefined()

    visible.value = false
    await nextTick()
    instance.value += 1
    visible.value = true
    await nextTick()

    expect(wrapper.text()).toBe('no-row')
    expect(wrapper.text()).not.toContain(WORK_ORDER_ID)
    await flushPromises()
    expect(sdk.list).toHaveBeenCalledTimes(2)
    expect(wrapper.text()).toBe('no-row')
    expect(wrapper.get('[data-failed="true"]')).toBeDefined()
  })
})
