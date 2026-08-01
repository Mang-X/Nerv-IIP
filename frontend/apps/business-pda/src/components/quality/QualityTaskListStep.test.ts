import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import { NvListRow, NvMobileDropdownMenuItem, NvScanBar } from '@nerv-iip/ui-mobile'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { describe, expect, it, vi } from 'vitest'
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

  it('scan direct: an exact/unique source-document or SKU hit auto-selects the task', async () => {
    const wrapper = mountList([
      task({
        inspectionTaskId: 'T1',
        sourceDocumentId: 'RCV-1001',
        skuCode: 'SKU-A',
        dueAtUtc: FUTURE,
      }),
      task({
        inspectionTaskId: 'T2',
        sourceType: 'final',
        sourceDocumentId: 'WO-2',
        skuCode: 'SKU-B',
        dueAtUtc: FUTURE,
      }),
    ])
    await wrapper.findComponent(NvScanBar).vm.$emit('scan', 'RCV-1001')
    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({ inspectionTaskId: 'T1' })
  })

  it('keeps status visible and emits server-backed keyword/source/status filters', async () => {
    const wrapper = mountList([task({ inspectionTaskId: 'T1' })])
    const status = wrapper.findAllComponents(NvMobileDropdownMenuItem)[0]

    expect(status?.props('title')).toBe('任务状态')
    status?.vm.$emit('update:modelValue', 'in-progress')
    await wrapper.findComponent(NvScanBar).vm.$emit('scan', 'WO-9001')
    await wrapper.get('[data-testid="chip-operation"]').trigger('click')

    expect(wrapper.emitted('update:status')?.at(-1)).toEqual(['in-progress'])
    expect(wrapper.emitted('update:keyword')?.at(-1)).toEqual(['WO-9001'])
    expect(wrapper.emitted('update:sourceType')?.at(-1)).toEqual(['operation'])
  })

  it('scan direct: cross-page hit loads all then auto-selects the task', async () => {
    // 目标任务在未加载分页（loaded 集合无命中，hasMore=true）→ loadAll 后跨页直达。
    const target = task({
      inspectionTaskId: 'T99',
      sourceDocumentId: 'RCV-9999',
      skuCode: 'SKU-Z',
      dueAtUtc: FUTURE,
    })
    const loaded = [task({ inspectionTaskId: 'T1', sourceDocumentId: 'RCV-1', dueAtUtc: FUTURE })]
    const loadAll = vi.fn().mockResolvedValue([...loaded, target])
    const wrapper = mount(QualityTaskListStep, {
      props: {
        tasks: loaded,
        total: 2,
        loaded: 1,
        hasMore: true,
        pending: false,
        error: null,
        loadAll,
      },
    })
    await wrapper.findComponent(NvScanBar).vm.$emit('scan', 'RCV-9999')
    await flushPromises()
    expect(loadAll).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({ inspectionTaskId: 'T99' })
  })

  it('scan direct: a current-page match plus a later-page match is not globally unique → no select', async () => {
    // 首页已有 1 个同 SKU 命中，后续页还有另一个同 SKU 命中 → 全量下非唯一，不得误选首页任务。
    const loaded = [task({ inspectionTaskId: 'T1', sourceDocumentId: 'RCV-1', skuCode: 'SKU-DUP' })]
    const loadAll = vi
      .fn()
      .mockResolvedValue([
        ...loaded,
        task({ inspectionTaskId: 'T2', sourceDocumentId: 'RCV-2', skuCode: 'SKU-DUP' }),
      ])
    const wrapper = mount(QualityTaskListStep, {
      props: {
        tasks: loaded,
        total: 2,
        loaded: 1,
        hasMore: true,
        pending: false,
        error: null,
        loadAll,
      },
    })
    await wrapper.findComponent(NvScanBar).vm.$emit('scan', 'SKU-DUP')
    await flushPromises()
    expect(loadAll).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('select')).toBeUndefined()
  })

  it('scan without a unique hit filters instead of navigating', async () => {
    const wrapper = mountList([
      task({ inspectionTaskId: 'T1', sourceDocumentId: 'RCV-1', skuCode: 'SHARED' }),
      task({ inspectionTaskId: 'T2', sourceDocumentId: 'RCV-2', skuCode: 'SHARED' }),
    ])
    await wrapper.findComponent(NvScanBar).vm.$emit('scan', 'SHARED')
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
