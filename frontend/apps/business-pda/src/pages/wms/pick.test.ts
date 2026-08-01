import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

const executeTask = vi.fn()
const refresh = vi.fn()
const loadMore = vi.fn()
const loadMoreError = shallowRef<unknown>()
const actionError = shallowRef<unknown>()
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
    loadMoreError,
    actionError,
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

import PickPage from './pick.vue'

describe('WMS 拣货作业页', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    filters.status = 'Open'
    filters.keyword = undefined
    filters.locationCode = undefined
    scopeKey.value = 'self:emp049'
    actionPending.value = false
    actionUnconfirmed.value = false
    actionError.value = { message: '拣货任务操作失败' }
    routeGuardState.guard = undefined
  })

  function dispatchBeforeUnload() {
    const event = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(event)
    return event
  }

  it('把范围、筛选、分页与任务交给移动作业视图', () => {
    const wrapper = mount(PickPage, {
      global: {
        stubs: {
          WarehouseTaskExecutionView: {
            props: [
              'title',
              'taskType',
              'tasks',
              'total',
              'status',
              'scopeKey',
              'scopeOptions',
              'updatedAt',
              'loadMoreError',
              'actionError',
            ],
            template:
              '<div data-testid="execution-view">{{ title }}|{{ taskType }}|{{ total }}|{{ scopeKey }}|{{ status }}|{{ tasks[0].taskNo }}|{{ updatedAt }}|{{ Boolean(loadMoreError) }}|{{ Boolean(actionError) }}</div>',
          },
        },
      },
    })

    expect(wrapper.get('[data-testid="execution-view"]').text()).toContain(
      '拣货|picking|2|self:emp049|Open|PK-2026-0001|2026-08-01T08:00:00.000Z|false|true',
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

  it('开始成功后聚焦执行中任务，避免任务从待执行列表消失', async () => {
    executeTask.mockResolvedValueOnce({ ...task, status: 'InProgress', version: 2 })
    const wrapper = mount(PickPage, {
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
    const wrapper = mount(PickPage, {
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
    mount(PickPage)
    locked.value = true

    expect(routeGuardState.guard?.()).toBe(false)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(true)

    locked.value = false
    expect(routeGuardState.guard?.()).toBe(true)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(false)
  })
})
