import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, ref } from 'vue'

// ---- vue-router mock（捕获 push）---------------------------------------------
const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// ---- useBusinessEquipmentAlarms mock ------------------------------------------
const filters = reactive<{ deviceAssetId?: string, skip: number, take: number }>({
  skip: 0,
  take: 100,
})
const alarms = ref<Array<Record<string, unknown>>>([
  {
    alarmEventId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    deviceAssetId: 'DEV-1001',
    alarmCode: 'E-101',
    severity: 'critical',
    raisedAtUtc: '2026-06-10T08:00:00Z',
    externalAlarmId: 'EXT-1',
  },
  {
    alarmEventId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    deviceAssetId: 'DEV-2002',
    alarmCode: 'W-200',
    severity: 'warning',
    raisedAtUtc: '2026-06-09T10:30:00Z',
    externalAlarmId: 'EXT-2',
  },
])
const total = ref(2)
const error = ref<unknown>(null)
const pending = ref(false)
const refresh = vi.fn(async () => {})

vi.mock('@/composables/useBusinessEquipmentAlarms', () => ({
  useBusinessEquipmentAlarms: () => ({
    filters,
    alarms,
    total,
    pending,
    error,
    refresh,
  }),
}))

import AlarmsPage from './alarms.vue'

beforeEach(() => {
  push.mockClear()
  refresh.mockClear()
  filters.deviceAssetId = undefined
  error.value = null
  pending.value = false
})

describe('PDA equipment alarms page', () => {
  it('renders alarm rows with device, alarm code and Chinese severity (no raw code)', () => {
    const wrapper = mount(AlarmsPage)
    const text = wrapper.text()
    expect(text).toContain('DEV-1001')
    expect(text).toContain('E-101') // business alarm code OK to show
    expect(text).toContain('严重') // severity critical → 中文
    expect(text).toContain('DEV-2002')
    expect(text).toContain('预警') // severity warning → 中文
    // raw severity codes must NOT be surfaced
    expect(text).not.toContain('critical')
    expect(text).not.toContain('warning')
  })

  it('never surfaces the alarmEventId / externalAlarmId GUID as a label', () => {
    const wrapper = mount(AlarmsPage)
    const text = wrapper.text()
    expect(text).not.toContain('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
    expect(text).not.toContain('EXT-1')
  })

  it('sets filters.deviceAssetId from a ScanBar scan', async () => {
    const wrapper = mount(AlarmsPage)
    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-SCAN-7')
    await scanInput.trigger('keydown.enter')
    expect(filters.deviceAssetId).toBe('DEV-SCAN-7')
  })

  it('clears the device filter', async () => {
    filters.deviceAssetId = 'DEV-1001'
    const wrapper = mount(AlarmsPage)
    await wrapper.get('[data-testid="clear-filter"]').trigger('click')
    expect(filters.deviceAssetId).toBeUndefined()
  })

  it('pushes to /equipment/repair carrying deviceAssetId + sourceAlarmId on 去报修', async () => {
    const wrapper = mount(AlarmsPage)
    await wrapper.get('[data-testid="repair-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]').trigger('click')
    expect(push).toHaveBeenCalledWith({
      path: '/equipment/repair',
      query: {
        deviceAssetId: 'DEV-1001',
        sourceAlarmId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      },
    })
  })

  it('shows the empty state only when not pending and no error', () => {
    alarms.value = []
    const wrapper = mount(AlarmsPage)
    expect(wrapper.text()).toContain('暂无设备报警')
    alarms.value = [
      {
        alarmEventId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        deviceAssetId: 'DEV-1001',
        alarmCode: 'E-101',
        severity: 'critical',
        raisedAtUtc: '2026-06-10T08:00:00Z',
        externalAlarmId: 'EXT-1',
      },
      {
        alarmEventId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        deviceAssetId: 'DEV-2002',
        alarmCode: 'W-200',
        severity: 'warning',
        raisedAtUtc: '2026-06-09T10:30:00Z',
        externalAlarmId: 'EXT-2',
      },
    ]
  })

  it('shows an error banner (not the empty state) when error is set', () => {
    const original = alarms.value
    alarms.value = []
    error.value = new Error('boom')
    const wrapper = mount(AlarmsPage)
    expect(wrapper.find('[data-testid="alarms-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无设备报警')
    alarms.value = original
  })

  it('does not show the empty state while pending', () => {
    const original = alarms.value
    alarms.value = []
    pending.value = true
    const wrapper = mount(AlarmsPage)
    expect(wrapper.text()).not.toContain('暂无设备报警')
    alarms.value = original
  })
})
