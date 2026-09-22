import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { useBusinessContextStore } from '@/stores/businessContext'
import { deadLetterRowKey, useBusinessDeadLetters } from './useBusinessDeadLetters'

const apiState = vi.hoisted(() => ({
  /** 每次单条重放的入参，按调用顺序记录。 */
  replayCalls: [] as Array<{ service: string; deadLetterId: string; query: unknown }>,
  /** 按 `service/id` 给定这次重放的返回；缺省按「已重放」。 */
  replayResults: new Map<string, { succeeded: boolean; status: string }>(),
}))

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  invalidateQueries: vi.fn(async () => undefined),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleDeadLettersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleDeadLetters' }],
    query: vi.fn(),
  })),
  getBusinessConsoleDeadLetterMetricsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleDeadLetterMetrics' }],
    query: vi.fn(),
  })),
  getBusinessConsoleDeadLetterQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleDeadLetter' }],
    query: vi.fn(),
  })),
  replayBusinessConsoleDeadLetterMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars: { path: { service: string; deadLetterId: string } }) => {
      const { service, deadLetterId } = vars.path
      apiState.replayCalls.push({
        service,
        deadLetterId,
        query: (vars as { query?: unknown }).query,
      })
      const result = apiState.replayResults.get(deadLetterRowKey(service, deadLetterId)) ?? {
        succeeded: true,
        status: 'replayed',
      }
      return { success: true, data: { id: deadLetterId, ...result } }
    }),
  })),
  ignoreBusinessConsoleDeadLetterMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async () => ({ success: true, data: { status: 'ignored' } })),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    isLoading: shallowRef(false),
    error: shallowRef(),
    mutateAsync: vi.fn(async (vars) => options.mutation(vars)),
  })),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(async () => undefined),
    }
  }),
  useQueryCache: vi.fn(() => ({ invalidateQueries: coladaState.invalidateQueries })),
}))

function seedList(
  items: Array<{ service: string; id: string; eventType?: string }>,
  sourceStatuses: Array<{ service: string; status: string; reason?: string | null }> = [],
) {
  coladaState.queryDataById.set('listBusinessConsoleDeadLetters', {
    success: true,
    data: {
      items: items.map(({ service, id, eventType }) => ({
        service,
        deadLetter: { id, eventType, status: 'pending' },
      })),
      sourceStatuses,
    },
  })
}

function seedMetrics(
  sourceStatuses: Array<{ service: string; status: string; reason?: string | null }> = [],
) {
  coladaState.queryDataById.set('getBusinessConsoleDeadLetterMetrics', {
    success: true,
    data: { actionableCount: 0, sourceStatuses },
  })
}

describe('死信运维 composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    apiState.replayCalls = []
    apiState.replayResults.clear()
    coladaState.queryDataById.clear()
    coladaState.invalidateQueries.mockClear()
  })

  it('批量重放只重放传入的那几行，不按筛选条件放大范围', async () => {
    seedList([
      { service: 'Erp', id: 'dl-1' },
      { service: 'Mes', id: 'dl-2' },
      { service: 'Wms', id: 'dl-3' },
    ])
    const deadLetters = useBusinessDeadLetters()

    await deadLetters.replaySelected([
      { service: 'Erp', deadLetterId: 'dl-1' },
      { service: 'Wms', deadLetterId: 'dl-3' },
    ])

    expect(
      apiState.replayCalls.map(({ service, deadLetterId }) => `${service}/${deadLetterId}`),
    ).toEqual(['Erp/dl-1', 'Wms/dl-3'])
  })

  it('重放的作用域随请求发出——少了它服务端会拒绝这次调用', async () => {
    seedList([{ service: 'Erp', id: 'dl-1' }])
    const deadLetters = useBusinessDeadLetters()

    await deadLetters.replayOne('Erp', 'dl-1')

    expect(apiState.replayCalls[0]?.query).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
  })

  it('两个服务里同号的死信是两行，重放其一不会牵连另一行的结果', async () => {
    seedList([
      { service: 'Erp', id: 'shared-id' },
      { service: 'Mes', id: 'shared-id' },
    ])
    apiState.replayResults.set('Erp/shared-id', { succeeded: false, status: 'noHandler' })
    const deadLetters = useBusinessDeadLetters()

    await deadLetters.replayOne('Erp', 'shared-id')

    expect(deadLetters.replayOutcomes.get('Erp/shared-id')?.status).toBe('noHandler')
    expect(deadLetters.replayOutcomes.get('Mes/shared-id')).toBeUndefined()
  })

  it('答不上来的来源在列表与计数里各报一次，合并后只提示一次', () => {
    seedList(
      [],
      [
        { service: 'Erp', status: 'unavailable', reason: 'sourceTimeout' },
        { service: 'Mes', status: 'available', reason: null },
      ],
    )
    seedMetrics([
      { service: 'Erp', status: 'unavailable', reason: 'sourceTimeout' },
      { service: 'Wms', status: 'unavailable', reason: 'sourceUnavailable' },
    ])
    const deadLetters = useBusinessDeadLetters()

    expect(deadLetters.unavailableSources.value).toEqual([
      { service: 'Erp', reason: 'sourceTimeout' },
      { service: 'Wms', reason: 'sourceUnavailable' },
    ])
  })

  it('所有来源都答上来时不提示——空列表就是真的没有死信', () => {
    seedList([], [{ service: 'Erp', status: 'available', reason: null }])
    seedMetrics([{ service: 'Erp', status: 'available', reason: null }])
    const deadLetters = useBusinessDeadLetters()

    expect(deadLetters.hasUnavailableSource.value).toBe(false)
    expect(deadLetters.filteredServiceUnavailable.value).toBe(false)
  })

  it('筛到某个服务而它恰好没答上来时，单独可判——空态才能与「它没有死信」分开说', () => {
    seedList([], [{ service: 'Erp', status: 'unavailable', reason: 'sourceUnavailable' }])
    seedMetrics([])
    const deadLetters = useBusinessDeadLetters()

    deadLetters.filters.service = 'Mes'
    expect(deadLetters.filteredServiceUnavailable.value).toBe(false)

    deadLetters.filters.service = 'Erp'
    expect(deadLetters.filteredServiceUnavailable.value).toBe(true)
  })
})
