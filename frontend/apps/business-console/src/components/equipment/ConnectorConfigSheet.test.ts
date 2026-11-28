import { flushPromises, mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import { ref } from 'vue'
import ConnectorConfigSheet from './ConnectorConfigSheet.vue'

const api = vi.hoisted(() => ({
  testConnection: vi.fn(async () => ({ reachable: true, latencyMs: 14, samplePreview: 42.5 })),
  saveConfig: vi.fn(async () => {}),
}))
vi.mock('@/composables/equipment/useConnectorConfigs', () => ({
  useConnectorConfigs: () => ({ configs: ref([]), ...api }),
}))
vi.mock('@/composables/equipment/useEquipmentPickerCatalog', () => ({
  useEquipmentDeviceCatalog: () => ({
    devicePublicIdByCode: ref(new Map([['DEV-CTG-01', 'public-coating']])),
  }),
}))
vi.mock('@/utils/notify', () => ({ notifyOperationFailure: vi.fn(), notifySuccess: vi.fn() }))

it('tests a simulator mapping and saves its public device reference', async () => {
  const wrapper = mount(ConnectorConfigSheet, {
    props: { config: null, open: false },
    global: {
      stubs: {
        NvButton: { template: '<button><slot /></button>' },
        NvSheet: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
        NvSheetContent: { template: '<div><slot /></div>' },
        NvSheetHeader: { template: '<div><slot /></div>' },
        NvSheetDescription: { template: '<p><slot /></p>' },
        NvSheetTitle: { template: '<h2><slot /></h2>' },
      },
    },
  })
  await wrapper.setProps({ open: true })
  const button = (label: string) =>
    wrapper.findAll('button').find((item) => item.text().includes(label))!
  await button('DEV-CTG-01').trigger('click')
  await button('测试连接').trigger('click')
  await flushPromises()
  expect(api.testConnection).toHaveBeenCalledWith({
    protocol: 'modbus',
    endpoint: 'tcp://127.0.0.1:15020',
    mapping: expect.objectContaining({ unitId: 3, address: 40001, tagKey: 'bath-temperature' }),
  })
  expect(wrapper.get('[role=status]').text()).toContain('可达 · 14 ms')
  expect(wrapper.get('[role=status]').text()).toContain('42.5')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(api.saveConfig).toHaveBeenCalledWith(
    expect.objectContaining({
      mappings: [
        expect.objectContaining({ deviceAssetId: 'public-coating', tagKey: 'bath-temperature' }),
        expect.objectContaining({ deviceAssetId: 'public-coating', tagKey: 'bath-ph' }),
      ],
    }),
  )
  wrapper.unmount()
})
