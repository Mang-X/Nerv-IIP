import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import { useBusinessDeviceDirectory } from './useBusinessDeviceDirectory'

const queryState = vi.hoisted(() => ({
  generatedOptions: vi.fn(),
  optionsFactory: undefined as undefined | (() => Record<string, unknown>),
  data: { value: undefined as unknown },
  error: { value: undefined as unknown },
  isLoading: { value: false },
  refetch: vi.fn(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleDeviceAssetsQueryOptions: queryState.generatedOptions,
  getConsolePrincipal: vi.fn(),
  loginConsoleUser: vi.fn(),
  logoutConsoleSession: vi.fn(),
  refreshConsoleSession: vi.fn(),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory: () => Record<string, unknown>) => {
    queryState.optionsFactory = optionsFactory
    optionsFactory()
    return {
      data: queryState.data,
      error: queryState.error,
      isLoading: queryState.isLoading,
      refetch: queryState.refetch,
    }
  }),
}))

function seedPrincipal(overrides: Record<string, unknown> = {}) {
  useAuthStore().$patch({
    principal: {
      principalId: 'user-admin',
      principalType: 'user',
      loginName: 'admin',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      ...overrides,
    } as never,
  })
}

describe('useBusinessDeviceDirectory', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    queryState.optionsFactory = undefined
    queryState.data.value = undefined
    queryState.error.value = undefined
    queryState.isLoading.value = false
    queryState.generatedOptions.mockImplementation((options) => ({
      key: [{ _id: 'listBusinessConsoleDeviceAssets' }],
      query: options.query,
    }))
  })

  it('gates the query until principal organization and environment are both present', () => {
    useBusinessDeviceDirectory()
    expect(queryState.optionsFactory?.()).toMatchObject({ enabled: false })

    setActivePinia(createPinia())
    seedPrincipal()
    useBusinessDeviceDirectory()
    expect(queryState.optionsFactory?.()).toMatchObject({ enabled: true })
    expect(queryState.generatedOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        includeDisabled: false,
        skip: 0,
        take: 20,
      },
    })
  })

  it('uses trimmed server keyword and bounded skip/take paging', () => {
    seedPrincipal()
    queryState.data.value = { success: true, data: { resources: [], total: 41 } }
    const directory = useBusinessDeviceDirectory()

    directory.search('  车床  ')
    queryState.optionsFactory?.()
    expect(queryState.generatedOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        includeDisabled: false,
        keyword: '车床',
        skip: 0,
        take: 20,
      },
    })

    expect(directory.canNextPage.value).toBe(true)
    directory.nextPage()
    queryState.optionsFactory?.()
    expect(queryState.generatedOptions.mock.calls.at(-1)?.[0].query).toMatchObject({
      keyword: '车床',
      skip: 20,
      take: 20,
    })
    directory.previousPage()
    expect(directory.deviceAssetFilters.skip).toBe(0)
  })

  it('returns only enabled rows carrying a non-empty stable deviceAssetId', () => {
    seedPrincipal()
    queryState.data.value = {
      success: true,
      data: {
        resources: [
          {
            deviceAssetId: ' device-1 ',
            displayName: '一号车床',
            code: 'LATHE-01',
            active: true,
          },
          { deviceAssetId: '', displayName: '无稳定 ID', code: 'BAD-1', active: true },
          { displayName: '缺失 ID', code: 'BAD-2', active: true },
          {
            deviceAssetId: 'device-disabled',
            displayName: '停用设备',
            code: 'BAD-3',
            active: false,
          },
        ],
        total: 4,
      },
    }
    const directory = useBusinessDeviceDirectory()

    expect(directory.deviceAssets.value).toEqual([
      expect.objectContaining({
        deviceAssetId: 'device-1',
        displayName: '一号车床',
        code: 'LATHE-01',
      }),
    ])
  })
})
