import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

// 真实 router.push 返回 Promise（index.vue 的 openTask 会 `.catch`）；mock 同此契约。
const push = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
}))

// 报警角标数据源：mock composable，避免拉起 pinia/colada；用 ref 驱动角标可见性。
const unacknowledgedCount = ref(0)
vi.mock('@/composables/useBusinessEquipmentAlarms', () => ({
  useUnacknowledgedAlarmCount: () => ({ unacknowledgedCount }),
}))

import HomePage from './index.vue'

/** Find an app-wall button by its visible label. */
function buttonByLabel(wrapper: ReturnType<typeof mount>, label: string) {
  const btn = wrapper.findAll('button').find((b) => b.text() === label)
  if (!btn) throw new Error(`app-wall button "${label}" not found`)
  return btn
}

describe('PDA home', () => {
  beforeEach(() => {
    push.mockReset()
    unacknowledgedCount.value = 0
  })

  it('shows the unacknowledged-alarm count badge on the 查看报警 tile, and hides it at zero', async () => {
    const wrapper = mount(HomePage)
    expect(wrapper.find('[data-testid="alarm-badge"]').exists()).toBe(false)

    unacknowledgedCount.value = 3
    await wrapper.vm.$nextTick()
    const badge = wrapper.find('[data-testid="alarm-badge"]')
    expect(badge.exists()).toBe(true)
    expect(badge.text()).toContain('3')

    // 角标挂在「查看报警」入口上，而非其它快捷应用
    const alarmTile = wrapper.findAll('button').find((b) => b.text().includes('查看报警'))
    expect(alarmTile?.find('[data-testid="alarm-badge"]').exists()).toBe(true)
  })

  it('renders the scan bar and the app wall from the task dictionary', () => {
    const wrapper = mount(HomePage)
    // 扫码条：以 placeholder 做稳健断言（不依赖 SFC 组件名推断）
    expect(wrapper.find('input[placeholder^="扫描"]').exists()).toBe(true)
    // 应用墙渲染字典中的任务标签（WMS / MES / 设备运维 三域）
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).toContain('报工')
    expect(wrapper.text()).toContain('报修')
    expect(wrapper.text()).toContain('点检')
    expect(wrapper.text()).toContain('查看报警')
  })

  it('shows an empty-state for "我的任务" until the backend personal-task facade lands', () => {
    const wrapper = mount(HomePage)
    expect(wrapper.text()).toContain('暂无分配给你的任务')
  })

  const ENTRIES: Array<[label: string, route: string]> = [
    // WMS
    ['收货入库', '/wms/inbound'],
    ['复核发货', '/wms/review'],
    ['拣货', '/wms/pick'],
    ['上架', '/wms/putaway'],
    ['盘点', '/wms/count'],
    // MES
    ['报工', '/mes/report'],
    ['领料', '/mes/issue'],
    ['完工入库', '/mes/receipt'],
    ['工序执行', '/mes/operation'],
    // 设备运维
    ['报修', '/equipment/repair'],
    ['点检', '/equipment/inspect'],
    ['查看报警', '/equipment/alarms'],
  ]

  it.each(ENTRIES)('enables the %s entry and navigates to %s on click', async (label, route) => {
    const wrapper = mount(HomePage)
    const btn = buttonByLabel(wrapper, label)
    // 合并后全部域已交付（routeReady=true）→ 入口均不再 disabled
    expect(btn.attributes('disabled')).toBeUndefined()
    push.mockClear()
    await btn.trigger('click')
    expect(push).toHaveBeenCalledWith(route)
  })

  it('echoes the scanned value in-page and does NOT navigate (scan-resolve is M5)', async () => {
    const wrapper = mount(HomePage)
    const input = wrapper.get('input[placeholder^="扫描"]')
    await input.setValue('WO-2026-0001')
    await input.trigger('keydown.enter')

    // 诚实的页内反馈：回显扫码内容，不做假跳转到尚不存在的 /scan。
    expect(wrapper.text()).toContain('已扫码：WO-2026-0001')
    expect(push).not.toHaveBeenCalled()
  })
})
