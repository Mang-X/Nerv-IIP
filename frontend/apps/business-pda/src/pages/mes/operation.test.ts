import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// --- composable mock: 2 operation tasks with different statuses ---
type ActionOptions = { reasonCode?: string, idempotencyKey: string }
const completeTask = vi.fn(async (_id: string, _options: ActionOptions) => {})
const startTask = vi.fn(async (_id: string, _options: ActionOptions) => {})
const pauseTask = vi.fn(async (_id: string, _options: ActionOptions) => {})
const resumeTask = vi.fn(async (_id: string, _options: ActionOptions) => {})
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

    // 确认后才调用 —— 携带稳定逐操作幂等键
    const confirmBtn = document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!
    confirmBtn.click()
    await flushPromises()
    expect(completeTask).toHaveBeenCalledWith('OP-1', expect.objectContaining({ idempotencyKey: expect.any(String) }))
    expect(completeTask.mock.calls[0][1].idempotencyKey).toBeTruthy()

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

  it('reuses the SAME idempotencyKey on action retry; a different action initiation mints a new key', async () => {
    // 行 0 是 Running 工序，可执行「暂停」与「完成」
    completeTask.mockRejectedValueOnce(new Error('lost response'))
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()

    // 发起「完成」→ 进入二次确认（铸造稳定键）→ 确认 → 首次失败
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)

    // 不重新发起，直接重试该动作 → 复用同一 idempotencyKey
    wrapper.get('[data-testid="retry-action"]').trigger('click')
    await flushPromises()

    expect(completeTask).toHaveBeenCalledTimes(2)
    const firstKey = completeTask.mock.calls[0][1].idempotencyKey
    const retryKey = completeTask.mock.calls[1][1].idempotencyKey
    expect(firstKey).toBeTruthy()
    expect(retryKey).toBe(firstKey)

    // 成功后继续 → 重新打开面板并发起新动作（暂停）→ 新键
    wrapper.findAll('button').find((b) => b.text() === '继续')!.trigger('click')
    await flushPromises()
    const rows2 = wrapper.findAll('[data-row]')
    await rows2[0].trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-pause"]')!.click()
    await flushPromises()

    expect(pauseTask).toHaveBeenCalledTimes(1)
    const pauseKey = pauseTask.mock.calls[0][1].idempotencyKey
    expect(pauseKey).toBeTruthy()
    expect(pauseKey).not.toBe(firstKey)
    wrapper.unmount()
  })
})
