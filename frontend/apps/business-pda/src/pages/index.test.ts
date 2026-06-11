import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// `openTask` chains `.catch` on the push result — return a resolved Promise.
const push = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
}))

import HomePage from './index.vue'

beforeEach(() => {
  push.mockClear()
})

describe('PDA home', () => {
  it('renders the scan bar and the app wall from the task dictionary', () => {
    const wrapper = mount(HomePage)
    // 扫码条：以 placeholder 做稳健断言（不依赖 SFC 组件名推断）
    expect(wrapper.find('input[placeholder^="扫描"]').exists()).toBe(true)
    // 应用墙渲染字典中的任务标签
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).toContain('报工')
  })

  it('shows an empty-state for "我的任务" until the backend personal-task facade lands', () => {
    const wrapper = mount(HomePage)
    expect(wrapper.text()).toContain('暂无分配给你的任务')
  })

  // On THIS branch only the WMS pages exist; MES/equipment kinds are routeReady:false.
  function findButton(wrapper: ReturnType<typeof mount>, label: string) {
    const btn = wrapper.findAll('button').find((b) => b.text().includes(label))
    if (!btn) throw new Error(`app-wall button not found: ${label}`)
    return btn
  }

  const WMS_ENTRIES: Array<[string, string]> = [
    ['收货入库', '/wms/inbound'],
    ['复核发货', '/wms/review'],
    ['拣货', '/wms/pick'],
    ['上架', '/wms/putaway'],
    ['盘点', '/wms/count'],
  ]

  it.each(WMS_ENTRIES)('enables the %s WMS entry and navigates to %s on click', async (label, route) => {
    const wrapper = mount(HomePage)
    const btn = findButton(wrapper, label)
    expect(btn.attributes('disabled')).toBeUndefined()
    await btn.trigger('click')
    expect(push).toHaveBeenCalledWith(route)
  })

  it.each(['报工', '领料', '完工入库', '工序执行', '报修', '点检'])(
    'keeps the not-yet-ready %s entry disabled and does not navigate on click',
    async (label) => {
      const wrapper = mount(HomePage)
      const btn = findButton(wrapper, label)
      expect(btn.attributes('disabled')).toBeDefined()
      await btn.trigger('click')
      expect(push).not.toHaveBeenCalled()
    },
  )

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
