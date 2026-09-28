import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import { useAuthStore } from '@/stores/auth'
import SparePartsPage from './spare-parts.vue'

const state = vi.hoisted(() => ({
  createSparePart: vi.fn(async (_body: Record<string, unknown>) => ({})),
  // 物料编码 → 基本单位；用例里改它来模拟物料列表晚一步刷新回来。
  baseUomBySku: undefined as unknown as { value: Map<string, string> },
}))

vi.mock('@/composables/useBusinessMaintenance', () => ({
  useMaintenanceSpareParts: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 20 }),
    spareParts: computed(() => []),
    sparePartsError: shallowRef(),
    sparePartsPending: shallowRef(false),
    sparePartsTotal: computed(() => 0),
    refreshSpareParts: vi.fn(),
    createSparePart: state.createSparePart,
    createSparePartPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useEquipmentPickerCatalog', () => {
  state.baseUomBySku = shallowRef(new Map([['BRG-6205', 'pcs']]))
  return {
    useEquipmentSkuCatalog: () => ({ baseUomBySku: state.baseUomBySku }),
    useEquipmentUomCatalog: () => ({
      uomOptions: computed(() => [{ value: 'pcs', label: '个' }]),
      uomsPending: shallowRef(false),
    }),
    useMaintenanceDocumentCatalog: () => ({
      workOrderOptions: computed(() => [
        { value: 'wo-1', label: 'WO-00000001' },
        { value: 'wo-2', label: 'WO-00000002' },
      ]),
      workOrdersPending: shallowRef(false),
      // wo-1 的设备登记在 SITE-001；wo-2 的设备没有所属工厂。
      workOrderDeviceId: (id?: string | null) =>
        id === 'wo-1' ? 'DEV-CNC-01' : id === 'wo-2' ? 'DEV-NO-SITE' : '',
    }),
    useDeviceSiteLookup: () => ({
      deviceSiteCode: (device?: string | null) => (device === 'DEV-CNC-01' ? 'SITE-001' : ''),
      siteLabel: (code: string) => (code === 'SITE-001' ? '一号工厂' : code),
      devicesPending: shallowRef(false),
    }),
  }
})

vi.mock('@/composables/useMasterDataDisplayNames', () => ({
  useMasterDataDisplayNames: () => ({
    formatUom: (code?: string | null) => code ?? '',
    resolveDevice: () => undefined,
  }),
}))

vi.mock('@/composables/useSkuNames', () => ({
  useSkuNames: () => ({ resolveSkuName: () => undefined }),
}))

const idInputStub = {
  props: ['modelValue', 'id', 'formSiteCode'],
  emits: ['update:modelValue'],
  template:
    '<input :id="id" :data-form-site-code="formSiteCode" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
}

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { template: '<a><slot /></a>' },
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogClose: { template: '<div><slot /></div>' },
  NvEntityPicker: idInputStub,
  // 物料选择器的取数与就地新增由 DirectoryPicker 自己的测试覆盖；这里只模拟「选中了某个编码」。
  DirectoryPicker: idInputStub,
}

async function type(wrapper: ReturnType<typeof mount>, selector: string, value: string) {
  await wrapper.get(selector).setValue(value)
  await flushPromises()
}

function signInWith(permissionCodes: string[]) {
  useAuthStore().$patch({
    principal: { principalType: 'user', principalId: 'user-1', loginName: 'user', permissionCodes },
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
  signInWith(['business.maintenance.work-orders.read', 'business.maintenance.work-orders.manage'])
})

describe('备件需求新建', () => {
  it('只读角色看不到「新建备件需求」', async () => {
    signInWith(['business.maintenance.work-orders.read'])
    const wrapper = mount(SparePartsPage, { global: { stubs } })
    await flushPromises()

    // 对话框被桩成常显，标题也叫「新建备件需求」；这里只看页头有没有这颗按钮。
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建备件需求'))).toBe(false)
  })

  it('就地新建物料后物料列表晚一步刷新回来，单位仍按新物料的基本单位带出', async () => {
    const wrapper = mount(SparePartsPage, { global: { stubs } })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建备件需求'))!
      .trigger('click')

    await type(wrapper, '#sp-work-order', 'wo-1')
    // 选择器选中新建的物料时，物料列表还没刷新回它，查不到基本单位。
    await type(wrapper, '#sp-sku', 'SKU-NEW')
    expect((wrapper.get('#sp-uom').element as HTMLInputElement).value).toBe('')

    // 物料列表刷新回来，基本单位查得到了。
    state.baseUomBySku.value = new Map([
      ['BRG-6205', 'pcs'],
      ['SKU-NEW', 'kg'],
    ])
    await flushPromises()
    expect((wrapper.get('#sp-uom').element as HTMLInputElement).value).toBe('kg')

    await type(wrapper, '#sp-location', 'loc-spare-01')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.createSparePart).toHaveBeenCalledWith(
      expect.objectContaining({ skuCode: 'SKU-NEW', uomCode: 'kg' }),
    )
  })

  it('备件从工单设备所在工厂的库位领出：工厂随工单带出，库位只在该工厂里选（#3902）', async () => {
    state.createSparePart.mockClear()
    const wrapper = mount(SparePartsPage, { global: { stubs } })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建备件需求'))!
      .trigger('click')

    await type(wrapper, '#sp-work-order', 'wo-1')
    expect((wrapper.get('#sp-site').element as HTMLInputElement).value).toBe('一号工厂')
    expect(wrapper.get('#sp-location').attributes('data-form-site-code')).toBe('SITE-001')
    await type(wrapper, '#sp-sku', 'BRG-6205')

    // 没选库位不能提交。
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.createSparePart).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('请选择领出库位。')

    await type(wrapper, '#sp-location', 'loc-spare-01')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.createSparePart).toHaveBeenCalledWith(
      expect.objectContaining({
        workOrderId: 'wo-1',
        siteCode: 'SITE-001',
        locationCode: 'loc-spare-01',
      }),
    )
  })

  it('工单设备没有所属工厂时如实提示，不猜工厂也不提交（#3902）', async () => {
    state.createSparePart.mockClear()
    const wrapper = mount(SparePartsPage, { global: { stubs } })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建备件需求'))!
      .trigger('click')

    await type(wrapper, '#sp-work-order', 'wo-2')
    expect((wrapper.get('#sp-site').element as HTMLInputElement).value).toBe('设备未登记所属工厂')
    await type(wrapper, '#sp-sku', 'BRG-6205')
    await type(wrapper, '#sp-location', 'loc-spare-01')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.createSparePart).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('该工单的设备未登记所属工厂')
  })
})
