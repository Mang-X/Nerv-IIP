import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, ref } from 'vue'
import { RequestTimeoutError } from '@/api/request-timeout'

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
const acknowledge = vi.fn(async (_id: string, _atUtc: string) => ({ success: true }))
const shelve = vi.fn(async (_id: string, _minutes: number, _atUtc: string, _reason?: string) => ({
  success: true,
}))
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
  acknowledge.mockReset()
  acknowledge.mockResolvedValue({ success: true })
  shelve.mockReset()
  shelve.mockResolvedValue({ success: true })
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
    expect(text).toContain('E-101')
    expect(text).toContain('严重')
    expect(text).toContain('DEV-2002')
    expect(text).toContain('预警')
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
    expect(wrapper.find('[data-testid="ack-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]').exists()).toBe(
      true,
    )
    expect(
      wrapper.find('[data-testid="shelve-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]').exists(),
    ).toBe(true)
    expect(wrapper.find('[data-testid="ack-bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"]').exists()).toBe(
      false,
    )
    const tag = wrapper.find('[data-testid="status-bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"]')
    expect(tag.exists()).toBe(true)
    expect(tag.text()).toContain('已确认')
    expect(wrapper.text()).toContain('张三')
  })

  // P2 回归：行不再是可交互控件，动作/详情是行内同级按钮，键盘操作不会误开详情。
  it('rows are non-interactive; detail opens via its own button, not a bubbling row keydown', async () => {
    const wrapper = mount(AlarmsPage, { attachTo: document.body })
    const row = wrapper.findAll('[data-row]')[0]
    expect(row.attributes('role')).toBeUndefined()
    expect(row.attributes('tabindex')).toBeUndefined()

    // Enter 落在行上不应打开详情（行无 keydown 处理）
    await row.trigger('keydown.enter')
    await flushPromises()
    expect(document.body.textContent).not.toContain('去报修')

    // 详情入口是独立按钮
    await wrapper
      .get('[data-testid="detail-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]')
      .trigger('click')
    await flushPromises()
    expect(
      document.body.querySelector('[data-testid="repair-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]'),
    ).toBeTruthy()
    wrapper.unmount()
  })

  it('acknowledges via a lightweight confirm dialog with a stable timestamp', async () => {
    const wrapper = mount(AlarmsPage, { attachTo: document.body })
    await wrapper.get('[data-testid="ack-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]').trigger('click')
    await flushPromises()
    const confirmBtn = document.body.querySelector<HTMLElement>('.ds-md-confirm')
    expect(confirmBtn).toBeTruthy()
    confirmBtn!.click()
    await flushPromises()
    expect(acknowledge).toHaveBeenCalledTimes(1)
    const [id, atUtc] = acknowledge.mock.calls[0]
    expect(id).toBe('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
    expect(typeof atUtc).toBe('string')
    expect(Number.isNaN(Date.parse(atUtc))).toBe(false)
    wrapper.unmount()
  })

  it('shelves via an ActionSheet duration pick, passing minutes + a stable timestamp', async () => {
    const wrapper = mount(AlarmsPage, { attachTo: document.body })
    await wrapper
      .get('[data-testid="shelve-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]')
      .trigger('click')
    await flushPromises()
    const options = [...document.body.querySelectorAll<HTMLButtonElement>('button')]
    expect(options.some((b) => b.textContent?.includes('30 分钟'))).toBe(true)
    expect(options.some((b) => b.textContent?.includes('2 小时'))).toBe(true)
    expect(options.some((b) => b.textContent?.includes('8 小时'))).toBe(true)
    options.find((b) => b.textContent?.includes('2 小时'))!.click()
    await flushPromises()
    expect(shelve).toHaveBeenCalledTimes(1)
    const [id, minutes, atUtc] = shelve.mock.calls[0]
    expect(id).toBe('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
    expect(minutes).toBe(120)
    expect(typeof atUtc).toBe('string')
    wrapper.unmount()
  })

  // P1 幂等：确定性失败可重试，且重试复用同一 atUtc（搁置窗口不因重试延长）。
  it('retries a determinate failure with the SAME stable timestamp', async () => {
    shelve.mockRejectedValueOnce({ message: '设备不存在' }) // 业务错误对象 → 确定性、可重试
    const wrapper = mount(AlarmsPage, { attachTo: document.body })
    await wrapper
      .get('[data-testid="shelve-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]')
      .trigger('click')
    await flushPromises()
    document.body
      .querySelectorAll<HTMLButtonElement>('button')
      .forEach((b) => b.textContent?.includes('2 小时') && b.click())
    await flushPromises()

    // 失败对话框出现，含「重试」
    const dialogText = document.body.textContent ?? ''
    expect(dialogText).toContain('操作失败')
    const retryBtn = document.body.querySelector<HTMLElement>('.ds-md-confirm')
    expect(retryBtn?.textContent).toContain('重试')
    retryBtn!.click()
    await flushPromises()

    expect(shelve).toHaveBeenCalledTimes(2)
    // 第 2 次（重试）复用第 1 次的 atUtc → 窗口固定、不延长
    expect(shelve.mock.calls[1][2]).toBe(shelve.mock.calls[0][2])
    expect(shelve.mock.calls[1][1]).toBe(shelve.mock.calls[0][1]) // 同 minutes
    wrapper.unmount()
  })

  // P1 幂等：已发出但结果未知（超时）不盲目重试——刷新列表引导核对。
  it('does NOT offer blind retry on an indeterminate failure; steers to verify + refreshes', async () => {
    acknowledge.mockRejectedValue(new RequestTimeoutError())
    const wrapper = mount(AlarmsPage, { attachTo: document.body })
    await wrapper.get('[data-testid="ack-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]').trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('.ds-md-confirm')!.click() // 确认弹层
    await flushPromises()

    expect(acknowledge).toHaveBeenCalledTimes(1)
    const dialogText = document.body.textContent ?? ''
    expect(dialogText).toContain('提交结果未知')
    expect(dialogText).toContain('核对')
    // 无「重试」按钮，只有「我知道了」
    const confirmBtn = document.body.querySelector<HTMLElement>('.ds-md-confirm')
    expect(confirmBtn?.textContent).toContain('我知道了')
    expect(refresh).toHaveBeenCalled()

    // 点「我知道了」不会再次发起确认
    confirmBtn!.click()
    await flushPromises()
    expect(acknowledge).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('keeps 去报修 in the row detail sheet, carrying deviceAssetId + sourceAlarmId', async () => {
    const wrapper = mount(AlarmsPage, { attachTo: document.body })
    await wrapper
      .get('[data-testid="detail-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]')
      .trigger('click')
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
