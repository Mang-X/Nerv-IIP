import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// 真实 router.push 返回 Promise（index.vue 的 openTask 会 `.catch`）；mock 同此契约。
const push = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
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
  })

  it('renders the scan bar and the app wall from the task dictionary', () => {
    const wrapper = mount(HomePage)
    // 扫码条：以 placeholder 做稳健断言（不依赖 SFC 组件名推断）
    expect(wrapper.find('input[placeholder^="扫描"]').exists()).toBe(true)
    // 应用墙渲染字典中的任务标签
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).toContain('报工')
    // 设备运维三件套（Plan 4）也渲染在墙上（含新增的查看报警入口）
    expect(wrapper.text()).toContain('报修')
    expect(wrapper.text()).toContain('点检')
    expect(wrapper.text()).toContain('查看报警')
  })

  it('shows an empty-state for "我的任务" until the backend personal-task facade lands', () => {
    const wrapper = mount(HomePage)
    expect(wrapper.text()).toContain('暂无分配给你的任务')
  })

  it('enables the ready equipment entries and navigates to their routes on click', async () => {
    const wrapper = mount(HomePage)

    const cases: Array<[string, string]> = [
      ['报修', '/equipment/repair'],
      ['点检', '/equipment/inspect'],
      ['查看报警', '/equipment/alarms'],
    ]

    for (const [label, route] of cases) {
      const btn = buttonByLabel(wrapper, label)
      // 字典已点亮（routeReady=true）→ 入口不再 disabled
      expect(btn.attributes('disabled')).toBeUndefined()
      push.mockClear()
      await btn.trigger('click')
      expect(push).toHaveBeenCalledWith(route)
    }
  })

  it('enables the four MES app-wall entries and navigates to their routes on click', async () => {
    const wrapper = mount(HomePage)
    const mesEntries: Array<[label: string, route: string]> = [
      ['报工', '/mes/report'],
      ['领料', '/mes/issue'],
      ['完工入库', '/mes/receipt'],
      ['工序执行', '/mes/operation'],
    ]
    for (const [label, route] of mesEntries) {
      const btn = buttonByLabel(wrapper, label)
      expect(btn.attributes('disabled')).toBeUndefined()
      await btn.trigger('click')
      expect(push).toHaveBeenCalledWith(route)
    }
    expect(push).toHaveBeenCalledTimes(mesEntries.length)
  })

  it('keeps the WMS app-wall entries disabled and does not navigate on click', async () => {
    const wrapper = mount(HomePage)
    const wmsLabels = ['收货入库', '拣货', '上架', '复核发货', '盘点']
    for (const label of wmsLabels) {
      const btn = buttonByLabel(wrapper, label)
      expect(btn.attributes('disabled')).toBeDefined()
      await btn.trigger('click')
    }
    expect(push).not.toHaveBeenCalled()
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
