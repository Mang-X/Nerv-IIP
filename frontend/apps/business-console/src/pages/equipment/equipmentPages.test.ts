import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, shallowRef } from 'vue'

import EquipmentAlarmsPage from './alarms.vue'
import EquipmentDetailPage from './[deviceAssetId].vue'
import EquipmentIndexPage from './index.vue'

const routeState = vi.hoisted(() => ({
  route: undefined as { params: { deviceAssetId: string } } | undefined,
}))

const equipmentComposableState = vi.hoisted(() => ({
  deviceFilters: { deviceAssetId: 'DEV-OIL-01' },
  refreshDevice: vi.fn(),
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  routeState.route = reactive({ params: { deviceAssetId: 'DEV-OIL-01' } })

  return {
    ...actual,
    useRoute: () => routeState.route,
  }
})

vi.mock('@/composables/useBusinessEquipment', () => ({
  describeEquipmentReason: (code: string) => ({
    code,
    label: code || '未知',
    nextStep: '查看设备详情并处理来源业务单据',
  }),
  equipmentStatusTone: () => 'success',
  useBusinessEquipmentAlarms: () => ({
    alarms: computed(() => []),
    alarmsError: shallowRef(),
    alarmsPending: shallowRef(false),
    refreshAlarms: vi.fn(),
  }),
  useBusinessEquipmentDevice: () => ({
    activeAlarms: computed(() => []),
    availabilityWindows: computed(() => []),
    device: computed(() => ({
      currentState: {
        deviceAssetId: 'DEV-OIL-01',
        currentState: 'running',
        isSourceFresh: true,
      },
    })),
    deviceError: shallowRef(),
    devicePending: shallowRef(false),
    filters: equipmentComposableState.deviceFilters,
    refreshDevice: equipmentComposableState.refreshDevice,
  }),
  useBusinessEquipmentOverview: () => ({
    activeBlocks: computed(() => []),
    devices: computed(() => []),
    filters: {
      deviceAssetIds: 'DEV-OIL-01,DEV-PACK-01',
    },
    overviewError: shallowRef(),
    overviewPending: shallowRef(false),
    refreshOverview: vi.fn(),
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}

describe('equipment pages', () => {
  beforeEach(() => {
    if (routeState.route) {
      routeState.route.params.deviceAssetId = 'DEV-OIL-01'
    }
    equipmentComposableState.deviceFilters.deviceAssetId = 'DEV-OIL-01'
    equipmentComposableState.refreshDevice.mockClear()
  })

  it('does not expose organization or environment context on equipment pages', () => {
    for (const page of [EquipmentIndexPage, EquipmentAlarmsPage, EquipmentDetailPage]) {
      const wrapper = mount(page, { global: { stubs } })

      expect(wrapper.text()).not.toContain('组织')
      expect(wrapper.text()).not.toContain('环境')
      expect(wrapper.html()).not.toContain('organizationId')
      expect(wrapper.html()).not.toContain('environmentId')
    }
  })

  it('updates the device filter and refreshes when route device id changes', async () => {
    mount(EquipmentDetailPage, { global: { stubs } })

    routeState.route!.params.deviceAssetId = 'DEV-PACK-02'
    await nextTick()

    expect(equipmentComposableState.deviceFilters.deviceAssetId).toBe('DEV-PACK-02')
    expect(equipmentComposableState.refreshDevice).toHaveBeenCalledTimes(1)
  })
})
