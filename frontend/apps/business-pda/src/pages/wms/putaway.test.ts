import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

const executeTask = vi.fn()
const refresh = vi.fn()
const loadMore = vi.fn()
const loadMoreError = shallowRef<unknown>()
const lastUpdatedAt = shallowRef('2026-08-01T08:00:00.000Z')
const actionPending = shallowRef(false)
const actionUnconfirmed = shallowRef(false)
const candidateState = vi.hoisted(() => ({ refresh: vi.fn(async () => {}) }))
const routeGuardState = vi.hoisted(() => ({
  guard: undefined as (() => boolean) | undefined,
}))
vi.mock('vue-router', () => ({
  onBeforeRouteLeave: vi.fn((guard: () => boolean) => {
    routeGuardState.guard = guard
  }),
}))
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
    loadMoreError,
    lastUpdatedAt,
    actionPending,
    actionUnconfirmed,
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
    actionPending.value = false
    actionUnconfirmed.value = false
    routeGuardState.guard = undefined
  })

  function dispatchBeforeUnload() {
    const event = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(event)
    return event
  }

  it('使用同一移动作业视图并锁定上架任务类型', () => {
    const wrapper = mount(PutawayPage, {
      global: {
        stubs: {
          WarehouseTaskExecutionView: {
            props: [
              'title',
              'taskType',
              'tasks',
              'total',
              'scopeKey',
              'status',
              'updatedAt',
              'loadMoreError',
            ],
            template:
              '<div data-testid="execution-view">{{ title }}|{{ taskType }}|{{ total }}|{{ scopeKey }}|{{ status }}|{{ tasks[0].taskNo }}|{{ updatedAt }}|{{ Boolean(loadMoreError) }}</div>',
          },
        },
      },
    })

    expect(wrapper.get('[data-testid="execution-view"]').text()).toContain(
      '上架|putaway|1|self:emp049|Open|PA-2026-0001|2026-08-01T08:00:00.000Z|false',
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

  it('开始成功后聚焦执行中上架任务', async () => {
    executeTask.mockResolvedValueOnce({ ...task, status: 'InProgress', version: 2 })
    const wrapper = mount(PutawayPage, {
      global: {
        stubs: {
          WarehouseTaskExecutionView: {
            emits: ['execute'],
            template:
              '<button data-testid="execute" @click="$emit(\'execute\', { action: \'start\', task })" />',
            setup() {
              return { task }
            },
          },
        },
      },
    })

    await wrapper.get('[data-testid="execute"]').trigger('click')
    await Promise.resolve()

    expect(filters.status).toBe('InProgress')
  })

  it('候选刷新失败也不阻断权威任务刷新后的执行中聚焦', async () => {
    refresh.mockResolvedValueOnce({ confirmedAction: 'start' })
    candidateState.refresh.mockRejectedValueOnce(new Error('candidate refresh failed'))
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
    await flushPromises()

    expect(filters.status).toBe('InProgress')
  })

  it.each([
    ['动作请求发送中', actionPending],
    ['动作结果待核实', actionUnconfirmed],
  ])('%s时阻止路由离开与浏览器刷新', async (_state, locked) => {
    mount(PutawayPage)
    locked.value = true

    expect(routeGuardState.guard?.()).toBe(false)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(true)

    locked.value = false
    expect(routeGuardState.guard?.()).toBe(true)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(false)
  })
})
