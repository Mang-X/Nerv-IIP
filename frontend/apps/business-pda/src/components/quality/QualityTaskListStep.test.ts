import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import { NvScanBar } from '@nerv-iip/ui-mobile'
import { flushPromises, mount } from '@vue/test-utils'
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
    props: { tasks, total: tasks.length, loaded: tasks.length, hasMore: false, pending: false, error: null },
  })
}

describe('QualityTaskListStep', () => {
  it('scan direct: an exact/unique source-document or SKU hit auto-selects the task', async () => {
    const wrapper = mountList([
      task({ inspectionTaskId: 'T1', sourceDocumentId: 'RCV-1001', skuCode: 'SKU-A', dueAtUtc: FUTURE }),
      task({ inspectionTaskId: 'T2', sourceType: 'final', sourceDocumentId: 'WO-2', skuCode: 'SKU-B', dueAtUtc: FUTURE }),
    ])
    await wrapper.findComponent(NvScanBar).vm.$emit('scan', 'RCV-1001')
    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({ inspectionTaskId: 'T1' })
  })

  it('scan direct: cross-page hit loads all then auto-selects the task', async () => {
    // 目标任务在未加载分页（loaded 集合无命中，hasMore=true）→ loadAll 后跨页直达。
    const target = task({ inspectionTaskId: 'T99', sourceDocumentId: 'RCV-9999', skuCode: 'SKU-Z', dueAtUtc: FUTURE })
    const loaded = [task({ inspectionTaskId: 'T1', sourceDocumentId: 'RCV-1', dueAtUtc: FUTURE })]
    const loadAll = vi.fn().mockResolvedValue([...loaded, target])
    const wrapper = mount(QualityTaskListStep, {
      props: { tasks: loaded, total: 2, loaded: 1, hasMore: true, pending: false, error: null, loadAll },
    })
    await wrapper.findComponent(NvScanBar).vm.$emit('scan', 'RCV-9999')
    await flushPromises()
    expect(loadAll).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({ inspectionTaskId: 'T99' })
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
})
