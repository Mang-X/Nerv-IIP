import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  createBusinessConsoleSkuMutationOptions,
  removeBusinessConsoleTeamMemberMutationOptions,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  listBusinessConsoleWorkersQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  useBusinessMasterDataGroups,
  useBusinessMasterDataResources,
  useBusinessSkus,
  useTeamMembers,
  useBusinessWorkers,
  WORKER_DIRECTORY_MAX_PAGE_SIZE,
} from './useBusinessMasterData'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryFactoriesById: new Map<string, () => unknown>(),
  queryDataById: new Map<string, unknown>(),
}))

function mutationOptionStub() {
  return vi.fn(() => ({
    mutation: vi.fn(async (vars: { body: unknown }) => ({ success: true, data: vars.body })),
  }))
}

vi.mock('@nerv-iip/api-client', () => ({
  createBusinessConsoleSkuMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  createBusinessConsoleSiteMutationOptions: mutationOptionStub(),
  createBusinessConsoleProductionLineMutationOptions: mutationOptionStub(),
  createBusinessConsoleWorkCenterMutationOptions: mutationOptionStub(),
  registerBusinessConsoleDeviceAssetMutationOptions: mutationOptionStub(),
  createBusinessConsoleShiftMutationOptions: mutationOptionStub(),
  createBusinessConsoleWorkCalendarMutationOptions: mutationOptionStub(),
  createBusinessConsoleTeamMutationOptions: mutationOptionStub(),
  createBusinessConsoleDepartmentMutationOptions: mutationOptionStub(),
  addBusinessConsoleTeamMemberMutationOptions: mutationOptionStub(),
  listBusinessConsoleMasterDataResourcesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMasterDataResources' }],
    query: vi.fn(),
  })),
  listBusinessConsoleSkusQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleSkus' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWorkersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWorkers' }],
    query: vi.fn(),
  })),
  listBusinessConsoleTeamMembersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleTeamMembers' }],
    query: vi.fn(),
  })),
  removeBusinessConsoleTeamMemberMutationOptions: mutationOptionStub(),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      await options.onSuccess?.(result)
      return result
    }),
  })),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryFactoriesById.set(id, optionsFactory)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: coladaState.invalidateQueries,
  })),
}))

describe('business master data composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
    coladaState.queryFactoriesById.clear()
    coladaState.queryDataById.clear()
  })

  it('lists SKUs with default business context and exposes safe resources', () => {
    coladaState.queryDataById.set('listBusinessConsoleSkus', {
      success: true,
      data: {
        total: 120,
        resources: [
          {
            code: 'SKU-001',
            displayName: 'Widget',
          },
        ],
      },
    })

    const { filters, skus, skusTotal } = useBusinessSkus()

    expect(filters).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    expect(listBusinessConsoleSkusQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
      },
    })
    expect(skusTotal.value).toBe(120)
    expect(skus.value).toEqual([
      {
        code: 'SKU-001',
        displayName: 'Widget',
      },
    ])
  })

  it('defaults SKU resources to an empty array for unsuccessful envelopes', () => {
    coladaState.queryDataById.set('listBusinessConsoleSkus', {
      success: false,
    })

    const { skus } = useBusinessSkus()

    expect(skus.value).toEqual([])
  })

  it('uses the latest business context store values for SKU queries', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-ctx-a', environmentId: 'env-ctx-a' })

    const { filters } = useBusinessSkus()

    expect(filters).toMatchObject({
      organizationId: 'org-ctx-a',
      environmentId: 'env-ctx-a',
    })

    context.patchContext({ organizationId: 'org-ctx-b', environmentId: 'env-ctx-b' })
    coladaState.queryFactoriesById.get('listBusinessConsoleSkus')?.()

    expect(listBusinessConsoleSkusQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-ctx-b',
        environmentId: 'env-ctx-b',
        skip: 0,
        take: 100,
      },
    })
  })

  it('creates SKUs and invalidates the SKU list query', async () => {
    const { createSku } = useBusinessSkus()

    await createSku({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      code: 'SKU-002',
      name: 'New widget',
      baseUomCode: 'EA',
      category: 'FG',
      materialType: 'finished-good',
      batchTrackingPolicy: 'none',
      serialTrackingPolicy: 'none',
      shelfLifePolicyCode: 'none',
      storageConditionCode: 'ambient',
      defaultBarcodeRuleCode: 'default',
      qualityRequired: true,
    })

    expect(createBusinessConsoleSkuMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(createBusinessConsoleSkuMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        code: 'SKU-002',
      }),
    })
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({
      predicate: expect.any(Function),
    })
  })

  it('移除班组成员时把必填原因与业务上下文一起交给 generated mutation', async () => {
    const { removeMember } = useTeamMembers('TEAM-A')

    await removeMember('usr-1', '调入维修班组')

    expect(removeBusinessConsoleTeamMemberMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(removeBusinessConsoleTeamMemberMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      path: { teamCode: 'TEAM-A', userId: 'usr-1' },
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        reason: '调入维修班组',
      },
    })
  })

  it('lists master data resources by editable resource type', () => {
    coladaState.queryDataById.set('listBusinessConsoleMasterDataResources', {
      success: true,
      data: {
        total: 42,
        resources: [
          {
            resourceType: 'uom',
            code: 'EA',
          },
        ],
      },
    })

    const { resources, resourcesTotal } = useBusinessMasterDataResources('uom')

    expect(listBusinessConsoleMasterDataResourcesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        resourceType: 'uom',
        skip: 0,
        take: 100,
      },
    })
    expect(resourcesTotal.value).toBe(42)
    expect(resources.value).toEqual([
      {
        resourceType: 'uom',
        code: 'EA',
      },
    ])
  })

  it('lists multiple master data resource groups for linked selectors', () => {
    const { groups } = useBusinessMasterDataGroups([
      { key: 'site', title: '工厂' },
      { key: 'production-line', title: '产线' },
      { key: 'work-center', title: '工作中心' },
    ])

    expect(listBusinessConsoleMasterDataResourcesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        resourceType: 'site',
        skip: 0,
        take: 100,
      },
    })
    expect(listBusinessConsoleMasterDataResourcesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        resourceType: 'production-line',
        skip: 0,
        take: 100,
      },
    })
    expect(groups.value).toMatchObject([
      { key: 'site', title: '工厂', rows: [] },
      { key: 'production-line', title: '产线', rows: [] },
      { key: 'work-center', title: '工作中心', rows: [] },
    ])
  })

  // 回归：网关 BusinessConsoleWorkerDirectoryRequestValidator 要求 PageIndex > 0（1-based）。
  // 曾发 pageIndex:0 → 后端 400，组织/技能/派工三处人员选择器静默空（MAN-461 实机走查发现）。
  it('lists workers with a 1-based pageIndex to satisfy the gateway validator', () => {
    const { workers } = useBusinessWorkers()

    expect(listBusinessConsoleWorkersQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        pageIndex: 1,
        pageSize: 100,
      },
    })
    expect(workers.value).toEqual([])
  })

  /**
   * 网关 `RuleFor(x => x.PageSize).InclusiveBetween(1, 200)`。超了是 400，而调用方大多把
   * 人员目录当查表用，失败表现为**整列静默显占位符**而不是报错——极难从界面看出来。
   *
   * 第五轮走查实际踩到：待检工作台写死 `pageSize: 500`，「当前持有人」整列变 `—`，
   * 连「已被他人认领」都读不出来，而单测因为 mock 掉了 API 全绿。
   * 所以在 composable 里夹紧，并用这条锁住上界。
   */
  it('clamps the worker directory page size to the gateway bound', () => {
    useBusinessWorkers({ pageSize: 500 })

    expect(listBusinessConsoleWorkersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ pageSize: WORKER_DIRECTORY_MAX_PAGE_SIZE }),
    })
    expect(WORKER_DIRECTORY_MAX_PAGE_SIZE).toBe(200)
  })

  it('keeps a caller-supplied page size that is already within the bound', () => {
    useBusinessWorkers({ pageSize: 25 })

    expect(listBusinessConsoleWorkersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ pageSize: 25 }),
    })
  })

  // 派工候选靠工作中心收敛：filters 必须原样进入查询，否则弹窗会把全厂人都列出来。
  it('passes work-center, team, skill and duty filters to the worker directory query', () => {
    useBusinessWorkers({
      employmentStatus: 'active',
      workCenterCode: 'WC-CNC',
      teamCode: 'TEAM-CNC',
      skillCode: 'cnc-operation',
    })

    expect(listBusinessConsoleWorkersQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        teamCode: 'TEAM-CNC',
        workCenterCode: 'WC-CNC',
        skillCode: 'cnc-operation',
        employmentStatus: 'active',
        pageIndex: 1,
        pageSize: 100,
      },
    })
  })
})
