import { mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import WorkerSelect from './WorkerSelect.vue'

const workerDirectory = vi.hoisted(() => ({
  workers: undefined as unknown as { value: Array<{ userId: string; displayName: string }> },
  workersPending: undefined as unknown as { value: boolean },
  workersError: undefined as unknown as { value: unknown },
  refresh: vi.fn(),
  filters: undefined as unknown as { keyword?: string },
}))

vi.mock('@/composables/master-data', async () => {
  const { reactive, shallowRef } = await import('vue')
  workerDirectory.workers = shallowRef([])
  workerDirectory.workersPending = shallowRef(false)
  workerDirectory.workersError = shallowRef()
  workerDirectory.filters = reactive({})
  return { useBusinessWorkers: () => workerDirectory }
})

const selectStubs = {
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<div><slot /></div>' },
  NvSelectValue: true,
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<div><slot /></div>' },
  NvInput: true,
}

beforeEach(() => {
  workerDirectory.workers.value = [{ userId: 'worker-planned', displayName: '计划技师' }]
  workerDirectory.filters.keyword = undefined
  workerDirectory.workersError.value = undefined
  workerDirectory.refresh.mockReset()
})

it('preserves a selected worker when the current search page no longer contains it', async () => {
  const wrapper = mount(WorkerSelect, {
    props: { modelValue: 'worker-planned', keepOutOfRange: true },
    global: {
      stubs: selectStubs,
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
      stubs: selectStubs,
    },
  })

  workerDirectory.workers.value = [{ userId: 'worker-other', displayName: '其他技师' }]
  await nextTick()

  expect(wrapper.emitted('update:modelValue')).toEqual([['']])
})

it('distinguishes empty search results, an empty directory and a failed directory request', async () => {
  workerDirectory.workers.value = []
  workerDirectory.filters.keyword = '李'
  const wrapper = mount(WorkerSelect, { global: { stubs: selectStubs } })
  expect(wrapper.text()).toContain('没有符合搜索条件的人员')
  await wrapper.get('button').trigger('click')
  expect(workerDirectory.filters.keyword).toBeUndefined()
  expect(wrapper.text()).toContain('人员目录暂无可选人员')
  workerDirectory.workersError.value = new Error('无法读取')
  await nextTick()
  expect(wrapper.text()).toContain('人员目录加载失败')
  await wrapper.get('button').trigger('click')
  expect(workerDirectory.refresh).toHaveBeenCalledOnce()
  wrapper.unmount()
})
