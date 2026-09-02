import { mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import WorkerSelect from './WorkerSelect.vue'

const workerDirectory = vi.hoisted(() => ({
  workers: undefined as unknown as { value: Array<{ userId: string; displayName: string }> },
  workersPending: undefined as unknown as { value: boolean },
  filters: undefined as unknown as { keyword?: string },
}))

vi.mock('@/composables/useBusinessMasterData', async () => {
  const { reactive, shallowRef } = await import('vue')
  workerDirectory.workers = shallowRef([])
  workerDirectory.workersPending = shallowRef(false)
  workerDirectory.filters = reactive({})
  return { useBusinessWorkers: () => workerDirectory }
})

beforeEach(() => {
  workerDirectory.workers.value = [{ userId: 'worker-planned', displayName: '计划技师' }]
  workerDirectory.filters.keyword = undefined
})

it('preserves a selected worker when the current search page no longer contains it', async () => {
  const wrapper = mount(WorkerSelect, {
    props: { modelValue: 'worker-planned', keepOutOfRange: true },
    global: {
      stubs: {
        NvSelect: { template: '<div><slot /></div>' },
        // 不渲染 trigger 的 slot：这在 #2879 给 barrel 补 name **之前**是必需的——那时 `NvSelectValue`
        // 键打不中，真实 reka SelectValue 一旦渲染就会去 inject SelectRoot（已被 NvSelect stub 抹平）而抛错。
        // 补 name 后该前提已不成立。这两个用例只断言 watch(options) 的 emit、不需要真实下拉，绕法予以保留
        // （不依赖跨用例 stub 泄漏），但它已是历史遗留，见 #2879 跟进项。
        NvSelectTrigger: { template: '<div />' },
        NvSelectValue: true,
        NvSelectContent: { template: '<div><slot /></div>' },
        NvSelectItem: { template: '<div><slot /></div>' },
        NvInput: true,
      },
    },
  })

  workerDirectory.workers.value = [{ userId: 'worker-other', displayName: '其他技师' }]
  await nextTick()

  expect(wrapper.emitted('update:modelValue')).toBeUndefined()
})

it('clears a selected worker outside the current result set by default', async () => {
  const wrapper = mount(WorkerSelect, {
    props: { modelValue: 'worker-planned' },
    global: {
      stubs: {
        NvSelect: { template: '<div><slot /></div>' },
        // 不渲染 trigger 的 slot：这在 #2879 给 barrel 补 name **之前**是必需的——那时 `NvSelectValue`
        // 键打不中，真实 reka SelectValue 一旦渲染就会去 inject SelectRoot（已被 NvSelect stub 抹平）而抛错。
        // 补 name 后该前提已不成立。这两个用例只断言 watch(options) 的 emit、不需要真实下拉，绕法予以保留
        // （不依赖跨用例 stub 泄漏），但它已是历史遗留，见 #2879 跟进项。
        NvSelectTrigger: { template: '<div />' },
        NvSelectValue: true,
        NvSelectContent: { template: '<div><slot /></div>' },
        NvSelectItem: { template: '<div><slot /></div>' },
        NvInput: true,
      },
    },
  })

  workerDirectory.workers.value = [{ userId: 'worker-other', displayName: '其他技师' }]
  await nextTick()

  expect(wrapper.emitted('update:modelValue')).toEqual([['']])
})
