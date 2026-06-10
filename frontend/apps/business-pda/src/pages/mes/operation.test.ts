import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// --- composable mock: 2 operation tasks with different statuses ---
const completeTask = vi.fn(async () => {})
const startTask = vi.fn(async () => {})
const pauseTask = vi.fn(async () => {})
const resumeTask = vi.fn(async () => {})
const refresh = vi.fn(async () => {})

const filters = reactive({ keyword: undefined as string | undefined })

const tasks = [
  {
    operationTaskId: 'OP-1',
    workOrderId: 'WO-2026-0001',
    status: 'Running',
    operationSequence: 10,
    workCenterId: 'WC-A',
  },
  {
    operationTaskId: 'OP-2',
    workOrderId: 'WO-2026-0002',
    status: 'Ready',
    operationSequence: 20,
    workCenterId: 'WC-B',
  },
]

vi.mock('@/composables/useBusinessMes', () => ({
  useMesOperationTasks: () => ({
    filters,
    operationTasks: computed(() => tasks),
    total: computed(() => tasks.length),
    pending: ref(false),
    error: ref(null),
    refresh,
    startTask,
    pauseTask,
    resumeTask,
    completeTask,
    actionPending: ref(false),
  }),
}))

import OperationPage from './operation.vue'

describe('PDA MES operation execution page', () => {
  beforeEach(() => {
    completeTask.mockClear()
    startTask.mockClear()
    pauseTask.mockClear()
    resumeTask.mockClear()
    filters.keyword = undefined
  })

  it('renders the scan bar and an operation ListRow per task', () => {
    const wrapper = mount(OperationPage)
    expect(wrapper.find('input[placeholder^="扫"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('WO-2026-0001')
    expect(wrapper.text()).toContain('WO-2026-0002')
    // 工序序号可读呈现
    expect(wrapper.text()).toContain('工序 10')
  })

  it('sets filters.keyword when scanning', async () => {
    const wrapper = mount(OperationPage)
    const input = wrapper.get('input[placeholder^="扫"]')
    await input.setValue('WO-2026-0002')
    await input.trigger('keydown.enter')
    expect(filters.keyword).toBe('WO-2026-0002')
  })

  it('opens the action BottomSheet when a row is tapped', async () => {
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()
    // BottomSheet 内容 teleport 到 body
    expect(document.body.textContent).toContain('完成')
    wrapper.unmount()
  })

  it('completes a task only after explicit confirmation, calling completeTask with the id', async () => {
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()

    // 点"完成"——先进入二次确认，不立即调用
    const completeBtn = document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!
    completeBtn.click()
    await flushPromises()
    expect(completeTask).not.toHaveBeenCalled()

    // 确认后才调用
    const confirmBtn = document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!
    confirmBtn.click()
    await flushPromises()
    expect(completeTask).toHaveBeenCalledWith('OP-1')

    // 成功后显示 Result 成功文案
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('工序已完成')
    wrapper.unmount()
  })

  it('shows an error Result with a retry entry when an action fails', async () => {
    completeTask.mockRejectedValueOnce(new Error('boom'))
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('重试')
    wrapper.unmount()
  })
})
