import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions,
  listBusinessConsoleQualityInspectionTasksQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  isInspectionTaskOverdue,
  sortInspectionTasks,
  useQualityInspectionTasks,
} from './useQualityInspectionTasks'

const state = vi.hoisted(() => ({
  data: undefined as unknown,
  invalidateQueries: vi.fn(async () => undefined),
}))

vi.mock('@nerv-iip/api-client', () => ({
  createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (variables) => ({ success: true, data: variables })),
  })),
  listBusinessConsoleQualityInspectionTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleQualityInspectionTasks' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (variables) => options.mutation(variables)),
  })),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    return {
      data: shallowRef(state.data),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
      key: options.key,
    }
  }),
  useQueryCache: vi.fn(() => ({ invalidateQueries: state.invalidateQueries })),
}))

describe('quality inspection task workbench', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    state.data = undefined
    state.invalidateQueries.mockClear()
    vi.clearAllMocks()
  })

  it('passes context, source type and server pagination to the real task facade', () => {
    const { filters } = useQualityInspectionTasks({ sourceType: 'receiving', take: 20 })

    expect(listBusinessConsoleQualityInspectionTasksQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 20,
      },
    })
    expect(filters.take).toBe(20)
  })

  it('keeps the server total while exposing safe task items', () => {
    state.data = {
      success: true,
      data: { total: 12, items: [{ inspectionTaskId: 'TASK-1', status: 'pending' }] },
    }

    const { tasks, total } = useQualityInspectionTasks()

    expect(total.value).toBe(12)
    expect(tasks.value).toEqual([{ inspectionTaskId: 'TASK-1', status: 'pending' }])
  })

  it('filters the visible page by平级来源类型 without inventing a second data source', () => {
    state.data = {
      success: true,
      data: {
        total: 2,
        items: [
          { inspectionTaskId: 'TASK-IN', sourceType: 'receiving' },
          { inspectionTaskId: 'TASK-OP', sourceType: 'operation' },
        ],
      },
    }

    const { filters, tasks } = useQualityInspectionTasks()
    filters.sourceType = 'receiving'

    expect(tasks.value.map((task) => task.inspectionTaskId)).toEqual(['TASK-IN'])
  })

  it('sorts overdue tasks ahead of due tasks without hiding the status text', () => {
    const now = new Date('2026-07-19T10:00:00Z')
    const overdue = {
      inspectionTaskId: 'late',
      status: 'pending',
      dueAtUtc: '2026-07-18T10:00:00Z',
    }
    const due = { inspectionTaskId: 'due', status: 'pending', dueAtUtc: '2026-07-20T10:00:00Z' }

    expect(isInspectionTaskOverdue(overdue, now)).toBe(true)
    expect(sortInspectionTasks([due, overdue], now).map((task) => task.inspectionTaskId)).toEqual([
      'late',
      'due',
    ])
  })

  it('starts a task through the existing from-task record mutation and invalidates the list', async () => {
    const { startInspection, taskFilters } = useQualityInspectionTasks()

    await startInspection('TASK-1', {
      inspectorUserId: 'user-qa',
      resultLines: [],
    })

    expect(createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions).mock.results[0]
        ?.value.mutation,
    ).toHaveBeenCalledWith({
      path: { inspectionTaskId: 'TASK-1' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      body: { inspectorUserId: 'user-qa', resultLines: [] },
    })
    expect(taskFilters.organizationId).toBe('org-001')
  })
})
