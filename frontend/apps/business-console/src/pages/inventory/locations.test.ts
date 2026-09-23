import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import LocationsPage from './locations.vue'

const stub = vi.hoisted(() => ({
  saveLocation: vi.fn(),
  locationCodeExists: vi.fn(),
}))

vi.mock('@/composables/useBusinessInventory', () => ({
  useInventoryLocations: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', keyword: undefined }),
    locationCodeExists: stub.locationCodeExists,
    locationRows: computed(() => [
      {
        locationId: 'location-001',
        locationCode: 'loc-line-01',
        locationType: 'line-side',
        siteCode: 'SITE-001',
        parentLocationCode: null,
        status: 'inactive',
        updatedAtUtc: '2026-09-23T08:00:00Z',
      },
    ]),
    locationsError: shallowRef(undefined),
    locationsPage: shallowRef(1),
    locationsPageSize: shallowRef(20),
    locationsPending: shallowRef(false),
    locationsTotal: computed(() => 1),
    refreshLocations: vi.fn(),
    saveLocation: stub.saveLocation,
    saveLocationPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({
    resources: computed(() => [{ code: 'SITE-001', displayName: '一号工厂' }]),
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvDialog: { template: '<div><slot /></div>' },
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
  })

  it('shows the location type and status in business language', async () => {
    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('线边库位')
    expect(wrapper.text()).toContain('停用')
    expect(wrapper.text()).toContain('一号工厂')
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
