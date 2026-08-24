import { shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({
  queryOptions: undefined as Record<string, unknown> | undefined,
  create: vi.fn(),
  activate: vi.fn(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  activateBusinessConsoleQualityInspectionPlanMutationOptions: () => ({ kind: 'activate' }),
  createBusinessConsoleQualityInspectionPlanMutationOptions: () => ({ kind: 'create' }),
  createBusinessConsoleQualityInspectionRecordMutationOptions: () => ({ kind: 'record' }),
  getBusinessConsoleQualityInspectionRecordQueryOptions: (options: unknown) => options,
  getBusinessConsoleQualityNcrQueryOptions: (options: unknown) => options,
  listBusinessConsoleQualityInspectionPlanCharacteristicsQueryOptions: (options: unknown) =>
    options,
  listBusinessConsoleQualityInspectionPlansQueryOptions: (options: unknown) => options,
  listBusinessConsoleQualityInspectionRecordsQueryOptions: (options: Record<string, unknown>) => {
    state.queryOptions = options
    return options
  },
  listBusinessConsoleQualityNcrsQueryOptions: (options: unknown) => options,
  closeBusinessConsoleQualityNcr: vi.fn(),
  submitBusinessConsoleQualityNcrDisposition: vi.fn(),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: (options: { kind: string }) => ({
    error: shallowRef(undefined),
    isLoading: shallowRef(false),
    mutateAsync:
      options.kind === 'create'
        ? state.create
        : options.kind === 'activate'
          ? state.activate
          : vi.fn(),
  }),
  useQuery: (factory: () => unknown) => {
    factory()
    return {
      data: shallowRef({ success: true, data: { items: [], total: 0 } }),
      error: shallowRef(undefined),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  },
  useQueryCache: () => ({ invalidateQueries: vi.fn().mockResolvedValue(undefined) }),
}))

vi.mock('./businessContextBinding', () => ({
  bindBusinessContext: <T>(filters: T) => filters,
  hasBusinessContext: (filters: { organizationId?: string; environmentId?: string }) =>
    Boolean(filters.organizationId && filters.environmentId),
  refetchWithBusinessContext: (_filters: unknown, query: { refetch: () => unknown }) =>
    query.refetch(),
}))

describe('首件检验数据编排', () => {
  beforeEach(() => {
    state.queryOptions = undefined
    state.create.mockReset()
    state.activate.mockReset()
  })

  it('首件记录查询始终携带 first-article，并透传 SKU 与结果筛选', async () => {
    const quality = await import('./useBusinessQuality')
    const useFirstArticle = (
      quality as unknown as {
        useQualityFirstArticleInspections?: (initial?: Record<string, unknown>) => {
          recordFilters: { organizationId: string; environmentId: string }
        }
      }
    ).useQualityFirstArticleInspections

    expect(useFirstArticle).toBeTypeOf('function')
    if (!useFirstArticle) return

    useFirstArticle({
      organizationId: 'org-1',
      environmentId: 'env-1',
      skuCode: 'SKU-FA-001',
      result: 'passed',
    })

    expect(state.queryOptions).toEqual({
      query: expect.objectContaining({
        organizationId: 'org-1',
        environmentId: 'env-1',
        sourceType: 'first-article',
        keyword: 'SKU-FA-001',
        status: 'passed',
      }),
    })
  })

  it('创建成功但启用失败时返回草稿标识供页面恢复，不吞掉失败', async () => {
    state.create.mockResolvedValue({ success: true, data: { inspectionPlanId: 'plan-1' } })
    const activationError = new Error('activation failed')
    state.activate.mockRejectedValue(activationError)

    const quality = await import('./useBusinessQuality')
    const useFirstArticlePlanActions = (
      quality as unknown as {
        useQualityFirstArticlePlanActions?: () => {
          createAndActivateFirstArticlePlan: (body: Record<string, unknown>) => Promise<{
            inspectionPlanId: string
            activated: boolean
            activationError?: unknown
          }>
        }
      }
    ).useQualityFirstArticlePlanActions

    expect(useFirstArticlePlanActions).toBeTypeOf('function')
    if (!useFirstArticlePlanActions) return

    const result = await useFirstArticlePlanActions().createAndActivateFirstArticlePlan({
      organizationId: 'org-1',
      environmentId: 'env-1',
      planCode: 'FA-PLAN-001',
      category: 'first-article',
      characteristics: [{ characteristicCode: 'appearance' }],
    })

    expect(result).toEqual({
      inspectionPlanId: 'plan-1',
      activated: false,
      activationError,
    })
    expect(state.activate).toHaveBeenCalledWith({
      path: { inspectionPlanId: 'plan-1' },
      body: { organizationId: 'org-1', environmentId: 'env-1' },
    })
  })
})
