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
  taskNo: 'PA-2026-0001',
  status: 'Open',
  version: 1,
  allowedActions: ['start'],
}

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsPutaway: () => ({
    filters,
    scopeKey,
    scopeOptions: computed(() => [{ label: '我的任务', value: 'self:emp049' }]),
    tasks: computed(() => [task]),
    total: computed(() => 1),
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

import PutawayPage from './putaway.vue'

describe('WMS 上架作业页', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    filters.status = 'Open'
    filters.keyword = undefined
    filters.locationCode = undefined
    scopeKey.value = 'self:emp049'
  })

  it('使用同一移动作业视图并锁定上架任务类型', () => {
    const wrapper = mount(PutawayPage, {
      global: {
        stubs: {
          WarehouseTaskExecutionView: {
            props: ['title', 'taskType', 'tasks', 'total', 'scopeKey', 'status'],
            template:
              '<div data-testid="execution-view">{{ title }}|{{ taskType }}|{{ total }}|{{ scopeKey }}|{{ status }}|{{ tasks[0].taskNo }}</div>',
          },
        },
      },
    })

    expect(wrapper.get('[data-testid="execution-view"]').text()).toContain(
      '上架|putaway|1|self:emp049|Open|PA-2026-0001',
    )
  })

  it('把真实上架动作交给 composable，不保留只读说明或伪完成按钮', async () => {
    const wrapper = mount(PutawayPage, {
      global: {
        stubs: {
          WarehouseTaskExecutionView: {
            emits: ['execute'],
            template:
              '<button data-testid="execute" @click="$emit(\'execute\', { action: \'start\', task: task })" />',
            setup() {
              return { task }
            },
          },
        },
      },
    })

    await wrapper.get('[data-testid="execute"]').trigger('click')

    expect(executeTask).toHaveBeenCalledWith({ action: 'start', task })
    expect(wrapper.text()).not.toContain('上架完成经收货入库过账')
  })

  it('下拉刷新同时刷新任务与作业候选', async () => {
    const wrapper = mount(PutawayPage, {
      global: {
        stubs: {
          WarehouseTaskExecutionView: {
            emits: ['refresh'],
            template: '<button data-testid="refresh" @click="$emit(\'refresh\')" />',
          },
        },
      },
    })

    await wrapper.get('[data-testid="refresh"]').trigger('click')

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(candidateState.refresh).toHaveBeenCalledTimes(1)
  })
})
