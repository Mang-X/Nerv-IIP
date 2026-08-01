import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

const currentPath = ref('/tasks')
const push = vi.fn(() => Promise.resolve())

vi.mock('vue-router', () => ({
  useRoute: () => ({
    get path() {
      return currentPath.value
    },
  }),
  useRouter: () => ({ push }),
}))

import PdaBottomNavigation from './PdaBottomNavigation.vue'

describe('PdaBottomNavigation', () => {
  it('renders the fixed four entrances once and marks the current route', async () => {
    const wrapper = mount(PdaBottomNavigation)

    expect(wrapper.findAll('button').map((button) => button.text())).toEqual([
      '工作台',
      '任务',
      '扫码',
      '我的',
    ])
    expect(wrapper.get('button[aria-current="page"]').text()).toBe('任务')

    await wrapper.findAll('button')[2]!.trigger('click')
    expect(push).toHaveBeenCalledWith('/scan')
  })

  it('keeps a child work route under the workbench entrance', () => {
    currentPath.value = '/wms/inbound'
    const wrapper = mount(PdaBottomNavigation)

    expect(wrapper.get('button[aria-current="page"]').text()).toBe('工作台')
  })
})
