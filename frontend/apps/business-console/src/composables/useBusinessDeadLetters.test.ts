import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { useBusinessContextStore } from '@/stores/businessContext'
import {
  getBusinessConsoleDeadLetterMetricsQueryOptions,
  listBusinessConsoleDeadLettersQueryOptions,
} from '@nerv-iip/api-client'
import { deadLetterRowKey, useBusinessDeadLetters } from './useBusinessDeadLetters'

const apiState = vi.hoisted(() => ({
  /** 每次单条重放的入参，按调用顺序记录。 */
  replayCalls: [] as Array<{ service: string; deadLetterId: string; query: unknown }>,
  /** 按 `service/id` 给定这次重放的返回；缺省按「已重放」。 */
  replayResults: new Map<string, { succeeded: boolean; status: string }>(),
  /** 这些行的重放调用直接抛（模拟传输层失败）。 */
  replayThrows: new Set<string>(),
}))

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  invalidateQueries: vi.fn(async () => undefined),
  /**
   * 每个查询的 options 工厂。mock 的 `useQuery` 不具响应性（只在构造时求值一次），
   * 断言若依赖「改了 filters 就会自动重新求值」会**恒真**——那正是上一版那条零鉴别力断言的成因。
   * 这里把工厂留出来，由测试显式重新求值，鉴别力不再挂在 mock 的响应性上。
   */
  queryFactoryById: new Map<string, () => unknown>(),
}))

/** 显式重新求值某个查询的 options，使 generated options 函数收到当前 filters。 */
function reevaluateQuery(id: string) {
  const factory = coladaState.queryFactoryById.get(id)
  if (!factory) throw new Error(`query factory not registered: ${id}`)
  factory()
}

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
      if (apiState.replayThrows.has(deadLetterRowKey(service, deadLetterId))) {
        apiState.replayCalls.push({
          service,
          deadLetterId,
          query: (vars as { query?: unknown }).query,
        })
        throw new Error('transport boom')
      }
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
    coladaState.queryFactoryById.set(id, optionsFactory)
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
    apiState.replayThrows.clear()
    coladaState.queryDataById.clear()
    coladaState.queryFactoryById.clear()
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

  it('选中某个服务后，可选服务清单不塌缩——否则用户切不到别的服务', () => {
    // 网关在 service 非空时只扇出那一个来源，列表响应的 sourceStatuses 因此只回一条；
    // 清单必须取自不带筛选的概览响应。
    seedList([{ service: 'Erp', id: 'dl-1' }], [{ service: 'Erp', status: 'available' }])
    seedMetrics([
      { service: 'Erp', status: 'available' },
      { service: 'Mes', status: 'available' },
      { service: 'Wms', status: 'available' },
    ])
    const deadLetters = useBusinessDeadLetters()

    deadLetters.filters.service = 'Erp'

    expect(deadLetters.availableServices.value).toEqual(['Erp', 'Mes', 'Wms'])
  })

  it('概览查询不带 service —— 它是全局堆积，不随列表筛选收缩', () => {
    seedList([])
    seedMetrics([])
    const deadLetters = useBusinessDeadLetters()
    deadLetters.filters.service = 'Erp'

    // 显式重新求值，不指望 mock 的 useQuery 有响应性。
    reevaluateQuery('getBusinessConsoleDeadLetterMetrics')
    reevaluateQuery('listBusinessConsoleDeadLetters')

    // `toStrictEqual`：`toEqual` 把值为 undefined 的键视同不存在，
    // 于是 `{..., service: undefined}` 也会通过，断言挡不住它自称要挡的那个回归。
    const metricsQuery = vi
      .mocked(getBusinessConsoleDeadLetterMetricsQueryOptions)
      .mock.calls.at(-1)?.[0].query
    expect(metricsQuery).toStrictEqual({ organizationId: 'org-001', environmentId: 'env-dev' })

    // 阳性对照：同一时刻列表侧**确实**带上了筛选，证明筛选真的生效、不是「哪边都没传」。
    const listQuery = vi
      .mocked(listBusinessConsoleDeadLettersQueryOptions)
      .mock.calls.at(-1)?.[0].query
    expect(listQuery?.service).toBe('Erp')
  })

  it('整批重放中某行失败不中断其余行，且列表计数一定被失效', async () => {
    seedList([
      { service: 'Erp', id: 'dl-1' },
      { service: 'Mes', id: 'dl-2' },
      { service: 'Wms', id: 'dl-3' },
    ])
    apiState.replayThrows.add('Mes/dl-2')
    const deadLetters = useBusinessDeadLetters()

    const { outcomes, firstError, unansweredCount } = await deadLetters.replaySelected([
      { service: 'Erp', deadLetterId: 'dl-1' },
      { service: 'Mes', deadLetterId: 'dl-2' },
      { service: 'Wms', deadLetterId: 'dl-3' },
    ])

    // 第 2 行失败没有挡住第 3 行
    expect(apiState.replayCalls.map((c) => `${c.service}/${c.deadLetterId}`)).toEqual([
      'Erp/dl-1',
      'Mes/dl-2',
      'Wms/dl-3',
    ])
    expect(outcomes.map(({ outcome }) => outcome.status)).toEqual(['replayed', 'replayed'])
    expect(firstError).toBeDefined()
    expect(unansweredCount).toBe(1)
    // 半应用状态的解药：无论成败，列表与计数都回到服务端的说法
    expect(coladaState.invalidateQueries).toHaveBeenCalled()
  })

  it('没收到答复的行不写受控枚举——「未知」不能伪装成「试过并失败了」', async () => {
    seedList([{ service: 'Mes', id: 'dl-2' }])
    apiState.replayThrows.add('Mes/dl-2')
    const deadLetters = useBusinessDeadLetters()

    const { unansweredCount } = await deadLetters.replaySelected([
      { service: 'Mes', deadLetterId: 'dl-2' },
    ])

    expect(unansweredCount).toBe(1)
    // 行上不留痕迹：留了 `failed` 会与刷新后的「状态」列自相矛盾（502 但下游其实已重放成功）。
    expect(deadLetters.replayOutcomes.get('Mes/dl-2')).toBeUndefined()
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
