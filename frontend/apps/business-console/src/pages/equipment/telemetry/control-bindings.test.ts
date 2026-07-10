import { flushPromises, mount } from '@vue/test-utils'
import type { BusinessConsoleTelemetryDeviceControlBindingItem } from '@nerv-iip/api-client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ControlBindingsPage from './control-bindings.vue'

const stub = vi.hoisted(() => ({
  saveBinding: vi.fn(() => Promise.resolve({ success: true })),
  disableBinding: vi.fn(() => Promise.resolve({ success: true })),
  refreshBindings: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const bindingState = vi.hoisted(() => ({
  rows: [
    {
      deviceControlChannelBindingId: 'binding-1',
      deviceAssetId: 'DEV-CNC-01',
      connectorHostId: 'connector-host-001',
      instanceKey: 'opcua-cell-01',
      isActive: true,
      disabledReason: null,
      updatedAtUtc: '2026-07-01T08:00:00Z',
    },
  ] as BusinessConsoleTelemetryDeviceControlBindingItem[],
}))

const authState = vi.hoisted(() => ({
  permissionCodes: [
    'business.iiot.device-control.read',
    'business.iiot.device-control.write',
  ] as string[],
}))

vi.mock('@/composables/useBusinessDeviceControlBinding', () => ({
  useBusinessDeviceControlBindings: () => ({
    bindings: computed(() => bindingState.rows),
    bindingsError: shallowRef(),
    bindingsPending: shallowRef(false),
    bindingsTotal: computed(() => bindingState.rows.length),
    filters: reactive({ deviceAssetId: '', skip: 0, take: 100 }),
    refreshBindings: stub.refreshBindings,
    saveBinding: stub.saveBinding,
    saveBindingError: shallowRef(),
    saveBindingPending: shallowRef(false),
    disableBinding: stub.disableBinding,
    disableBindingError: shallowRef(),
    disableBindingPending: shallowRef(false),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: { loginName: 'operator-a', permissionCodes: authState.permissionCodes },
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

// Nv* dialog/alert-dialog/row-actions wrap reka portals (jsdom crashes on unmount); render them in place.
const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  DialogPro: { template: '<div><slot /></div>' },
  DialogProTrigger: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuProContent: { template: '<div><slot /></div>' },
  DropdownMenuProItem: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
  AlertDialogPro: { template: '<div><slot /></div>' },
  AlertDialogProContent: { template: '<div><slot /></div>' },
  AlertDialogProHeader: { template: '<div><slot /></div>' },
  AlertDialogProFooter: { template: '<div><slot /></div>' },
  AlertDialogProTitle: { template: '<h2><slot /></h2>' },
  AlertDialogProDescription: { template: '<p><slot /></p>' },
  AlertDialogProCancel: { template: '<button type="button"><slot /></button>' },
  AlertDialogProAction: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
}

beforeEach(() => {
  bindingState.rows = [
    {
      deviceControlChannelBindingId: 'binding-1',
      deviceAssetId: 'DEV-CNC-01',
      connectorHostId: 'connector-host-001',
      instanceKey: 'opcua-cell-01',
      isActive: true,
      disabledReason: null,
      updatedAtUtc: '2026-07-01T08:00:00Z',
    },
  ]
  authState.permissionCodes = [
    'business.iiot.device-control.read',
    'business.iiot.device-control.write',
  ]
  stub.saveBinding.mockClear()
  stub.disableBinding.mockClear()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
})

describe('device control bindings page', () => {
  it('renders the title, binding rows, headers and create action', async () => {
    const wrapper = mount(ControlBindingsPage, { global: { stubs } })
    await flushPromises()

    expect(wrapper.text()).toContain('设备控制通道绑定')
    expect(wrapper.text()).toContain('DEV-CNC-01')
    expect(wrapper.text()).toContain('connector-host-001')
    for (const header of ['设备', '连接主机', '实例标识', '状态', '更新时间']) {
      expect(wrapper.text()).toContain(header)
    }
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建绑定'))).toBe(true)
  })

  it('creates a binding and shows a success toast', async () => {
    const wrapper = mount(ControlBindingsPage, { global: { stubs } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建绑定'))!
      .trigger('click')
    await wrapper.find('#binding-device').setValue('DEV-CNC-09')
    await wrapper.find('#binding-host').setValue('connector-host-009')
    await wrapper.find('#binding-instance').setValue('opcua-cell-09')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.saveBinding).toHaveBeenCalledWith({
      deviceAssetId: 'DEV-CNC-09',
      connectorHostId: 'connector-host-009',
      instanceKey: 'opcua-cell-09',
    })
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('does not submit when required fields are missing', async () => {
    const wrapper = mount(ControlBindingsPage, { global: { stubs } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建绑定'))!
      .trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.saveBinding).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('请填写设备编号')
  })

  it('disables a binding only after a reason is provided', async () => {
    const wrapper = mount(ControlBindingsPage, { global: { stubs } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('停用'))!
      .trigger('click')
    await flushPromises()

    // Confirm without a reason is a no-op.
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('确认停用'))!
      .trigger('click')
    await flushPromises()
    expect(stub.disableBinding).not.toHaveBeenCalled()

    await wrapper.find('#binding-disable-reason').setValue('通道迁移下线')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('确认停用'))!
      .trigger('click')
    await flushPromises()

    expect(stub.disableBinding).toHaveBeenCalledWith('DEV-CNC-01', '通道迁移下线')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })
})
