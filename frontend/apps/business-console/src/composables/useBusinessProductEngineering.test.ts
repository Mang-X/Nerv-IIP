import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  listBusinessConsoleEngineeringBomsQueryOptions,
  listBusinessConsoleEngineeringProductionVersionsQueryOptions,
  listBusinessConsoleEngineeringRoutingsQueryOptions,
  resolveBusinessConsoleEngineeringProductionVersionQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessProductEngineering } from './useBusinessProductEngineering'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
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
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
  })

  it('loads only released BOMs, routings, and active production versions by default', () => {
    coladaState.queryDataById.set('listBusinessConsoleEngineeringBoms', {
      success: true,
      data: {
        items: [{ bomCode: 'MBOM-FRONT', revision: 'R1', parentItemCode: 'FG-FRONT-SHOCK', status: 'Released' }],
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleEngineeringRoutings', {
      success: true,
      data: {
        items: [{ routingCode: 'RT-FRONT', revision: 'R1', skuCode: 'FG-FRONT-SHOCK', status: 'Released' }],
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleEngineeringProductionVersions', {
      success: true,
      data: {
        items: [{ productionVersionId: 'pv-front', skuCode: 'FG-FRONT-SHOCK', status: 'active' }],
      },
    })

    const { boms, productionVersions, routings } = useBusinessProductEngineering()

    expect(listBusinessConsoleEngineeringBomsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'Released',
      },
    })
    expect(listBusinessConsoleEngineeringRoutingsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'Released',
      },
    })
    expect(listBusinessConsoleEngineeringProductionVersionsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'active',
      },
    })
    expect(boms.value).toHaveLength(1)
    expect(routings.value).toHaveLength(1)
    expect(productionVersions.value).toHaveLength(1)
  })

  it('resolves the released production version for the selected SKU, date, and lot size', () => {
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
        skuCode: 'FG-FRONT-SHOCK',
        effectiveDate: expect.any(String),
        lotSize: 100,
      },
    })
    expect(resolveFilters.skuCode).toBe('FG-FRONT-SHOCK')
    expect(resolvedProductionVersion.value?.productionVersionId).toBe('pv-front')
  })
})
