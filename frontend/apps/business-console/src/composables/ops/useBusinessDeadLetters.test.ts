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
  /** 这些行的重放调用直接抛出给定的错误（按 `service/id` 取）。 */
  replayThrows: new Map<string, unknown>(),
}))

/**
 * 三种失败形态。api-client 的错误拦截器把原始 `Response` 挂在 error 的 `response` 上，
 * `errorStatusCode` 据此读状态码；这里照同一形状造。
 */
const networkError = () => new Error('Failed to fetch')
const httpError = (status: number) =>
  Object.assign(new Error(`HTTP ${status}`), { response: { status } })

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
      const thrown = apiState.replayThrows.get(deadLetterRowKey(service, deadLetterId))
      if (thrown) {
        apiState.replayCalls.push({
          service,
          deadLetterId,
          query: (vars as { query?: unknown }).query,
        })
        throw thrown
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

/** 构造一次重放的目标；点击前行状态默认待处理。 */
function target(
  service: string,
  deadLetterId: string,
  statusAtAttempt: 'pending' | 'failed' = 'pending',
) {
  return { service, deadLetterId, statusAtAttempt }
}

/** 取出「收到了答复」那一档的受控状态；未答复或没点过返回 undefined。 */
function answeredStatus(result: unknown) {
  const r = result as { answered?: boolean; outcome?: { status?: string } } | undefined
  return r?.answered ? r.outcome?.status : undefined
}

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
      { service: 'Erp', deadLetterId: 'dl-1', statusAtAttempt: 'pending' },
      { service: 'Wms', deadLetterId: 'dl-3', statusAtAttempt: 'pending' },
    ])

    expect(
      apiState.replayCalls.map(({ service, deadLetterId }) => `${service}/${deadLetterId}`),
    ).toEqual(['Erp/dl-1', 'Wms/dl-3'])
  })

  it('重放的作用域随请求发出——少了它服务端会拒绝这次调用', async () => {
    seedList([{ service: 'Erp', id: 'dl-1' }])
    const deadLetters = useBusinessDeadLetters()

    await deadLetters.replayOne(target('Erp', 'dl-1'))

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

    await deadLetters.replayOne(target('Erp', 'shared-id'))

    expect(answeredStatus(deadLetters.replayResults.get('Erp/shared-id'))).toBe('noHandler')
    expect(deadLetters.replayResults.get('Mes/shared-id')).toBeUndefined()
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
    apiState.replayThrows.set('Mes/dl-2', networkError())
    const deadLetters = useBusinessDeadLetters()

    const results = await deadLetters.replaySelected([
      { service: 'Erp', deadLetterId: 'dl-1', statusAtAttempt: 'pending' },
      { service: 'Mes', deadLetterId: 'dl-2', statusAtAttempt: 'pending' },
      { service: 'Wms', deadLetterId: 'dl-3', statusAtAttempt: 'pending' },
    ])

    // 第 2 行失败没有挡住第 3 行
    expect(apiState.replayCalls.map((c) => `${c.service}/${c.deadLetterId}`)).toEqual([
      'Erp/dl-1',
      'Mes/dl-2',
      'Wms/dl-3',
    ])
    expect(results.map((r) => r.kind)).toEqual(['answered', 'unanswered', 'answered'])
    // 半应用状态的解药：无论成败，列表与计数都回到服务端的说法
    expect(coladaState.invalidateQueries).toHaveBeenCalled()
  })

  it('整批：没收到答复的行记为「未知」——不是 failed，也不是留白', async () => {
    seedList([{ service: 'Mes', id: 'dl-2' }])
    apiState.replayThrows.set('Mes/dl-2', networkError())
    const deadLetters = useBusinessDeadLetters()

    const results = await deadLetters.replaySelected([
      { service: 'Mes', deadLetterId: 'dl-2', statusAtAttempt: 'pending' },
    ])

    expect(results.map((r) => r.kind)).toEqual(['unanswered'])
    // 严格等于「未答复」：写成 failed 是伪造服务端取值；留白则与「从没点过」同形。
    // statusAtAttempt 是**行状态**枚举取值，记下的是发起这次重放时的状态。
    expect(deadLetters.replayResults.get('Mes/dl-2')).toStrictEqual({
      answered: false,
      statusAtAttempt: 'pending',
    })
  })

  it('单条：网关 502 同样记为「未知」并把错误抛给调用方——与整批同形', async () => {
    seedList([{ service: 'Mes', id: 'dl-2' }])
    apiState.replayThrows.set('Mes/dl-2', httpError(502))
    const deadLetters = useBusinessDeadLetters()

    // 点击前是「重放失败」（N1 那一类行）。与整批用例的 'pending' 取值互不相等：
    // 记下的值必须来自入参——若实现把它写死成某个常量，两条用例不可能同时绿。
    await expect(deadLetters.replayOne(target('Mes', 'dl-2', 'failed'))).rejects.toBeDefined()

    expect(deadLetters.replayResults.get('Mes/dl-2')).toStrictEqual({
      answered: false,
      statusAtAttempt: 'failed',
    })
    expect(coladaState.invalidateQueries).toHaveBeenCalled()
  })

  it('4xx 是网关的明确拒绝，不是「未知」：可以确定没有重放', async () => {
    seedList([
      { service: 'Mes', id: 'dl-2' },
      { service: 'Erp', id: 'dl-1' },
    ])
    apiState.replayThrows.set('Mes/dl-2', httpError(403))
    apiState.replayThrows.set('Erp/dl-1', httpError(400))
    const deadLetters = useBusinessDeadLetters()

    await expect(deadLetters.replayOne(target('Mes', 'dl-2'))).rejects.toBeDefined()
    const results = await deadLetters.replaySelected([
      { service: 'Erp', deadLetterId: 'dl-1', statusAtAttempt: 'pending' },
    ])

    // 把已知的拒绝说成「不确定、请去核实」，是与伪造 failed 反方向的同一种错。
    expect(deadLetters.replayResults.get('Mes/dl-2')).toBeUndefined()
    expect(deadLetters.replayResults.get('Erp/dl-1')).toBeUndefined()
    expect(results.map((r) => r.kind)).toEqual(['rejected'])
  })

  it('T3 混合批次（成功 + 502 + 429）：逐行恰好各归一类，批次后只保留被拒绝那一行的选中', async () => {
    seedList([
      { service: 'Erp', id: 'dl-1' },
      { service: 'Mes', id: 'dl-2' },
      { service: 'Wms', id: 'dl-3' },
    ])
    apiState.replayThrows.set('Mes/dl-2', httpError(502))
    apiState.replayThrows.set('Wms/dl-3', httpError(429))
    const deadLetters = useBusinessDeadLetters()
    deadLetters.selectedRowKeys.value = ['Erp/dl-1', 'Mes/dl-2', 'Wms/dl-3']

    const results = await deadLetters.replaySelected([
      target('Erp', 'dl-1'),
      target('Mes', 'dl-2'),
      target('Wms', 'dl-3'),
    ])

    expect(results.map((r) => `${r.rowKey}:${r.kind}`)).toEqual([
      'Erp/dl-1:answered',
      'Mes/dl-2:unanswered',
      'Wms/dl-3:rejected',
    ])
    // 批次不变量：每行要么有「重放结果」记录，要么仍选中。
    expect(deadLetters.selectedRowKeys.value).toEqual(['Wms/dl-3'])
    expect(deadLetters.replayResults.has('Erp/dl-1')).toBe(true)
    expect(deadLetters.replayResults.has('Mes/dl-2')).toBe(true)
    // 被拒绝的行不写记录（确定没重放，行上 `—` 是真话），靠保留选中交代。
    expect(deadLetters.replayResults.has('Wms/dl-3')).toBe(false)
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
