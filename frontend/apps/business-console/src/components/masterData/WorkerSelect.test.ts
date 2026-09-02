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
        NvSelectTrigger: { template: '<div><slot /></div>' },
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
        NvSelectTrigger: { template: '<div><slot /></div>' },
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
