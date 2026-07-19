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
  locateInspectionTasks,
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

  it('scans every facade page and locates a WMS receiving task by exact source document number', async () => {
    const loadPage = vi.fn(async (skip: number) => ({
      success: true,
      data: {
        total: 201,
        items:
          skip === 0
            ? Array.from({ length: 200 }, (_, index) => ({
                inspectionTaskId: `TASK-${index}`,
                sourceService: 'wms',
                sourceType: 'receiving',
                sourceDocumentId: `GR-${index}`,
              }))
            : [
                {
                  inspectionTaskId: 'TASK-LOCATED',
                  sourceService: 'WMS',
                  sourceType: 'receiving',
                  sourceDocumentId: 'ASN-20260718-0087',
                },
              ],
      },
    }))

    await expect(
      locateInspectionTasks(loadPage, { sourceDocumentNo: ' ASN-20260718-0087 ' }),
    ).resolves.toEqual([expect.objectContaining({ inspectionTaskId: 'TASK-LOCATED' })])
    expect(loadPage).toHaveBeenNthCalledWith(1, 0, 200)
    expect(loadPage).toHaveBeenNthCalledWith(2, 200, 200)
  })

  it('does not treat non-WMS, non-receiving or partial source references as a match', async () => {
    const items = [
      {
        inspectionTaskId: 'ERP',
        sourceService: 'erp',
        sourceType: 'receiving',
        sourceDocumentId: 'ASN-1',
      },
      {
        inspectionTaskId: 'FINAL',
        sourceService: 'wms',
        sourceType: 'final',
        sourceDocumentId: 'ASN-1',
      },
      {
        inspectionTaskId: 'PARTIAL',
        sourceService: 'wms',
        sourceType: 'receiving',
        sourceDocumentId: 'ASN-10',
      },
    ]

    await expect(
      locateInspectionTasks(async () => ({ success: true, data: { total: 3, items } }), {
        sourceDocumentNo: 'ASN-1',
      }),
    ).resolves.toEqual([])
  })

  it('rejects the whole locator query when a later facade page fails', async () => {
    const loadPage = vi.fn(async (skip: number) => {
      if (skip === 200) throw new Error('503')
      return {
        success: true,
        data: {
          total: 201,
          items: Array.from({ length: 200 }, (_, index) => ({ inspectionTaskId: `TASK-${index}` })),
        },
      }
    })

    await expect(locateInspectionTasks(loadPage, { inspectionTaskId: 'TASK-200' })).rejects.toThrow(
      '503',
    )
  })

  it('starts a task through the existing from-task record mutation and invalidates the list', async () => {
    const { startInspection, filters } = useQualityInspectionTasks()

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
    expect(filters.organizationId).toBe('org-001')
  })
})
