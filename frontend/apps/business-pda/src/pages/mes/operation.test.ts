import { RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

// --- composable mock: 2 operation tasks with different statuses ---
type ActionOptions = { reasonCode?: string; idempotencyKey: string }
const completeTask = vi.fn(async (_id: string, _options: ActionOptions) => {})
const startTask = vi.fn(async (_id: string, _options: ActionOptions) => {})
const pauseTask = vi.fn(async (_id: string, _options: ActionOptions) => {})
const resumeTask = vi.fn(async (_id: string, _options: ActionOptions) => {})
const refresh = vi.fn(async () => {})
const refreshSops = vi.fn()
const createSopFileDownloadGrant = vi.fn()

const filters = reactive({ keyword: undefined as string | undefined })
const tasksErrorRef = ref<unknown>(null)
const sopsErrorRef = ref<unknown>(null)

const defaultTasks = [
  {
    operationTaskId: 'OP-1',
    workOrderId: 'WO-2026-0001',
    status: 'Running',
    operationSequence: 10,
    operationCode: 'OP-CODE-1',
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
const operationTasksRef = ref<(typeof defaultTasks)[number][]>(defaultTasks)
const currentSopsRef = ref<Array<Record<string, unknown>>>([])

vi.mock('@/composables/useBusinessMes', () => ({
  useMesOperationTasks: () => ({
    filters,
    operationTasks: computed(() => operationTasksRef.value),
    total: computed(() => operationTasksRef.value.length),
    pending: ref(false),
    error: tasksErrorRef,
    refresh,
    startTask,
    pauseTask,
    resumeTask,
    completeTask,
    actionPending: ref(false),
  }),
  useMesCurrentOperationSops: () => ({
    filters: {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      operationCode: '',
      workCenterCode: '',
    },
    currentSops: currentSopsRef,
    pending: ref(false),
    error: sopsErrorRef,
    refresh: refreshSops,
    createSopFileDownloadGrant,
  }),
}))

import OperationPage from './operation.vue'

describe('PDA MES operation execution page', () => {
  beforeEach(() => {
    completeTask.mockClear()
    startTask.mockClear()
    pauseTask.mockClear()
    resumeTask.mockClear()
    refreshSops.mockClear()
    tasksErrorRef.value = null
    sopsErrorRef.value = null
    operationTasksRef.value = defaultTasks
    currentSopsRef.value = []
    createSopFileDownloadGrant.mockClear()
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
    expect(completeTask).toHaveBeenCalledWith(
      'OP-1',
      expect.objectContaining({ idempotencyKey: expect.any(String) }),
    )
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
    wrapper
      .findAll('button')
      .find((b) => b.text() === '继续')!
      .trigger('click')
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

  // P1：SOP 查询失败也要有可操作错误态 + 重试入口（#814 所有 facade）。
  it('SOP 查询超时：SOP 区显示可操作错误面板 + 重试调 refresh', async () => {
    sopsErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click') // OP-1 绑定了标准工序
    await flushPromises()
    const panel = document.body.querySelector<HTMLElement>('[data-testid="sops-error"]')!
    expect(panel).toBeTruthy()
    expect(panel.textContent).toContain('网络超时，请检查连接后重试')
    panel.querySelector<HTMLElement>('[data-testid="retry-list"]')!.click()
    expect(refreshSops).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  // P2：加载失败时空态与错误态互斥，不把网络错误误报成"暂无"。
  it('工序列表加载失败时不显示"暂无工序任务"空态', () => {
    operationTasksRef.value = []
    tasksErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(OperationPage)
    expect(wrapper.find('[data-testid="operation-tasks-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无工序任务')
    wrapper.unmount()
  })

  // P1：SOP 打开文件失败（超时/离线）时不隐藏 SOP 列表——保留"查看SOP"作为重试入口。
  it('打开 SOP 失败（超时）时保留 SOP 列表与"查看SOP"按钮以便重试', async () => {
    currentSopsRef.value = [
      { fileId: 'F1', fileName: 'SOP-1', documentNumber: 'D1', revision: 'A', effectiveDate: null },
    ]
    createSopFileDownloadGrant.mockRejectedValueOnce(new Error('网络超时，请检查连接后重试'))
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const viewBtn = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      b.textContent?.includes('查看SOP'),
    )!
    expect(viewBtn).toBeTruthy()
    viewBtn.click()
    await flushPromises()
    // 打开失败文案出现
    expect(document.body.querySelector('[data-testid="sop-file-error"]')?.textContent).toContain(
      '网络超时，请检查连接后重试',
    )
    // 且 SOP 列表与"查看SOP"按钮仍在（可再次点击重试），不被错误文本隐藏
    expect(
      [...document.body.querySelectorAll('button')].some((b) => b.textContent?.includes('查看SOP')),
    ).toBe(true)
    wrapper.unmount()
  })
})
