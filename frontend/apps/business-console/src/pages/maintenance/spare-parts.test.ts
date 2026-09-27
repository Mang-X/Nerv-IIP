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
      workOrderOptions: computed(() => [{ value: 'wo-1', label: 'WO-00000001' }]),
      workOrdersPending: shallowRef(false),
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
  props: ['modelValue', 'id'],
  emits: ['update:modelValue'],
  template:
    '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
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

    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.createSparePart).toHaveBeenCalledWith(
      expect.objectContaining({ skuCode: 'SKU-NEW', uomCode: 'kg' }),
    )
  })
})
