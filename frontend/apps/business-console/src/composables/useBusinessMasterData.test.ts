import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  createBusinessConsoleSkuMutationOptions,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsoleSkusQueryOptions,
} from '@nerv-iip/api-client'
import {
  useBusinessMasterDataGroups,
  useBusinessMasterDataResources,
  useBusinessSkus,
} from './useBusinessMasterData'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
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
  listBusinessConsoleMasterDataResourcesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMasterDataResources' }],
    query: vi.fn(),
  })),
  listBusinessConsoleSkusQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleSkus' }],
    query: vi.fn(),
  })),
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
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
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
})
