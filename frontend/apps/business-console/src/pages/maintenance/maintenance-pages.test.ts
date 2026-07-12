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
const completeWorkOrder = vi.hoisted(() => vi.fn())

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
    workOrders: computed(() => [{
      workOrderId: '11111111-2222-3333-4444-55555555abcd',
      deviceAssetId: 'DEV-PRESS-01',
      priority: 'high',
      status: 'open',
      openedAtUtc: '2026-07-06T00:00:00Z',
      warrantyStatus: 'in-warranty',
      warrantyExpiresOn: '2027-01-14',
      supplierPartnerCode: 'SUP-ACME',
      assignedTechnicianUserId: 'worker-planned',
    }]),
    workOrdersError: shallowRef(),
    workOrdersPending: shallowRef(false),
    workOrdersTotal: computed(() => 0),
    refreshWorkOrders: vi.fn(),
    createWorkOrder: vi.fn(),
    createWorkOrderPending: shallowRef(false),
    createWorkOrderError: shallowRef(),
    completeWorkOrder,
    completeWorkOrderPending: shallowRef(false),
    completeWorkOrderError: shallowRef(),
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvDataTable: { props: ['rows'], template: '<div><slot name="cell-warrantyStatus" :row="rows[0]" /><slot name="cell-actions" :row="rows[0]" /></div>' },
  NvRowActions: { template: '<div><slot /></div>' },
  NvDropdownMenuItem: { template: '<button type="button" @click="$emit(\'click\')"><slot /></button>' },
  WorkerSelect: {
    props: ['modelValue', 'id'],
    emits: ['update:modelValue'],
    template: '<select :id="id" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value="worker-planned">计划技师</option><option value="worker-actual">实际技师</option></select>',
  },
}

describe('maintenance pages', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
    routeState.query = {
      deviceAssetId: 'DEV-PRESS-01',
      sourceAlarmId: 'ALARM-9001',
    }
    completeWorkOrder.mockReset()
  })

  it('prefills maintenance work order creation from equipment alarm context', async () => {
    mount(WorkOrdersPage, {
      attachTo: document.body,
      global: { stubs },
    })
    await flushPromises()

    expect(document.body.textContent).toContain('新建维护工单')
    expect(document.body.textContent).toContain('在保')
    expect(document.body.textContent).toContain('SUP-ACME')
    expect(document.body.querySelector<HTMLInputElement>('#mwo-device')?.value).toBe('DEV-PRESS-01')
    expect(document.body.querySelector<HTMLInputElement>('#mwo-alarm')?.value).toBe('ALARM-9001')
  })

  it('submits the selected actual technician without replacing the planned assignment', async () => {
    routeState.query = {}
    const wrapper = mount(WorkOrdersPage, {
      attachTo: document.body,
      global: { stubs },
    })
    await flushPromises()

    ;(wrapper.vm as unknown as { openComplete: (row: Record<string, unknown>) => void }).openComplete({
      workOrderId: '11111111-2222-3333-4444-55555555abcd',
      deviceAssetId: 'DEV-PRESS-01',
      assignedTechnicianUserId: 'worker-planned',
    })
    await flushPromises()

    const technician = document.body.querySelector<HTMLSelectElement>('#mwo-actual-technician')
    expect(technician?.value).toBe('worker-planned')
    const vm = wrapper.vm as unknown as {
      completeForm: { actualTechnicianUserId: string; sparePartSku: string }
      submitComplete: () => Promise<void>
    }
    vm.completeForm.actualTechnicianUserId = 'worker-actual'
    vm.completeForm.sparePartSku = 'SPARE-001'
    await vm.submitComplete()
    await flushPromises()

    expect(completeWorkOrder).toHaveBeenCalledWith(
      '11111111-2222-3333-4444-55555555abcd',
      expect.objectContaining({ actualTechnicianUserId: 'worker-actual' }),
    )
  })
})
