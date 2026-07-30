import { mount } from '@vue/test-utils'
import { NvPullRefresh } from '@nerv-iip/ui-mobile'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import WarehouseTaskExecutionView from './WarehouseTaskExecutionView.vue'

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
  blockReasons: [],
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
      status: 'Open',
      scopeKey: 'self:emp049',
      currentPrincipalId: 'emp049',
      scopeOptions: [
        { label: '我的任务', value: 'self:emp049' },
        { label: '仓储一组', value: 'team:TEAM-WMS-01' },
        { label: '一号仓库', value: 'site:SITE-001' },
      ],
    },
  })
}

describe('WarehouseTaskExecutionView', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    intersectionState.callback = undefined
    document.body.innerHTML = ''
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

    wrapper.getComponent(NvPullRefresh).vm.$emit('refresh')
    intersectionState.callback?.([{ isIntersecting: true }])
    await nextTick()

    expect(wrapper.emitted('refresh')).toHaveLength(1)
    expect(wrapper.emitted('loadMore')).toHaveLength(1)
    expect(wrapper.findComponent({ name: 'NvInfiniteList' }).exists()).toBe(false)
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

  it('执行中任务支持进度、异常、完成，并以快捷原因完成短拣', async () => {
    const wrapper = mountView()
    await wrapper.get('[data-task-no="PK-2026-0002"]').trigger('click')

    expect(document.querySelector('[data-testid="confirm-progress"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="report-exception"]')).not.toBeNull()
    expect(document.querySelector('[data-testid="confirm-complete"]')).not.toBeNull()

    const quantity = document.querySelector<HTMLInputElement>('[data-testid="executed-quantity"]')!
    quantity.value = '8'
    quantity.dispatchEvent(new Event('input', { bubbles: true }))
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
    expect(document.querySelector('[role="dialog"][data-state="open"]')).toBeNull()
  })

  it('上架任务不允许用差异原因绕过全量完成门禁', async () => {
    mountView([inProgressTask], 'putaway')
    document.querySelector<HTMLElement>('[data-task-no="PK-2026-0002"]')!.click()
    await nextTick()

    const quantity = document.querySelector<HTMLInputElement>('[data-testid="executed-quantity"]')!
    quantity.value = '8'
    quantity.dispatchEvent(new Event('input', { bubbles: true }))
    document.querySelector<HTMLButtonElement>('[data-testid="difference-short-stock"]')!.click()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await nextTick()

    expect(document.body.textContent).toContain('上架任务须完成全部计划数量')
  })
})
