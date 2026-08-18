import { flushPromises, mount } from '@vue/test-utils'
import type { BusinessConsoleTelemetryDeviceControlBindingItem } from '@nerv-iip/api-client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ControlBindingsPage from './control-bindings.vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      deviceByCode: emptyIndex,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

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
    'business.iiot.device-control.manage',
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

// 设备与连接器实例目录走真实读面（useQuery）；单测只关心页面提交行为，给确定目录。
vi.mock('@/composables/useEquipmentPickerCatalog', () => ({
  useConnectorInstanceCatalog: () => ({
    connectorInstanceOptions: computed(() => [
      { value: 'opcua-cell-01', label: '一号车间采集器' },
      { value: 'opcua-cell-09', label: '九号车间采集器' },
    ]),
    connectorsPending: shallowRef(false),
  }),
  useEquipmentDeviceCatalog: () => ({
    deviceOptions: computed(() => [
      { value: 'DEV-CNC-01', label: '一号加工中心' },
      { value: 'DEV-CNC-09', label: '九号加工中心' },
    ]),
    devicesPending: shallowRef(false),
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
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  // 实体选择弹窗同样是 reka portal；单测只关心取值，替成同 id 的输入位。
  NvEntityPicker: {
    props: ['modelValue', 'id', 'options', 'loading', 'disabled'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  RowActions: { template: '<div><slot /></div>' },
  NvDropdownMenuContent: { template: '<div><slot /></div>' },
  NvDropdownMenuItem: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
  NvAlertDialog: { template: '<div><slot /></div>' },
  NvAlertDialogContent: { template: '<div><slot /></div>' },
  NvAlertDialogHeader: { template: '<div><slot /></div>' },
  NvAlertDialogFooter: { template: '<div><slot /></div>' },
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogCancel: { template: '<button type="button"><slot /></button>' },
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
    'business.iiot.device-control.manage',
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
