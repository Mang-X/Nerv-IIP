import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  listBusinessConsoleEngineeringBomsQueryOptions,
  listBusinessConsoleEngineeringProductionVersionsQueryOptions,
  listBusinessConsoleEngineeringRoutingsQueryOptions,
  resolveBusinessConsoleEngineeringProductionVersionQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessProductEngineering } from './useBusinessProductEngineering'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleEngineeringBomsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleEngineeringBoms' }],
    query: vi.fn(),
  })),
  listBusinessConsoleEngineeringProductionVersionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleEngineeringProductionVersions' }],
    query: vi.fn(),
  })),
  listBusinessConsoleEngineeringRoutingsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleEngineeringRoutings' }],
    query: vi.fn(),
  })),
  resolveBusinessConsoleEngineeringProductionVersionQueryOptions: vi.fn(() => ({
    key: [{ _id: 'resolveBusinessConsoleEngineeringProductionVersion' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))

describe('business product engineering composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
  })

  it('loads only published BOMs, routings, and active production versions by default', () => {
    coladaState.queryDataById.set('listBusinessConsoleEngineeringBoms', {
      success: true,
      data: {
        total: 11,
        items: [{ bomCode: 'MBOM-FRONT', revision: 'R1', parentItemCode: 'FG-FRONT-SHOCK', status: 'Published' }],
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleEngineeringRoutings', {
      success: true,
      data: {
        total: 12,
        items: [{ routingCode: 'RT-FRONT', revision: 'R1', skuCode: 'FG-FRONT-SHOCK', status: 'Published' }],
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleEngineeringProductionVersions', {
      success: true,
      data: {
        total: 13,
        items: [{ productionVersionId: 'pv-front', skuCode: 'FG-FRONT-SHOCK', status: 'active' }],
      },
    })

    const { boms, bomsTotal, productionVersions, productionVersionsTotal, routings, routingsTotal } = useBusinessProductEngineering()

    expect(listBusinessConsoleEngineeringBomsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'Published',
        skip: 0,
        take: 100,
      },
    })
    expect(listBusinessConsoleEngineeringRoutingsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'Published',
        skip: 0,
        take: 100,
      },
    })
    expect(listBusinessConsoleEngineeringProductionVersionsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'active',
        skip: 0,
        take: 100,
      },
    })
    expect(boms.value).toHaveLength(1)
    expect(bomsTotal.value).toBe(11)
    expect(routings.value).toHaveLength(1)
    expect(routingsTotal.value).toBe(12)
    expect(productionVersions.value).toHaveLength(1)
    expect(productionVersionsTotal.value).toBe(13)
  })

  it('uses the business context store for organization and environment filters', () => {
    const context = useBusinessContextStore()
    context.patchContext({
      organizationId: 'org-002',
      environmentId: 'prod',
    })

    useBusinessProductEngineering()

    expect(listBusinessConsoleEngineeringBomsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-002',
        environmentId: 'prod',
        status: 'Published',
        skip: 0,
        take: 100,
      },
    })
    expect(resolveBusinessConsoleEngineeringProductionVersionQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-002',
        environmentId: 'prod',
        skuCode: '',
        effectiveDate: expect.any(String),
        lotSize: 100,
      },
    })
  })

  it('does not resolve a production version until a SKU is selected', () => {
    const { resolveFilters } = useBusinessProductEngineering()

    expect(resolveBusinessConsoleEngineeringProductionVersionQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: '',
        effectiveDate: expect.any(String),
        lotSize: 100,
      },
    })
    expect(resolveFilters.skuCode).toBe('')
    expect(coladaState.queryOptionsById.get('resolveBusinessConsoleEngineeringProductionVersion')?.enabled)
      .toBe(false)
  })

  it('unwraps a successful resolved production version envelope without a default SKU', () => {
    coladaState.queryDataById.set('resolveBusinessConsoleEngineeringProductionVersion', {
      success: true,
      data: {
        productionVersionId: 'pv-front',
        skuCode: 'FG-FRONT-SHOCK',
        mbomVersionId: 'mbom-front-r1',
        routingVersionId: 'routing-front-r1',
        status: 'active',
      },
    })

    const { resolvedProductionVersion, resolveFilters } = useBusinessProductEngineering()

    expect(resolveBusinessConsoleEngineeringProductionVersionQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: '',
        effectiveDate: expect.any(String),
        lotSize: 100,
      },
    })
    expect(resolveFilters.skuCode).toBe('')
    expect(resolvedProductionVersion.value?.productionVersionId).toBe('pv-front')
  })
})
