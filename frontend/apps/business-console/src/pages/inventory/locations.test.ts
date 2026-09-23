import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'

import LocationsPage from './locations.vue'

const stub = vi.hoisted(() => ({
  saveLocation: vi.fn(),
  locationCodeExists: vi.fn(),
  rows: [] as unknown[],
  filters: undefined as undefined | { keyword?: string },
}))

const LINE_SIDE_ROW = {
  locationId: 'location-001',
  locationCode: 'loc-line-01',
  locationType: 'line-side',
  siteCode: 'SITE-001',
  parentLocationCode: null,
  status: 'inactive',
  updatedAtUtc: '2026-09-23T08:00:00Z',
}

vi.mock('@/composables/useBusinessInventory', async () => {
  const { computed, reactive, shallowRef } = await import('vue')
  return {
    useInventoryLocations: () => {
      const filters = reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        keyword: undefined as string | undefined,
      })
      stub.filters = filters
      return {
        filters,
        locationCodeExists: stub.locationCodeExists,
        locationRows: computed(() => stub.rows),
        locationsError: shallowRef(undefined),
        locationsPage: shallowRef(1),
        locationsPageSize: shallowRef(20),
        locationsPending: shallowRef(false),
        locationsTotal: computed(() => stub.rows.length),
        refreshLocations: vi.fn(),
        saveLocation: stub.saveLocation,
        saveLocationPending: shallowRef(false),
      }
    },
  }
})

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({
    resources: computed(() => [{ code: 'SITE-001', displayName: '一号工厂' }]),
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  // 与真实弹窗一致：未打开时不渲染内容，避免对话框里的下拉选项文字混进表格断言。
  NvDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

async function mountPage() {
  const wrapper = mount(LocationsPage, { global: { stubs } })
  await flushPromises()
  return wrapper
}

function button(wrapper: Awaited<ReturnType<typeof mountPage>>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().includes(text))!
}

describe('inventory locations page', () => {
  beforeEach(() => {
    stub.saveLocation.mockReset().mockResolvedValue({})
    stub.locationCodeExists.mockReset().mockResolvedValue(false)
    stub.rows = [LINE_SIDE_ROW]
  })

  it('shows the location type, site and status in business language in the table', async () => {
    const wrapper = await mountPage()

    const row = wrapper.find('tbody tr')
    expect(row.text()).toContain('loc-line-01')
    expect(row.text()).toContain('线边库位')
    expect(row.text()).toContain('一号工厂')
    expect(row.text()).toContain('停用')
    expect(row.text()).not.toContain('line-side')
    expect(row.text()).not.toContain('SITE-001')
  })

  it('offers 清空筛选 instead of the first-use hint when a search finds nothing', async () => {
    stub.rows = []
    const wrapper = await mountPage()
    await wrapper.find('input[type="search"], input').setValue('nope')
    await flushPromises()

    expect(wrapper.text()).toContain('没有符合条件的库位')
    expect(wrapper.text()).not.toContain('还没有库位')
    await button(wrapper, '清空筛选').trigger('click')
    await flushPromises()
    expect(stub.filters?.keyword).toBeUndefined()
    expect(wrapper.text()).toContain('还没有库位')
  })

  it('creates a line-side location with the chosen type and site', async () => {
    const wrapper = await mountPage()

    await button(wrapper, '新建库位').trigger('click')
    await wrapper.find('#location-code').setValue('loc-line-02')
    await wrapper.findAll('select')[0]!.setValue('line-side')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.locationCodeExists).toHaveBeenCalledWith('loc-line-02')
    expect(stub.saveLocation).toHaveBeenCalledWith({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      locationCode: 'loc-line-02',
      locationType: 'line-side',
      siteCode: 'SITE-001',
      parentLocationCode: null,
      status: 'active',
    })
  })

  it('does not overwrite an existing location from the create dialog', async () => {
    stub.locationCodeExists.mockResolvedValue(true)
    const wrapper = await mountPage()

    await button(wrapper, '新建库位').trigger('click')
    await wrapper.find('#location-code').setValue('loc-line-01')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.saveLocation).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('库位编码已存在')
  })

  it('re-activates an inactive location from the edit dialog without asking for the code again', async () => {
    const wrapper = await mountPage()

    await button(wrapper, '编辑').trigger('click')
    expect(wrapper.find('#location-code').exists()).toBe(false)
    const statusSelect = wrapper.findAll('select').at(-1)!
    await statusSelect.setValue('active')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.locationCodeExists).not.toHaveBeenCalled()
    expect(stub.saveLocation).toHaveBeenCalledWith(
      expect.objectContaining({
        locationCode: 'loc-line-01',
        locationType: 'line-side',
        status: 'active',
      }),
    )
  })
})
