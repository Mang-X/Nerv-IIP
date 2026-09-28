import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import AvailabilityPage from './availability.vue'

const WORK_ORDER_GUID = '0199a3b4-5c6d-7e8f-9a0b-1c2d3e4f5a6b'
const DEVICE_GUID = '0199a3b4-0000-7e8f-9a0b-1c2d3e4f5a6b'

const windows = [
  {
    deviceAssetId: 'DEV-PRESS-01',
    availabilityStatus: 'unavailable',
    reasonCode: 'equipment.downtime',
    sourceType: 'downtime',
    sourceReferenceId: WORK_ORDER_GUID,
    sourceReferenceLabel: 'MWO-2026-0007',
    startUtc: '2026-09-28T01:00:00Z',
    endUtc: '2026-09-28T03:00:00Z',
  },
  {
    deviceAssetId: DEVICE_GUID,
    availabilityStatus: 'unavailable',
    reasonCode: 'equipment.activeAlarm',
    sourceType: 'alarm',
    sourceReferenceId: 'WH-DEV-ASM-12-press-force:0000',
    sourceReferenceLabel: null,
    startUtc: '2026-09-28T04:00:00Z',
    endUtc: '2026-09-28T05:00:00Z',
  },
]

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => reactive({ query: {} }),
}))

vi.mock('@/composables/useBusinessMaintenance', () => ({
  useMaintenanceAvailabilityWindows: () => ({
    availabilityError: shallowRef(),
    availabilityPending: shallowRef(false),
    availabilityWindows: computed(() => windows),
    filters: reactive({
      deviceAssetIds: '',
      workCenterIds: '',
      windowStartUtc: '2026-09-28T00:00:00Z',
      windowEndUtc: '2026-09-29T00:00:00Z',
    }),
    refreshAvailability: vi.fn(),
  }),
}))

vi.mock('@/composables/useEquipmentScopeSelection', () => ({
  useEquipmentScopeSelection: () => ({
    scope: shallowRef({}),
    levels: computed(() => []),
    devicesInScope: computed(() => [{ code: 'DEV-PRESS-01' }]),
    scopeLabel: computed(() => '全厂'),
    scopePending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useEquipmentPickerCatalog', () => ({
  useEquipmentWorkCenterCatalog: () => ({
    workCenterOptions: computed(() => []),
    workCentersPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useMasterDataDisplayNames', () => ({
  useMasterDataDisplayNames: () => ({
    resolveDevice: (reference?: string | null) =>
      reference === 'DEV-PRESS-01' ? '冲压机 1 号' : undefined,
    resolveDeviceCode: (reference?: string | null) =>
      reference === 'DEV-PRESS-01' ? 'DEV-PRESS-01' : undefined,
    resolveWorkCenter: () => undefined,
  }),
}))

function mountPage() {
  return mount(AvailabilityPage, {
    global: {
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        EntityMultiPicker: true,
        NvCascadePicker: true,
        RouterLink: { props: ['to'], template: '<a><slot /></a>' },
      },
    },
  })
}

describe('维保可用窗口页', () => {
  it('设备列显示「名称（编码）」，关联业务显示工单号，不露 GUID 与合成键', async () => {
    const wrapper = mountPage()
    await flushPromises()
    const text = wrapper.text()

    expect(text).toContain('冲压机 1 号（DEV-PRESS-01）')
    expect(text).toContain('MWO-2026-0007')
    expect(text).not.toContain(WORK_ORDER_GUID)
    expect(text).not.toContain(DEVICE_GUID)
    expect(text).not.toContain('WH-DEV')
  })
})
