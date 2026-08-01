import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import { NvListRow, NvMobileDropdownMenuItem, NvScanBar } from '@nerv-iip/ui-mobile'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { describe, expect, it } from 'vitest'
import QualityTaskListStep from './QualityTaskListStep.vue'

type Task = BusinessConsoleQualityInspectionTaskItem

const PAST = new Date(Date.now() - 3600_000).toISOString()
const FUTURE = new Date(Date.now() + 3600_000).toISOString()

function task(over: Partial<Task> = {}): Task {
  return {
    inspectionTaskId: 'T?',
    inspectionPlanId: 'P1',
    sourceType: 'receiving',
    sourceService: 'wms',
    sourceDocumentId: 'RCV-1',
    skuCode: 'SKU-A',
    quantity: 10,
    uomCode: 'pcs',
    status: 'pending',
    ...over,
  }
}

function mountList(tasks: Task[]) {
  return mount(QualityTaskListStep, {
    props: {
      tasks,
      total: tasks.length,
      loaded: tasks.length,
      hasMore: false,
      pending: false,
      error: null,
      scope: '当前登录组织 / 当前业务环境',
      updatedAt: '2026-07-28T10:20:30.000Z',
    },
  })
}

describe('QualityTaskListStep', () => {
  it('shows scope, source, counts, and stable successful-response time', () => {
    const wrapper = mountList([task({ inspectionTaskId: 'T1' })])
    expect(wrapper.text()).toContain('范围：当前登录组织 / 当前业务环境')
    expect(wrapper.text()).toContain('已加载 1 / 共 1')
    expect(wrapper.text()).toContain('更新时间（最近成功响应）：2026/7/28 18:20')
    expect(wrapper.text()).toContain('质检待检任务服务（当前账号 Self 范围，状态：待检）')
  })

  it('passes the independent refresh lifecycle to the shared task-list shell', () => {
    const wrapper = mount(QualityTaskListStep, {
      props: {
        tasks: [task({ inspectionTaskId: 'T1' })],
        total: 1,
        loaded: 1,
        hasMore: false,
        pending: false,
        refreshing: true,
        error: null,
      },
    })

    expect(wrapper.getComponent({ name: 'TaskListShell' }).props('refreshing')).toBe(true)
  })

  it('renders a retryable response failure instead of a business empty state', () => {
    const wrapper = mount(QualityTaskListStep, {
      props: {
        tasks: [],
        total: 0,
        loaded: 0,
        hasMore: false,
        pending: false,
        error: null,
        hasSuccessfulResponse: false,
        hasFailedResponse: true,
        scope: '当前登录组织 / 当前业务环境',
      },
    })

    expect(wrapper.get('[data-testid="list-failure-explanation"]').text()).toContain(
      '质检待检任务服务未成功返回，请刷新重试。',
    )
    expect(wrapper.find('[data-testid="tasks-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无待检任务')
  })

  it('keeps all filters server-backed, including source service and scan keyword', async () => {
    const wrapper = mountList([task({ inspectionTaskId: 'T1' })])
    const dropdowns = wrapper.findAllComponents(NvMobileDropdownMenuItem)
    const status = dropdowns[0]
    const sourceService = dropdowns.find((item) => item.props('title') === '来源服务')

    expect(status?.props('title')).toBe('任务状态')
    status?.vm.$emit('update:modelValue', 'in-progress')
    sourceService?.vm.$emit('update:modelValue', 'mes')
    await wrapper.findComponent(NvScanBar).vm.$emit('scan', 'WO-9001')
    await wrapper.get('[data-testid="chip-operation"]').trigger('click')

    expect(wrapper.emitted('update:status')?.at(-1)).toEqual(['in-progress'])
    expect(wrapper.emitted('update:keyword')?.at(-1)).toEqual(['WO-9001'])
    expect(wrapper.emitted('update:sourceType')?.at(-1)).toEqual(['operation'])
    expect(wrapper.emitted('update:sourceService')?.at(-1)).toEqual(['mes'])
    expect(wrapper.emitted('select')).toBeUndefined()
  })

  it('labels scanning as server-side filtering rather than direct navigation', () => {
    const wrapper = mountList([task({ inspectionTaskId: 'T1' })])

    expect(wrapper.findComponent(NvScanBar).props('placeholder')).toBe(
      '扫描或输入来源单据 / SKU 以筛选',
    )
  })

  it('never loads all pages or navigates from a client-only scan match', async () => {
    const wrapper = mountList([
      task({ inspectionTaskId: 'T1', sourceDocumentId: 'RCV-1001', skuCode: 'SKU-A' }),
    ])

    await wrapper.findComponent(NvScanBar).vm.$emit('scan', 'RCV-1001')

    expect(wrapper.emitted('update:keyword')?.at(-1)).toEqual(['RCV-1001'])
    expect(wrapper.emitted('select')).toBeUndefined()
  })

  it('sorts overdue tasks first and tags them 超期 (reactive clock)', () => {
    const wrapper = mountList([
      task({ inspectionTaskId: 'FUTURE', sourceDocumentId: 'RCV-F', dueAtUtc: FUTURE }),
      task({ inspectionTaskId: 'OVERDUE', sourceDocumentId: 'RCV-O', dueAtUtc: PAST }),
    ])
    const rows = wrapper.findAll('[data-testid="task-row"]')
    expect(rows[0].text()).toContain('RCV-O') // overdue first
    expect(wrapper.find('[data-testid="overdue-OVERDUE"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="overdue-FUTURE"]').exists()).toBe(false)
  })

  it('exposes each task stable id on its rendered row for response identity evidence', () => {
    const wrapper = mountList([
      task({ inspectionTaskId: 'TASK-IDENTITY-1', sourceDocumentId: 'RCV-IDENTITY-1' }),
    ])

    expect(wrapper.get('[data-testid="task-row"]').attributes('data-task-id')).toBe(
      'TASK-IDENTITY-1',
    )
  })

  it('shows every stable blocker on real task rows and keeps all blocked rows non-selectable', async () => {
    const wrapper = mountList([
      task({
        inspectionTaskId: 'OTHER-INSPECTOR',
        allowedActions: [],
        blockReasons: ['task-assigned-to-another-inspector'],
      }),
      task({
        inspectionTaskId: 'ALREADY-CLAIMED',
        status: 'in-progress',
        allowedActions: [],
        blockReasons: ['task-already-claimed'],
      }),
      task({
        inspectionTaskId: 'OUTSIDE-SCOPE',
        allowedActions: [],
        blockReasons: ['task-outside-selected-work-scope'],
      }),
      task({
        inspectionTaskId: 'OTHER-TEAM',
        allowedActions: [],
        blockReasons: ['task-assigned-to-another-team'],
      }),
    ])

    const text = wrapper.text()
    expect(text).toContain('任务已派给其他检验员，无法领取。')
    expect(text).toContain('任务已由其他检验员领取。')
    expect(text).toContain('任务不在当前工作范围内，无法领取。')
    expect(text).toContain('任务已派给其他班组，无法领取。')
    expect(text).toContain('待领取')
    expect(text).toContain('进行中')

    for (const row of wrapper.findAllComponents(NvListRow)) {
      row.vm.$emit('select')
    }
    await nextTick()
    expect(wrapper.emitted('select')).toBeUndefined()
  })
})
