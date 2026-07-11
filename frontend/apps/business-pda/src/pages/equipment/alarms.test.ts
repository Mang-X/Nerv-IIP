import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, ref } from 'vue'

// ---- vue-router mock（捕获 push）---------------------------------------------
const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// ---- useBusinessEquipmentAlarms mock ------------------------------------------
const filters = reactive<{ deviceAssetId?: string; skip: number; take: number }>({
  skip: 0,
  take: 100,
})
const RAISED = {
  alarmEventId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  deviceAssetId: 'DEV-1001',
  alarmCode: 'E-101',
  severity: 'critical',
  status: 'raised',
  raisedAtUtc: '2026-06-10T08:00:00Z',
  externalAlarmId: 'EXT-1',
}
const ACKED = {
  alarmEventId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  deviceAssetId: 'DEV-2002',
  alarmCode: 'W-200',
  severity: 'warning',
  status: 'acknowledged',
  raisedAtUtc: '2026-06-09T10:30:00Z',
  acknowledgedBy: '张三',
  acknowledgedAtUtc: '2026-06-09T10:32:00Z',
  externalAlarmId: 'EXT-2',
}
const alarms = ref<Array<Record<string, unknown>>>([RAISED, ACKED])
const total = ref(2)
const error = ref<unknown>(null)
const pending = ref(false)
const refresh = vi.fn(async () => {})
const acknowledge = vi.fn(async () => ({ success: true }))
const shelve = vi.fn(async () => ({ success: true }))
const actionPending = ref(false)

vi.mock('@/composables/useBusinessEquipmentAlarms', () => ({
  ALARM_SHELVE_DURATIONS_MINUTES: [30, 120, 480],
  useBusinessEquipmentAlarms: () => ({
    filters,
    alarms,
    total,
    pending,
    error,
    refresh,
    acknowledge,
    shelve,
    actionPending,
  }),
}))

import AlarmsPage from './alarms.vue'

beforeEach(() => {
  push.mockClear()
  refresh.mockClear()
  acknowledge.mockClear()
  shelve.mockClear()
  filters.deviceAssetId = undefined
  error.value = null
  pending.value = false
  alarms.value = [RAISED, ACKED]
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

  it('shows 确认/搁置 buttons only on unacknowledged rows; processed rows show a status tag + who/when', () => {
    const wrapper = mount(AlarmsPage)
    // raised row → dual action buttons
    expect(wrapper.find('[data-testid="ack-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]').exists()).toBe(
      true,
    )
    expect(
      wrapper.find('[data-testid="shelve-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]').exists(),
    ).toBe(true)
    // acknowledged row → no action buttons, a status tag instead
    expect(wrapper.find('[data-testid="ack-bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"]').exists()).toBe(
      false,
    )
    const tag = wrapper.find('[data-testid="status-bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"]')
    expect(tag.exists()).toBe(true)
    expect(tag.text()).toContain('已确认')
    // processed meta: who + when
    expect(wrapper.text()).toContain('张三')
  })

  it('acknowledges via a lightweight confirm dialog (2 taps): 确认 button → dialog 确认', async () => {
    const wrapper = mount(AlarmsPage, { attachTo: document.body })
    await wrapper.get('[data-testid="ack-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]').trigger('click')
    await flushPromises()
    // dialog teleported to body; confirm button carries the ds-md-confirm class
    const confirmBtn = document.body.querySelector<HTMLElement>('.ds-md-confirm')
    expect(confirmBtn).toBeTruthy()
    confirmBtn!.click()
    await flushPromises()
    expect(acknowledge).toHaveBeenCalledWith('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
    wrapper.unmount()
  })

  it('shelves via an ActionSheet duration pick (30m/2h/8h)', async () => {
    const wrapper = mount(AlarmsPage, { attachTo: document.body })
    await wrapper
      .get('[data-testid="shelve-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]')
      .trigger('click')
    await flushPromises()
    const options = [...document.body.querySelectorAll<HTMLButtonElement>('button')]
    // three durations present
    expect(options.some((b) => b.textContent?.includes('30 分钟'))).toBe(true)
    expect(options.some((b) => b.textContent?.includes('2 小时'))).toBe(true)
    expect(options.some((b) => b.textContent?.includes('8 小时'))).toBe(true)
    const twoHours = options.find((b) => b.textContent?.includes('2 小时'))!
    twoHours.click()
    await flushPromises()
    expect(shelve).toHaveBeenCalledWith('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 120)
    wrapper.unmount()
  })

  it('keeps 去报修 in the row detail sheet, carrying deviceAssetId + sourceAlarmId', async () => {
    const wrapper = mount(AlarmsPage, { attachTo: document.body })
    // tap the row (not the trailing buttons) → detail sheet
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const repairBtn = document.body.querySelector<HTMLElement>(
      '[data-testid="repair-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]',
    )
    expect(repairBtn).toBeTruthy()
    repairBtn!.click()
    await flushPromises()
    expect(push).toHaveBeenCalledWith({
      path: '/equipment/repair',
      query: {
        deviceAssetId: 'DEV-1001',
        sourceAlarmId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      },
    })
    wrapper.unmount()
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

  it('shows the empty state only when not pending and no error', () => {
    alarms.value = []
    const wrapper = mount(AlarmsPage)
    expect(wrapper.text()).toContain('暂无设备报警')
  })

  it('shows an error banner (not the empty state) when error is set', () => {
    alarms.value = []
    error.value = new Error('boom')
    const wrapper = mount(AlarmsPage)
    expect(wrapper.find('[data-testid="alarms-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无设备报警')
  })

  it('does not show the empty state while pending', () => {
    alarms.value = []
    pending.value = true
    const wrapper = mount(AlarmsPage)
    expect(wrapper.text()).not.toContain('暂无设备报警')
  })
})
