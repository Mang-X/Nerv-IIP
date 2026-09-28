import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import DeviceQuickViewSheet from './DeviceQuickViewSheet.vue'

// 设备速览抽屉：窗口状态与采集接入的说法、颜色走真实的字典与判据，只桩数据 composable。

const state = vi.hoisted(() => ({
  availabilityWindows: [] as Array<Record<string, unknown>>,
}))

vi.mock('@/composables/useBusinessEquipment', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/composables/useBusinessEquipment')>()),
  useBusinessEquipmentDevice: () => ({
    activeAlarms: computed(() => []),
    availabilityWindows: computed(() => state.availabilityWindows),
    device: computed(() => ({
      currentState: { deviceAssetId: 'EQ00001', currentState: null, isSourceFresh: false },
    })),
    deviceError: shallowRef(),
    devicePending: shallowRef(false),
    filters: reactive({ deviceAssetId: 'EQ00001' }),
  }),
}))

function windowBadges() {
  const items = [...document.body.querySelectorAll('li')]
  return Object.fromEntries(
    items.map((li) => {
      const badge = li.querySelector('[data-slot="nv-badge"]')!
      return [li.querySelector('span')!.textContent!.trim(), badge]
    }),
  )
}

describe('设备速览抽屉：最近可用性窗口', () => {
  it('状态未知的窗口（尚未接入 / 采集过期）用中性色说「状态未知」，不说成绿色「可用」', async () => {
    state.availabilityWindows = [
      {
        deviceAssetId: 'EQ00001',
        availabilityStatus: 'unknown',
        reasonCode: 'equipment.sourceNotConnected',
      },
      {
        deviceAssetId: 'EQ00001',
        availabilityStatus: 'unavailable',
        reasonCode: 'equipment.maintenanceWindow',
      },
      {
        deviceAssetId: 'EQ00001',
        availabilityStatus: 'available',
        reasonCode: 'equipment.downtime',
      },
    ]

    const wrapper = mount(DeviceQuickViewSheet, {
      props: { deviceAssetId: 'EQ00001', open: true },
      global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } },
      attachTo: document.body,
    })
    await flushPromises()

    const badges = windowBadges()
    expect(badges['尚未接入采集'].textContent?.trim()).toBe('状态未知')
    expect([...badges['尚未接入采集'].classList]).toContain('bg-muted')
    expect(badges['维修保养占用'].textContent?.trim()).toBe('不可用')
    expect(badges['设备停机中'].textContent?.trim()).toBe('可用')
    wrapper.unmount()
  })
})
