import { mount } from '@vue/test-utils'
import { NvBottomSheet, NvNumberKeyboard, NvScanBar } from '@nerv-iip/ui-mobile'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import WarehouseTaskExecutionView from './WarehouseTaskExecutionView.vue'
import TaskListShell from '@/components/task-list/TaskListShell.vue'

const intersectionState = vi.hoisted(() => ({
  callback: undefined as ((entries: Array<{ isIntersecting: boolean }>) => void) | undefined,
}))

vi.mock('@vueuse/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@vueuse/core')>()
  return {
    ...actual,
    useIntersectionObserver: vi.fn(
      (_target: unknown, callback: (entries: Array<{ isIntersecting: boolean }>) => void) => {
        intersectionState.callback = callback
        return { stop: vi.fn() }
      },
    ),
  }
})

const openTask = {
  warehouseTaskId: 'task-open',
  taskNo: 'PK-2026-0001',
  sourceOrderNo: 'OB-2026-0001',
  skuCode: 'SKU-A',
  lotNo: 'LOT-01',
  uomCode: 'EA',
  fromLocationCode: 'A-01',
  toLocationCode: 'OUT-01',
  plannedQuantity: 10,
  executedQuantity: 0,
  status: 'Open',
  version: 1,
  assignedOperatorUserId: 'emp049',
  allowedActions: ['start'],
  blockReasons: [] as string[],
}

const inProgressTask = {
  ...openTask,
  warehouseTaskId: 'task-running',
  taskNo: 'PK-2026-0002',
  executedQuantity: 6,
  status: 'InProgress',
  version: 3,
  allowedActions: ['progress', 'exception', 'complete'],
}

const completedTask = {
  ...openTask,
  warehouseTaskId: 'task-done',
  taskNo: 'PK-2026-0003',
  executedQuantity: 10,
  status: 'Completed',
  version: 4,
  allowedActions: [],
}

function mountView(
  tasks = [openTask, inProgressTask, completedTask],
  taskType: 'picking' | 'putaway' = 'picking',
  overrides: Record<string, unknown> = {},
) {
  return mount(WarehouseTaskExecutionView, {
    attachTo: document.body,
    props: {
      title: '拣货',
      taskType,
      tasks,
      total: tasks.length + 1,
      pending: false,
      refreshing: false,
      loadingMore: false,
      candidateReady: true,
      status: 'Open',
      scopeKey: 'self:emp049',
      currentPrincipalId: 'emp049',
      scopeOptions: [
        { label: '我的任务', value: 'self:emp049' },
        { label: '仓储一组', value: 'team:TEAM-WMS-01' },
        { label: '一号仓库', value: 'site:SITE-001' },
      ],
      ...overrides,
    },
  })
}

describe('WarehouseTaskExecutionView', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    intersectionState.callback = undefined
    document.body.innerHTML = ''
    sessionStorage.clear()
  })

  it('显示后端任务事实与范围、状态筛选，不暴露 GUID 或原始状态码', () => {
    const wrapper = mountView()
    const text = wrapper.text()

    expect(text).toContain('PK-2026-0001')
    expect(text).toContain('SKU-A')
    expect(text).toContain('LOT-01')
    expect(text).toContain('A-01')
    expect(text).toContain('OUT-01')
    expect(text).toContain('0 / 10 EA')
    expect(text).toContain('待执行')
    expect(text).toContain('执行中')
    expect(text).toContain('已完成')
    expect(text).not.toContain('task-open')
    expect(text).not.toContain('InProgress')
    expect(wrapper.findComponent(NvScanBar).props('placeholder')).toBe('扫描当前范围库位候选')
  })

  it('完整翻译服务端阻塞原因，不把不同原因复用成同一句兜底', () => {
    const wrapper = mountView([
      {
        ...completedTask,
        blockReasons: [
          'TASK_TERMINAL',
          'TASK_NOT_ASSIGNED_TO_WORK_POOL',
          'TASK_ASSIGNED_TO_ANOTHER_OPERATOR',
          'TASK_EXECUTION_CLAIMED_BY_WCS',
          'TASK_EXECUTION_CLAIMED_BY_ANOTHER_OPERATOR',
          'TASK_EXECUTION_NOT_CLAIMED',
        ],
      },
    ])

    expect(wrapper.text()).toContain('任务已结束，不可继续操作')
    expect(wrapper.text()).toContain('任务尚未分配作业池')
    expect(wrapper.text()).toContain('任务已派给其他人员')
    expect(wrapper.text()).toContain('任务已由 WCS 接管')
    expect(wrapper.text()).toContain('任务正由其他人员执行')
    expect(wrapper.text()).toContain('任务尚未开始执行')
  })

  it('站点或作业池中的他人派工不得误标为我的任务', () => {
    const wrapper = mountView([
      {
        ...openTask,
        assignedOperatorUserId: 'emp012',
      },
    ])
    const text = wrapper.text()

    expect(text).toContain('已派给他人')
    expect(text).not.toContain('已派给我')
  })

  it('下拉刷新与滑到底部加载更多均通过事件交给页面编排', async () => {
    const wrapper = mountView()

    wrapper.getComponent(TaskListShell).vm.$emit('refresh')
    wrapper.getComponent(TaskListShell).vm.$emit('loadMore')
    await nextTick()

    expect(wrapper.emitted('refresh')).toHaveLength(1)
    expect(wrapper.emitted('loadMore')).toHaveLength(1)
    expect(wrapper.findComponent(TaskListShell).exists()).toBe(true)
  })

  it('已有任务时独立呈现真实动作错误，绝不冒充下一页失败', () => {
    const wrapper = mountView([openTask], 'picking', {
      actionError: { message: '任务状态已被其他操作更新，请刷新后重试' },
    })

    expect(wrapper.get('[data-testid="action-error-banner"]').text()).toContain(
      '任务状态已被其他操作更新，请刷新后重试',
    )
    expect(wrapper.text()).not.toContain('下一页加载失败')
  })

  it('下一页失败只显示分页错误文案，不显示动作错误', () => {
    const wrapper = mountView([openTask], 'picking', {
      total: 40,
      loadMoreError: { message: '仓储任务下一页读取失败' },
    })

    expect(wrapper.get('[data-testid="task-list-load-error"]').text()).toContain('下一页加载失败')
    expect(wrapper.find('[data-testid="action-error-banner"]').exists()).toBe(false)
  })

  it('由 TaskListShell 托管全部筛选、元数据、分页错误与实际滚动位置恢复', async () => {
    sessionStorage.setItem(
      'nerv-iip.business-pda.task-list.wms-picking-tasks',
      JSON.stringify({
        filters: {
          scopeKey: 'team:TEAM-WMS-01',
          status: 'InProgress',
          keyword: 'PK-RESTORED',
          locationCode: 'A-09',
          lotNo: 'LOT-09',
          candidateSearchKeyword: '候选-09',
        },
        scrollTop: 184,
      }),
    )
    const loadMoreError = new Error('next page failed')
    const wrapper = mountView([openTask], 'picking', {
      keyword: 'PK-CURRENT',
      locationCode: 'A-01',
      lotNo: 'LOT-01',
      candidateSearchKeyword: '候选-01',
      updatedAt: '2026-08-01T08:00:00.000Z',
      loadMoreError,
    })
    await nextTick()
    await nextTick()
    await nextTick()
    await nextTick()

    const shell = wrapper.getComponent(TaskListShell)
    expect(shell.props('showMeta')).toBe(true)
    expect(shell.props('updatedAt')).toBe('2026-08-01T08:00:00.000Z')
    expect(shell.props('loadMoreError')).toBe(loadMoreError)
    expect(shell.props('filterState')).toEqual({
      scopeKey: 'self:emp049',
      status: 'Open',
      keyword: 'PK-CURRENT',
      locationCode: 'A-01',
      lotNo: 'LOT-01',
      candidateSearchKeyword: '候选-01',
    })
    expect(shell.find('[data-testid="wms-task-filters"]').exists()).toBe(true)
    expect((wrapper.get('.nv-m-pr-scroll').element as HTMLElement).scrollTop).toBe(184)
    expect(wrapper.emitted('update:scopeKey')?.at(-1)).toEqual(['team:TEAM-WMS-01'])
    expect(wrapper.emitted('update:status')?.at(-1)).toEqual(['InProgress'])
    expect(wrapper.emitted('update:keyword')?.at(-1)).toEqual(['PK-RESTORED'])
    expect(wrapper.emitted('update:locationCode')?.at(-1)).toEqual(['A-09'])
    expect(wrapper.emitted('update:lotNo')?.at(-1)).toEqual(['LOT-09'])
    expect(wrapper.emitted('update:candidateSearchKeyword')?.at(-1)).toEqual(['候选-09'])
  })

  it('终态任务不可进入操作，待执行任务只提供开始', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-task-no="PK-2026-0003"]').trigger('click')
    expect(document.querySelector('[role="dialog"]')).toBeNull()

    await wrapper.get('[data-task-no="PK-2026-0001"]').trigger('click')
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('开始拣货')
    expect(document.querySelector('[data-testid="confirm-progress"]')).toBeNull()
    expect(document.querySelector('[data-testid="confirm-complete"]')).toBeNull()
  })

  it('请求发出后保留动作抽屉，结果未知时冻结原意图并支持核实与原样重试', async () => {
    const wrapper = mountView([openTask])
    await wrapper.get('[data-task-no="PK-2026-0001"]').trigger('click')
    const start = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      button.textContent?.includes('开始拣货'),
    )!

    start.click()
    await wrapper.setProps({ actionPending: true })

    expect(document.querySelector('[role="dialog"][data-state="open"]')).not.toBeNull()
    expect(wrapper.emitted('execute')?.at(-1)).toEqual([
      {
        action: 'start',
        task: openTask,
      },
    ])

    await wrapper.setProps({ actionPending: false, actionUnconfirmed: true })
    const retry = document.querySelector<HTMLButtonElement>('[data-testid="retry-frozen-action"]')!
    const verify = document.querySelector<HTMLButtonElement>(
      '[data-testid="verify-frozen-action"]',
    )!
    expect(retry.textContent).toContain('按原内容重试')
    expect(verify.textContent).toContain('刷新核实')

    retry.click()
    verify.click()
    await nextTick()

    expect(wrapper.emitted('execute')?.at(-1)).toEqual([
      {
        action: 'start',
        task: openTask,
      },
    ])
    expect(wrapper.emitted('verify')).toHaveLength(1)

    wrapper.findComponent(NvBottomSheet).vm.$emit('update:open', false)
    await nextTick()
    expect(document.querySelector('[role="dialog"][data-state="open"]')).not.toBeNull()

    await wrapper.setProps({ actionUnconfirmed: false, actionConfirmedSequence: 1 })
    expect(document.querySelector('[role="dialog"][data-state="open"]')).toBeNull()
  })

  it('执行中任务支持进度、异常、完成，并以快捷原因完成短拣', async () => {
    const wrapper = mountView()
    await wrapper.get('[data-task-no="PK-2026-0002"]').trigger('click')

    expect(document.querySelector('[data-testid="confirm-progress"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="report-exception"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="confirm-complete"]')).not.toBeNull()

    document.querySelector<HTMLElement>('[data-testid="executed-quantity"]')!.click()
    await nextTick()
    expect(wrapper.findComponent(NvNumberKeyboard).props('show')).toBe(true)
    wrapper.findComponent(NvNumberKeyboard).vm.$emit('update:modelValue', '8')
    await nextTick()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await nextTick()
    expect(document.body.textContent).toContain('请选择差异原因')

    document.querySelector<HTMLButtonElement>('[data-testid="difference-short-stock"]')!.click()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await nextTick()

    expect(wrapper.emitted('execute')?.at(-1)).toEqual([
      {
        action: 'complete',
        task: inProgressTask,
        executedQuantity: 8,
        reason: '库位缺货',
      },
    ])
    await wrapper.setProps({ actionConfirmedSequence: 1 })
    expect(document.querySelector('[role="dialog"][data-state="open"]')).toBeNull()
  })

  it('上架任务不允许用差异原因绕过全量完成门禁', async () => {
    const wrapper = mountView([inProgressTask], 'putaway')
    document.querySelector<HTMLElement>('[data-task-no="PK-2026-0002"]')!.click()
    await nextTick()

    document.querySelector<HTMLElement>('[data-testid="executed-quantity"]')!.click()
    await nextTick()
    wrapper.findComponent(NvNumberKeyboard).vm.$emit('update:modelValue', '8')
    await nextTick()
    document.querySelector<HTMLButtonElement>('[data-testid="difference-short-stock"]')!.click()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await nextTick()

    expect(document.body.textContent).toContain('上架任务须完成全部计划数量')
  })
})
