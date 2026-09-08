import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import { useMaintenanceDowntimeReasonDirectory } from './useMaintenanceDowntimeReasonDirectory'

const queryState = vi.hoisted(() => ({
  generatedOptions: vi.fn(),
  optionsFactory: undefined as undefined | (() => Record<string, unknown>),
  data: { value: undefined as unknown },
  error: { value: undefined as unknown },
  isLoading: { value: false },
  refetch: vi.fn(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleSearchableDirectoryQueryOptions: queryState.generatedOptions,
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
      principalId: 'user-tech',
      principalType: 'user',
      loginName: 'tech01',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      ...overrides,
    } as never,
  })
}

function envelope(data: Record<string, unknown>, success = true) {
  return { success, data }
}

describe('useMaintenanceDowntimeReasonDirectory', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    queryState.optionsFactory = undefined
    queryState.data.value = undefined
    queryState.error.value = undefined
    queryState.isLoading.value = false
    queryState.generatedOptions.mockImplementation((options) => ({
      key: [{ _id: 'listBusinessConsoleSearchableDirectory' }],
      query: options.query,
    }))
  })

  it('只按 principal 的 organization/environment 请求权威 downtime-reason 目录', () => {
    useMaintenanceDowntimeReasonDirectory()
    expect(queryState.optionsFactory?.()).toMatchObject({ enabled: false })

    setActivePinia(createPinia())
    seedPrincipal()
    useMaintenanceDowntimeReasonDirectory()

    expect(queryState.optionsFactory?.()).toMatchObject({ enabled: true })
    expect(queryState.generatedOptions).toHaveBeenLastCalledWith({
      path: { directoryType: 'downtime-reason' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        pageIndex: 1,
        pageSize: 100,
        rankingMode: 'default',
      },
    })
  })

  it('跨租户隔离由后端承担：请求 scope 恒等于登录主体，前端不另设过滤', () => {
    // 别的 organization/environment 的码不会出现在响应里（BusinessGateway 按请求 org/env
    // 下发并按 principal 授权范围收敛），前端这一层能证明的只有一件事：
    // 请求携带的 scope 只能来自登录主体，调用方无从改写。
    seedPrincipal({ organizationId: 'org-002', environmentId: 'env-prod' })
    const directory = useMaintenanceDowntimeReasonDirectory()
    queryState.optionsFactory?.()

    expect(queryState.generatedOptions.mock.calls.at(-1)?.[0].query).toMatchObject({
      organizationId: 'org-002',
      environmentId: 'env-prod',
    })
    expect(Object.keys(directory)).not.toContain('organizationId')
    expect(Object.keys(directory)).not.toContain('environmentId')
  })

  it('关键字走服务端目录查询并 trim 空白；空关键字不进 query', () => {
    seedPrincipal()
    const directory = useMaintenanceDowntimeReasonDirectory()

    directory.search('  液压  ')
    queryState.optionsFactory?.()
    expect(queryState.generatedOptions.mock.calls.at(-1)?.[0].query).toMatchObject({
      keyword: '液压',
    })

    directory.search('   ')
    queryState.optionsFactory?.()
    expect(queryState.generatedOptions.mock.calls.at(-1)?.[0].query).not.toHaveProperty('keyword')
  })

  it('把目录条目的码原样带上写面——不 trim、不改大小写、名称缺失时回落成码', () => {
    seedPrincipal()
    queryState.data.value = envelope({
      status: 'available',
      items: [
        { id: '1', code: 'Hyd-Leak_01', displayName: '  液压泄漏  ' },
        { id: '2', code: 'Spindle-Noise' },
        { id: '3', code: '   ', displayName: '空码' },
        { id: '4', displayName: '缺码' },
      ],
      total: 4,
    })
    const directory = useMaintenanceDowntimeReasonDirectory()

    expect(directory.reasonOptions.value).toEqual([
      { code: 'Hyd-Leak_01', name: '液压泄漏', label: '液压泄漏（Hyd-Leak_01）' },
      { code: 'Spindle-Noise', name: 'Spindle-Noise', label: 'Spindle-Noise' },
    ])
    expect(directory.state.value).toBe('ok')
    expect(directory.canSelectReason.value).toBe(true)
  })

  it.each([
    [
      'unavailable 信封',
      () => {
        queryState.data.value = envelope({ status: 'unavailable', items: [], total: 0 })
      },
      'unavailable',
      '权威服务尚未配置停机原因词表，请联系管理员配置',
    ],
    [
      '403',
      () => {
        queryState.error.value = { status: 403, message: 'forbidden' }
      },
      'forbidden',
      '当前账号没有停机原因词表的读取权限，请联系管理员开通',
    ],
    [
      '一般失败',
      () => {
        queryState.error.value = new Error('boom')
      },
      'failed',
      '停机原因读取失败，请重试',
    ],
    [
      'HTTP 200 但 success:false',
      () => {
        queryState.data.value = { success: false, message: '下游不可用', data: null }
      },
      'failed',
      '停机原因读取失败，请重试',
    ],
  ])('%s 时 fail closed：零可选码且状态可归因', (_name, seed, state, message) => {
    seedPrincipal()
    seed()
    const directory = useMaintenanceDowntimeReasonDirectory()

    expect(directory.reasonOptions.value).toEqual([])
    expect(directory.canSelectReason.value).toBe(false)
    expect(directory.state.value).toBe(state)
    expect(directory.stateMessage.value).toBe(message)
  })

  it('一页取满时说出被截断——"翻不到"不能长得像"本组织没配"', () => {
    seedPrincipal()
    queryState.data.value = envelope({
      status: 'available',
      items: [{ id: '1', code: 'A-1', displayName: '甲' }],
      total: 137,
    })
    const directory = useMaintenanceDowntimeReasonDirectory()

    expect(directory.reasonsTotal.value).toBe(137)
    expect(directory.reasonsTruncated.value).toBe(true)
  })

  it('全量已在本页时不报截断', () => {
    seedPrincipal()
    queryState.data.value = envelope({
      status: 'available',
      items: [{ id: '1', code: 'A-1', displayName: '甲' }],
      total: 1,
    })
    const directory = useMaintenanceDowntimeReasonDirectory()

    expect(directory.reasonsTruncated.value).toBe(false)
  })

  it('读失败与词表未配置时不报截断，也不给出可疑总数', () => {
    seedPrincipal()
    queryState.error.value = new Error('boom')
    const failed = useMaintenanceDowntimeReasonDirectory()
    expect(failed.reasonsTruncated.value).toBe(false)
    expect(failed.reasonsTotal.value).toBe(0)

    setActivePinia(createPinia())
    seedPrincipal()
    queryState.error.value = undefined
    queryState.data.value = envelope({ status: 'unavailable', items: [], total: 42 })
    const unavailable = useMaintenanceDowntimeReasonDirectory()
    expect(unavailable.reasonsTruncated.value).toBe(false)
    expect(unavailable.reasonsTotal.value).toBe(0)
  })

  it('区分"组织没配"和"关键字没命中"，不把两者说成同一句话', () => {
    seedPrincipal()
    queryState.data.value = envelope({ status: 'available', items: [], total: 0 })
    const directory = useMaintenanceDowntimeReasonDirectory()

    expect(directory.state.value).toBe('empty')
    expect(directory.stateMessage.value).toBe('当前组织尚未配置可用停机原因')

    directory.search('不存在的原因')
    expect(directory.state.value).toBe('empty')
    expect(directory.stateMessage.value).toBe('没有匹配的停机原因')
    expect(directory.canSelectReason.value).toBe(false)
  })

  it('scope 未就绪时不发请求，也不谎报成"组织尚未配置"', () => {
    seedPrincipal({ environmentId: '' })
    const directory = useMaintenanceDowntimeReasonDirectory()

    expect(queryState.optionsFactory?.()).toMatchObject({ enabled: false })
    expect(directory.state.value).toBe('scope-pending')
    expect(directory.canSelectReason.value).toBe(false)
  })

  it('scope 未就绪时 refresh 不打网关', async () => {
    seedPrincipal({ organizationId: '' })
    const directory = useMaintenanceDowntimeReasonDirectory()

    await directory.refreshReasons()
    expect(queryState.refetch).not.toHaveBeenCalled()

    setActivePinia(createPinia())
    seedPrincipal()
    const ready = useMaintenanceDowntimeReasonDirectory()
    await ready.refreshReasons()
    expect(queryState.refetch).toHaveBeenCalledTimes(1)
  })
})
