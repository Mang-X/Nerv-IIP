import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { reactive, shallowRef } from 'vue'
import { useMesDefectNcrs } from './useMesDefectNcrs'

const api = vi.hoisted(() => ({ list: vi.fn() }))
let options: () => { key: unknown; enabled: boolean; query: () => Promise<unknown> }
vi.mock('@nerv-iip/api-client', () => ({ listBusinessConsoleQualityNcrs: api.list }))
vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((factory) => {
    options = factory
    return {
      data: shallowRef(),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))
function item(sourceDocumentId: string, sourceType = 'in-process', id = 'ncr-1') {
  return { id, code: 'NCR-20261012-001', sourceType, sourceDocumentId }
}
function response(items: ReturnType<typeof item>[], total = items.length) {
  return { data: { success: true, data: { items, total } } }
}
function setup() {
  const source = reactive({ organizationId: 'org-1', environmentId: 'prod', defectNos: ['DEF-1'] })
  const result = useMesDefectNcrs(() => source)
  return { source, result }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((accept, fail) => {
    resolve = accept
    reject = fail
  })
  return { promise, resolve, reject }
}

describe('MES 不良的 Quality NCR 关联（#4333 公开查询合同）', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.list.mockReset()
  })

  it('starts independent defect reads together and returns only the complete batch', async () => {
    const first = deferred<ReturnType<typeof response>>()
    const second = deferred<ReturnType<typeof response>>()
    api.list.mockImplementation(({ query }) =>
      query.keyword === 'DEF-1' ? first.promise : second.promise,
    )
    const { source } = setup()
    source.defectNos = ['DEF-1', 'DEF-2']
    const pending = options().query()
    let completed = false
    void pending.then(() => {
      completed = true
    })
    try {
      // 请求顺序是本回归的合同：第一条未返回时，第二条已发起。
      expect(api.list.mock.calls.map(([request]) => request.query.keyword)).toEqual([
        'DEF-1',
        'DEF-2',
      ])
      second.resolve(response([item('DEF-2', 'in-process', 'ncr-2')]))
      await flushPromises()
      expect(completed).toBe(false)
      first.resolve(response([item('DEF-1')]))
      await expect(pending).resolves.toEqual({
        success: true,
        ncrs: { 'DEF-1': item('DEF-1'), 'DEF-2': item('DEF-2', 'in-process', 'ncr-2') },
      })
    } finally {
      first.resolve(response([item('DEF-1')]))
      second.resolve(response([item('DEF-2', 'in-process', 'ncr-2')]))
      await pending
    }
  })

  it('rejects the whole batch when one concurrent read fails after another succeeds', async () => {
    const first = deferred<ReturnType<typeof response>>()
    const second = deferred<ReturnType<typeof response>>()
    api.list.mockImplementation(({ query }) =>
      query.keyword === 'DEF-1' ? first.promise : second.promise,
    )
    const { source } = setup()
    source.defectNos = ['DEF-1', 'DEF-2']
    const pending = options().query()
    const failure = new Error('Quality read for DEF-2 failed')
    const rejected = expect(pending).rejects.toBe(failure)
    first.resolve(response([item('DEF-1')]))
    await flushPromises()
    second.reject(failure)
    await rejected
  })

  it('matches the exact defect source after approximate hits, including later pages', async () => {
    api.list
      .mockResolvedValueOnce(response([item('DEF-10'), item('DEF-1', 'receiving')], 3))
      .mockResolvedValueOnce(response([item('DEF-1')], 3))
    setup()
    const data = (await options().query()) as { success: boolean; ncrs: Record<string, unknown> }
    expect(data.ncrs['DEF-1']).toEqual(item('DEF-1'))
    expect(api.list).toHaveBeenNthCalledWith(1, {
      query: {
        organizationId: 'org-1',
        environmentId: 'prod',
        keyword: 'DEF-1',
        skip: 0,
        take: 100,
      },
      throwOnError: true,
    })
    expect(api.list).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        query: expect.objectContaining({ skip: 2 }),
      }),
    )
  })

  it('reports not generated only after a successful complete search without an exact match', async () => {
    api.list.mockResolvedValue(response([item('DEF-10'), item('DEF-1', 'receiving')]))
    setup()
    const data = (await options().query()) as { ncrs: Record<string, unknown> }
    expect(data.ncrs['DEF-1']).toBeNull()
  })

  it('does not turn a transport or unsuccessful business envelope into not generated', async () => {
    setup()
    const failure = new Error('Quality unavailable')
    api.list.mockRejectedValueOnce(failure)
    await expect(options().query()).rejects.toBe(failure)
    api.list.mockResolvedValueOnce({ data: { success: false, data: { items: [], total: 0 } } })
    await expect(options().query()).rejects.toThrow('NCR 关联读取失败')
  })

  it('binds the query identity and requests to current tenant and defects', async () => {
    const { source } = setup()
    const oldKey = options().key
    source.organizationId = 'org-2'
    source.environmentId = 'staging'
    source.defectNos = ['DEF-2']
    expect(options().key).not.toEqual(oldKey)
    api.list.mockResolvedValue(response([]))
    await options().query()
    expect(api.list).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          organizationId: 'org-2',
          environmentId: 'staging',
          keyword: 'DEF-2',
        }),
      }),
    )
    source.organizationId = ''
    expect(options().enabled).toBe(false)
  })
})
