import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

const executeTask = vi.fn()
const refresh = vi.fn()
const loadMore = vi.fn()
const candidateState = vi.hoisted(() => ({ refresh: vi.fn(async () => {}) }))
const scopeKey = shallowRef('self:emp049')
const filters = reactive({
  status: 'Open' as string | undefined,
  keyword: undefined as string | undefined,
  locationCode: undefined as string | undefined,
})
const task = {
  warehouseTaskId: 'task-1',
  taskNo: 'PK-2026-0001',
  status: 'Open',
  version: 1,
  allowedActions: ['start'],
}

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsPicking: () => ({
    filters,
    scopeKey,
    scopeOptions: computed(() => [{ label: '我的任务', value: 'self:emp049' }]),
    tasks: computed(() => [task]),
    total: computed(() => 2),
    pending: shallowRef(false),
    error: shallowRef(),
    refreshing: shallowRef(false),
    loadingMore: shallowRef(false),
    actionPending: shallowRef(false),
    refresh,
    loadMore,
    executeTask,
  }),
}))
vi.mock('@/composables/useWmsOperationalCandidates', async () => {
  const { shallowRef } = await import('vue')
  return {
    useWmsOperationalCandidates: () => ({
      locationOptions: shallowRef([]),
      lotOptions: shallowRef([]),
      ready: shallowRef(true),
      searchKeyword: shallowRef(''),
      scanOverrides: shallowRef({}),
      sourceLabel: shallowRef('当前范围仓储作业记录候选'),
      asOfUtc: shallowRef(),
      freshnessUtc: shallowRef(),
      truncated: shallowRef(false),
      pending: shallowRef(false),
      error: shallowRef(),
      refresh: candidateState.refresh,
    }),
  }
})

import PickPage from './pick.vue'

describe('WMS 拣货作业页', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    filters.status = 'Open'
    filters.keyword = undefined
    filters.locationCode = undefined
    scopeKey.value = 'self:emp049'
  })

  it('把范围、筛选、分页与任务交给移动作业视图', () => {
    const wrapper = mount(PickPage, {
      global: {
        stubs: {
          WarehouseTaskExecutionView: {
            props: ['title', 'taskType', 'tasks', 'total', 'status', 'scopeKey', 'scopeOptions'],
            template:
              '<div data-testid="execution-view">{{ title }}|{{ taskType }}|{{ total }}|{{ scopeKey }}|{{ status }}|{{ tasks[0].taskNo }}</div>',
          },
        },
      },
    })

    expect(wrapper.get('[data-testid="execution-view"]').text()).toContain(
      '拣货|picking|2|self:emp049|Open|PK-2026-0001',
    )
  })

  it('转发刷新、触底加载、筛选和真实任务动作', async () => {
    const wrapper = mount(PickPage, {
      global: {
        stubs: {
          WarehouseTaskExecutionView: {
            emits: [
              'refresh',
              'loadMore',
              'update:locationCode',
              'update:status',
              'update:scopeKey',
              'execute',
            ],
            template: `
              <div>
                <button data-testid="refresh" @click="$emit('refresh')" />
                <button data-testid="load-more" @click="$emit('loadMore')" />
                <button data-testid="scan" @click="$emit('update:locationCode', 'A-01')" />
                <button data-testid="status" @click="$emit('update:status', 'InProgress')" />
                <button data-testid="scope" @click="$emit('update:scopeKey', 'team:TEAM-WMS-01')" />
                <button data-testid="execute" @click="$emit('execute', { action: 'start', task })" />
              </div>`,
            setup() {
              return { task }
            },
          },
        },
      },
    })

    await wrapper.get('[data-testid="refresh"]').trigger('click')
    await wrapper.get('[data-testid="load-more"]').trigger('click')
    await wrapper.get('[data-testid="scan"]').trigger('click')
    await wrapper.get('[data-testid="status"]').trigger('click')
    await wrapper.get('[data-testid="scope"]').trigger('click')
    await wrapper.get('[data-testid="execute"]').trigger('click')

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(candidateState.refresh).toHaveBeenCalledTimes(1)
    expect(loadMore).toHaveBeenCalledTimes(1)
    expect(filters.locationCode).toBe('A-01')
    expect(filters.status).toBe('InProgress')
    expect(scopeKey.value).toBe('team:TEAM-WMS-01')
    expect(executeTask).toHaveBeenCalledWith({ action: 'start', task })
  })
})
