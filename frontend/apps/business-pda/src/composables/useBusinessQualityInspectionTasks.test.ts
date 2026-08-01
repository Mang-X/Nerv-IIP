import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { useBusinessQualityInspectionTasks } from './useBusinessQualityInspectionTasks'
import { useAuthStore } from '@/stores/auth'

const coladaState = vi.hoisted(() => ({
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  queryFactoryById: new Map<string, () => unknown>(),
  dataById: new Map<string, { value: unknown }>(),
  refetchById: new Map<string, ReturnType<typeof vi.fn>>(),
  submit: vi.fn(),
  claim: vi.fn(),
  listPlain: vi.fn(),
  listOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleQualityInspectionTasks' }],
    query: vi.fn(),
  })),
}))

// The composable consumes the Quality facade through the curated
// `@nerv-iip/api-client` barrel; mock it here. Auth-API functions are stubbed
// because `@/stores/auth` lazily references them (never called — we only $patch).
vi.mock('@nerv-iip/api-client', () => ({
  confirmBusinessConsoleOperation: vi.fn(async (value) => value),
  listBusinessConsoleQualityInspectionTasks: coladaState.listPlain,
  listBusinessConsoleQualityInspectionTasksQueryOptions: coladaState.listOptions,
  listBusinessConsoleQualityReasonCodesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleQualityReasonCodes' }],
    query: vi.fn(),
  })),
  listBusinessConsoleQualityInspectionPlanCharacteristicsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleQualityInspectionPlanCharacteristics' }],
    query: vi.fn(),
  })),
  createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
    mutationKind: 'submit',
  })),
  claimBusinessConsoleQualityInspectionTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
    mutationKind: 'claim',
  })),
  getConsolePrincipal: vi.fn(),
  loginConsoleUser: vi.fn(),
  logoutConsoleSession: vi.fn(),
  refreshConsoleSession: vi.fn(),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)
    coladaState.queryFactoryById.set(id, optionsFactory)
    const data = coladaState.dataById.get(id) ?? shallowRef(undefined)
    coladaState.dataById.set(id, data)
    const refetch = vi.fn()
    coladaState.refetchById.set(id, refetch)
    return {
      data,
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch,
    }
  }),
  useMutation: vi.fn((options) => ({
    mutateAsync: options.mutationKind === 'claim' ? coladaState.claim : coladaState.submit,
    isLoading: shallowRef(false),
    error: shallowRef(),
  })),
  useQueryCache: vi.fn(() => ({ invalidateQueries: vi.fn(() => Promise.resolve()) })),
}))

function seedPrincipal(overrides: Record<string, unknown> = {}) {
  const auth = useAuthStore()
  auth.$patch((state) => {
    state.principal = {
      principalId: 'user-admin',
      principalType: 'user',
      loginName: 'admin',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      ...overrides,
    } as never
  })
}

const LINES = [
  {
    characteristicCode: '外径',
    observedValue: '10.5',
    unitCode: 'mm',
    result: 'failed' as const,
    defectReason: null,
    defectQuantity: null,
    measuredValue: 10.5,
  },
]

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

describe('useBusinessQualityInspectionTasks', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryOptionsById.clear()
    coladaState.queryFactoryById.clear()
    coladaState.dataById.clear()
    coladaState.refetchById.clear()
    coladaState.claim.mockResolvedValue({
      success: true,
      data: {
        inspectionTaskId: 'TASK-1',
        status: 'in-progress',
        assignedInspectorUserId: 'user-admin',
        version: 3,
      },
    })
    coladaState.listPlain.mockImplementation(
      async ({ query }: { query: { inspectionTaskId?: string } }) => ({
        data: {
          success: true,
          data: {
            items: query.inspectionTaskId
              ? [
                  {
                    inspectionTaskId: query.inspectionTaskId,
                    status: 'in-progress',
                    assignedInspectorUserId: 'user-admin',
                    version: 3,
                    allowedActions: ['submit-inspection'],
                  },
                ]
              : [],
            total: query.inspectionTaskId ? 1 : 0,
          },
        },
      }),
    )
  })

  it('keeps list + reason-code queries disabled when the principal has no org/env scope', () => {
    useBusinessQualityInspectionTasks()
    expect(
      coladaState.queryOptionsById.get('listBusinessConsoleQualityInspectionTasks')?.enabled,
    ).toBe(false)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleQualityReasonCodes')?.enabled).toBe(
      false,
    )
  })

  it('enables the queries once the principal carries an org/env scope', () => {
    seedPrincipal()
    useBusinessQualityInspectionTasks()
    expect(
      coladaState.queryOptionsById.get('listBusinessConsoleQualityInspectionTasks')?.enabled,
    ).toBe(true)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleQualityReasonCodes')?.enabled).toBe(
      true,
    )
    expect(coladaState.listOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'user-admin',
      }),
    })
  })

  it('uses a bounded first page and sends task filters to the server before pagination', async () => {
    seedPrincipal()
    coladaState.dataById.set(
      'listBusinessConsoleQualityInspectionTasks',
      shallowRef({ success: true, data: { items: [{ inspectionTaskId: 'T1' }], total: 2 } }),
    )
    coladaState.listPlain.mockResolvedValue({
      data: { success: true, data: { items: [{ inspectionTaskId: 'T2' }], total: 2 } },
    })
    const result = useBusinessQualityInspectionTasks()

    expect(coladaState.listOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ skip: 0, take: 20, status: 'pending' }),
    })

    result.filters.keyword = 'WO-9001'
    result.filters.sourceType = 'operation'
    result.filters.overdue = true
    await nextTick()
    coladaState.dataById.get('listBusinessConsoleQualityInspectionTasks')!.value = {
      success: true,
      data: { items: [{ inspectionTaskId: 'FILTERED-T1' }], total: 2 },
    }
    await nextTick()
    await result.loadMore()

    expect(coladaState.listPlain).toHaveBeenLastCalledWith({
      query: expect.objectContaining({
        skip: 1,
        take: 20,
        keyword: 'WO-9001',
        sourceType: 'operation',
        overdue: true,
      }),
    })
  })

  it('normalizes keyword once for query identity and every pagination request', async () => {
    seedPrincipal()
    coladaState.dataById.set(
      'listBusinessConsoleQualityInspectionTasks',
      shallowRef({
        success: true,
        data: { items: [{ inspectionTaskId: 'T1' }], total: 2 },
      }),
    )
    coladaState.listPlain.mockResolvedValue({
      data: { success: true, data: { items: [{ inspectionTaskId: 'T2' }], total: 2 } },
    })
    const result = useBusinessQualityInspectionTasks()

    result.filters.keyword = ' abc '
    await nextTick()
    coladaState.queryFactoryById.get('listBusinessConsoleQualityInspectionTasks')!()
    expect(coladaState.listOptions).toHaveBeenLastCalledWith({
      query: expect.objectContaining({ keyword: 'abc' }),
    })

    coladaState.dataById.get('listBusinessConsoleQualityInspectionTasks')!.value = {
      success: true,
      data: { items: [{ inspectionTaskId: 'T1' }], total: 2 },
    }
    await nextTick()
    await result.loadMore()
    expect(coladaState.listPlain).toHaveBeenLastCalledWith({
      query: expect.objectContaining({ keyword: 'abc' }),
    })

    const loadedBeforeEquivalentKeyword = result.loaded.value
    result.filters.keyword = 'abc'
    await nextTick()
    expect(result.loaded.value).toBe(loadedBeforeEquivalentKeyword)

    result.filters.keyword = '   '
    await nextTick()
    coladaState.queryFactoryById.get('listBusinessConsoleQualityInspectionTasks')!()
    expect(coladaState.listOptions).toHaveBeenLastCalledWith({
      query: expect.not.objectContaining({ keyword: expect.anything() }),
    })
  })

  it('claims an assigned pending task with the trusted self scope before execution', async () => {
    seedPrincipal()
    const { claimTask } = useBusinessQualityInspectionTasks()

    const claimed = await claimTask({
      inspectionTaskId: 'TASK-1',
      status: 'pending',
      version: 2,
      allowedActions: ['claim'],
    })

    expect(coladaState.claim).toHaveBeenCalledWith({
      path: { inspectionTaskId: 'TASK-1' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'user-admin',
      },
      body: {
        idempotencyKey: expect.stringMatching(/^quality-claim-/),
        expectedVersion: 2,
      },
    })
    expect(claimed).toMatchObject({
      inspectionTaskId: 'TASK-1',
      status: 'in-progress',
      assignedInspectorUserId: 'user-admin',
      version: 3,
    })
  })

  it('round-trips claim through the exact in-progress readback before allowing submit', async () => {
    seedPrincipal()
    coladaState.listPlain
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                inspectionTaskId: 'TASK-1',
                status: 'in-progress',
                assignedInspectorUserId: 'user-admin',
                version: 4,
                allowedActions: ['submit-inspection'],
                blockReasons: [],
              },
            ],
            total: 1,
          },
        },
      })
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                inspectionTaskId: 'TASK-1',
                status: 'in-progress',
                assignedInspectorUserId: 'user-admin',
                version: 4,
                allowedActions: ['submit-inspection'],
                blockReasons: [],
              },
            ],
            total: 1,
          },
        },
      })
    const { claimTask, submitInspection } = useBusinessQualityInspectionTasks()

    const claimed = await claimTask({
      inspectionTaskId: 'TASK-1',
      status: 'pending',
      version: 2,
      allowedActions: ['claim'],
    })
    await submitInspection('TASK-1', LINES)

    expect(claimed).toMatchObject({
      inspectionTaskId: 'TASK-1',
      status: 'in-progress',
      assignedInspectorUserId: 'user-admin',
      version: 4,
      allowedActions: ['submit-inspection'],
    })
    expect(coladaState.listPlain).toHaveBeenCalledTimes(2)
    expect(coladaState.listPlain).toHaveBeenNthCalledWith(1, {
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'user-admin',
        inspectionTaskId: 'TASK-1',
        skip: 0,
        take: 2,
      },
      throwOnError: true,
    })
    expect(coladaState.submit).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['task-already-claimed', '任务已由其他检验员领取。'],
    ['task-assigned-to-another-inspector', '任务已派给其他检验员，无法领取。'],
    ['task-assigned-to-another-team', '任务已派给其他班组，无法领取。'],
    ['task-outside-selected-work-scope', '任务不在当前工作范围内，无法领取。'],
  ])('explains the exact claim block reason %s', async (reason, expectedMessage) => {
    seedPrincipal()
    const { claimTask } = useBusinessQualityInspectionTasks()

    await expect(
      claimTask({
        inspectionTaskId: 'TASK-BLOCKED',
        status: 'pending',
        version: 2,
        allowedActions: [],
        blockReasons: [reason],
      }),
    ).rejects.toThrow(expectedMessage)
    expect(coladaState.claim).not.toHaveBeenCalled()
  })

  it.each([
    [
      { status: 403, message: 'task-outside-selected-work-scope' },
      '任务不在当前工作范围内，无法领取。',
    ],
    [{ response: { status: 422 }, message: 'task-already-claimed' }, '任务已由其他检验员领取。'],
  ])(
    'translates only a stable claim mutation blocker into actionable Chinese',
    async (failure, message) => {
      seedPrincipal()
      coladaState.claim.mockRejectedValueOnce(failure)
      const { claimTask } = useBusinessQualityInspectionTasks()

      await expect(
        claimTask({
          inspectionTaskId: 'TASK-RACED',
          status: 'pending',
          version: 2,
          allowedActions: ['claim'],
        }),
      ).rejects.toThrow(message)
    },
  )

  it.each([
    { success: false, message: 'lifecycle-conflict' },
    { status: 422, message: 'internal-untrusted-detail' },
  ])('preserves non-safe claim failures for the page recovery boundary', async (failure) => {
    seedPrincipal()
    coladaState.claim.mockRejectedValueOnce(failure)
    const { claimTask } = useBusinessQualityInspectionTasks()

    await expect(
      claimTask({
        inspectionTaskId: 'TASK-RACED',
        status: 'pending',
        version: 2,
        allowedActions: ['claim'],
      }),
    ).rejects.toBe(failure)
  })

  it('keeps inspector identity out of the public submit body and forwards org/env', async () => {
    seedPrincipal()
    const { submitInspection } = useBusinessQualityInspectionTasks()

    await submitInspection('TASK-1', LINES)

    expect(coladaState.submit).toHaveBeenCalledTimes(1)
    const arg = coladaState.submit.mock.calls[0][0]
    expect(arg.path).toEqual({ inspectionTaskId: 'TASK-1' })
    expect(arg.query).toEqual({ organizationId: 'org-001', environmentId: 'env-dev' })
    expect(arg.body).not.toHaveProperty('inspectorUserId')
    expect(arg.body.resultLines).toEqual(LINES)
    expect(arg.body.idempotencyKey).toMatch(/^quality-submit-/)
  })

  it('reuses the same submit key after timeout and rotates it after a confirmed success', async () => {
    seedPrincipal()
    coladaState.submit
      .mockRejectedValueOnce(new TypeError('network failed'))
      .mockResolvedValueOnce({ success: true, data: {} })
      .mockResolvedValueOnce({ success: true, data: {} })
    const { submitInspection } = useBusinessQualityInspectionTasks()

    await expect(submitInspection('TASK-1', LINES)).rejects.toThrow('network failed')
    await submitInspection('TASK-1', LINES)
    await submitInspection('TASK-1', LINES)

    const firstKey = coladaState.submit.mock.calls[0][0].body.idempotencyKey
    expect(coladaState.submit.mock.calls[1][0].body.idempotencyKey).toBe(firstKey)
    expect(coladaState.submit.mock.calls[2][0].body.idempotencyKey).not.toBe(firstKey)
  })

  it('replays the same submit key after a lost committed response when self readback is completed', async () => {
    seedPrincipal()
    coladaState.listPlain
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                inspectionTaskId: 'TASK-LOST-COMMIT',
                status: 'in-progress',
                inspectionRecordId: null,
                allowedActions: ['submit-inspection'],
              },
            ],
            total: 1,
          },
        },
      })
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                inspectionTaskId: 'TASK-LOST-COMMIT',
                status: 'completed',
                inspectionRecordId: 'RECORD-COMMITTED',
                allowedActions: [],
              },
            ],
            total: 1,
          },
        },
      })
    coladaState.submit
      .mockRejectedValueOnce(new TypeError('response lost after commit'))
      .mockResolvedValueOnce({
        success: true,
        data: { inspectionRecordId: 'RECORD-COMMITTED' },
      })
    const { submitInspection } = useBusinessQualityInspectionTasks()

    await expect(submitInspection('TASK-LOST-COMMIT', LINES)).rejects.toThrow(
      'response lost after commit',
    )
    await submitInspection('TASK-LOST-COMMIT', LINES)

    expect(coladaState.submit).toHaveBeenCalledTimes(2)
    const firstKey = coladaState.submit.mock.calls[0][0].body.idempotencyKey
    expect(coladaState.submit.mock.calls[1][0].body.idempotencyKey).toBe(firstKey)
  })

  it('clears the submit intent after a determinate 422 so the next attempt uses a new key', async () => {
    seedPrincipal()
    coladaState.submit
      .mockRejectedValueOnce({ status: 422, message: 'invalid inspection result' })
      .mockResolvedValueOnce({ success: true, data: {} })
    const { submitInspection } = useBusinessQualityInspectionTasks()

    await expect(submitInspection('TASK-DETERMINATE', LINES)).rejects.toMatchObject({ status: 422 })
    const firstKey = coladaState.submit.mock.calls[0][0].body.idempotencyKey
    await submitInspection('TASK-DETERMINATE', LINES)

    expect(coladaState.submit.mock.calls[1][0].body.idempotencyKey).not.toBe(firstKey)
  })

  it('refuses to submit when the principal lacks org/env scope (no mutation, throws)', async () => {
    seedPrincipal({ environmentId: '' })
    const { submitInspection } = useBusinessQualityInspectionTasks()

    await expect(submitInspection('TASK-1', LINES)).rejects.toThrow('登录态未就绪')
    expect(coladaState.submit).not.toHaveBeenCalled()
  })

  it('refuses to submit when the principal has no id (no mutation, throws)', async () => {
    seedPrincipal({ principalId: '' })
    const { submitInspection } = useBusinessQualityInspectionTasks()

    await expect(submitInspection('TASK-1', LINES)).rejects.toThrow('登录态未就绪')
    expect(coladaState.submit).not.toHaveBeenCalled()
  })

  it('re-reads the exact task and does not submit after another inspector completed it', async () => {
    seedPrincipal()
    coladaState.listPlain.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [
            {
              inspectionTaskId: 'TASK-1',
              status: 'completed',
              inspectionRecordId: 'RECORD-OTHER',
              allowedActions: [],
            },
          ],
          total: 1,
        },
      },
    })
    const { submitInspection } = useBusinessQualityInspectionTasks()

    await expect(submitInspection('TASK-1', LINES)).rejects.toThrow('状态已被其他操作更新')

    expect(coladaState.listPlain).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'user-admin',
        inspectionTaskId: 'TASK-1',
        skip: 0,
        take: 2,
      },
      throwOnError: true,
    })
    expect(coladaState.submit).not.toHaveBeenCalled()
  })

  it('ensureAllLoaded paginates with take <= 200 when total exceeds the backend cap', async () => {
    // 回归：total > 200 时不得把 take 直接扩到 total（后端验证器上限 200 会整段失败），
    // 而是受限分页迭代聚合全量。
    seedPrincipal()
    const taskAt = (i: number) => ({ inspectionTaskId: `T${i}`, sourceType: 'receiving' })
    // 基础查询已加载前 200 条，total=450。
    coladaState.dataById.set('listBusinessConsoleQualityInspectionTasks', {
      value: {
        success: true,
        data: { items: Array.from({ length: 200 }, (_, i) => taskAt(i)), total: 450 },
      },
    })
    coladaState.listPlain.mockImplementation(
      async ({ query }: { query: { skip: number; take: number } }) => ({
        data: {
          success: true,
          data: {
            items: Array.from({ length: Math.min(query.take, 450 - query.skip) }, (_, i) =>
              taskAt(query.skip + i),
            ),
            total: 450,
          },
        },
      }),
    )

    const { ensureAllLoaded } = useBusinessQualityInspectionTasks()
    const all = await ensureAllLoaded()

    expect(all).toHaveLength(450)
    // 每次分页请求 take 都不超上限 200。
    expect(coladaState.listPlain).toHaveBeenCalledTimes(2)
    for (const call of coladaState.listPlain.mock.calls) {
      expect(call[0].query.take).toBeLessThanOrEqual(200)
    }
    expect(coladaState.listPlain.mock.calls[0][0].query.skip).toBe(200)
    expect(coladaState.listPlain.mock.calls[1][0].query.skip).toBe(400)
  })

  it('advances the server offset by raw page rows while deduplicating overlapping task ids', async () => {
    seedPrincipal()
    coladaState.dataById.set('listBusinessConsoleQualityInspectionTasks', {
      value: {
        success: true,
        data: { items: [{ inspectionTaskId: 'T1' }], total: 4 },
      },
    })
    coladaState.listPlain
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [{ inspectionTaskId: 'T1' }, { inspectionTaskId: 'T2' }],
            total: 4,
          },
        },
      })
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: { items: [{ inspectionTaskId: 'T3' }], total: 4 },
        },
      })
    const result = useBusinessQualityInspectionTasks()

    await result.loadMore()
    await result.loadMore()

    expect(result.tasks.value.map((task) => task.inspectionTaskId)).toEqual(['T1', 'T2', 'T3'])
    expect(coladaState.listPlain.mock.calls.map(([request]) => request.query.skip)).toEqual([1, 3])
    expect(result.hasMore.value).toBe(false)
  })

  it('makes progress across a duplicate-only page without requesting the same offset again', async () => {
    seedPrincipal()
    coladaState.dataById.set('listBusinessConsoleQualityInspectionTasks', {
      value: {
        success: true,
        data: { items: [{ inspectionTaskId: 'T1' }], total: 3 },
      },
    })
    coladaState.listPlain
      .mockResolvedValueOnce({
        data: { success: true, data: { items: [{ inspectionTaskId: 'T1' }], total: 3 } },
      })
      .mockResolvedValueOnce({
        data: { success: true, data: { items: [{ inspectionTaskId: 'T2' }], total: 3 } },
      })
    const result = useBusinessQualityInspectionTasks()

    await result.loadMore()
    await result.loadMore()

    expect(coladaState.listPlain.mock.calls.map(([request]) => request.query.skip)).toEqual([1, 2])
    expect(result.tasks.value.map((task) => task.inspectionTaskId)).toEqual(['T1', 'T2'])
    expect(result.hasMore.value).toBe(false)
  })

  it('keeps all loaded rows on refresh failure, exposes the real lifecycle, and replaces on success', async () => {
    seedPrincipal()
    const taskPage = (prefix: string, start: number) =>
      Array.from({ length: 20 }, (_, index) => ({
        inspectionTaskId: `${prefix}-${start + index}`,
      }))
    coladaState.dataById.set(
      'listBusinessConsoleQualityInspectionTasks',
      shallowRef({
        success: true,
        data: { items: taskPage('OLD', 0), total: 40 },
      }),
    )
    coladaState.listPlain.mockResolvedValueOnce({
      data: { success: true, data: { items: taskPage('OLD', 20), total: 40 } },
    })
    const result = useBusinessQualityInspectionTasks()
    await result.loadMore()
    expect(result.tasks.value).toHaveLength(40)

    let rejectRefresh!: (reason?: unknown) => void
    coladaState.refetchById.get('listBusinessConsoleQualityInspectionTasks')!.mockReturnValueOnce(
      new Promise((_resolve, reject) => {
        rejectRefresh = reject
      }),
    )
    const refreshFailure = new Error('quality refresh failed')
    const failedRefresh = result.refresh()
    expect(result.refreshing.value).toBe(true)
    expect(result.tasks.value).toHaveLength(40)
    rejectRefresh(refreshFailure)
    await expect(failedRefresh).rejects.toBe(refreshFailure)
    expect(result.refreshing.value).toBe(false)
    expect(result.tasks.value).toHaveLength(40)

    const freshPage = taskPage('NEW', 0)
    coladaState.refetchById
      .get('listBusinessConsoleQualityInspectionTasks')!
      .mockImplementationOnce(async () => {
        coladaState.dataById.get('listBusinessConsoleQualityInspectionTasks')!.value = {
          success: true,
          data: { items: freshPage, total: 20 },
        }
        await nextTick()
      })
    const successfulRefresh = result.refresh()
    expect(result.refreshing.value).toBe(true)
    await successfulRefresh
    await nextTick()
    expect(result.refreshing.value).toBe(false)
    expect(result.tasks.value).toEqual(freshPage)
    expect(result.hasMore.value).toBe(false)
  })

  it('keeps the complete loaded snapshot when refresh resolves with success:false', async () => {
    seedPrincipal()
    const taskPage = (start: number) =>
      Array.from({ length: 20 }, (_, index) => ({ inspectionTaskId: `OLD-${start + index}` }))
    coladaState.dataById.set(
      'listBusinessConsoleQualityInspectionTasks',
      shallowRef({ success: true, data: { items: taskPage(0), total: 40 } }),
    )
    coladaState.listPlain.mockResolvedValueOnce({
      data: { success: true, data: { items: taskPage(20), total: 40 } },
    })
    const result = useBusinessQualityInspectionTasks()
    await result.loadMore()

    coladaState.refetchById
      .get('listBusinessConsoleQualityInspectionTasks')!
      .mockImplementationOnce(async () => {
        coladaState.dataById.get('listBusinessConsoleQualityInspectionTasks')!.value = {
          success: false,
          message: 'refresh rejected',
        }
        await nextTick()
      })
    await result.refresh()
    await nextTick()

    expect(result.tasks.value).toHaveLength(40)
    expect(result.total.value).toBe(40)
    expect(result.hasFailedResponse.value).toBe(true)
  })

  it('unbinds the previous base response immediately when any server filter changes', async () => {
    seedPrincipal()
    coladaState.dataById.set('listBusinessConsoleQualityInspectionTasks', {
      value: {
        success: true,
        data: { items: [{ inspectionTaskId: 'OLD-PENDING' }], total: 1 },
      },
    })
    const result = useBusinessQualityInspectionTasks()
    expect(result.tasks.value.map((task) => task.inspectionTaskId)).toEqual(['OLD-PENDING'])

    result.filters.status = 'in-progress'
    await nextTick()

    expect(result.tasks.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)
  })

  it('exposes success:false and malformed raw task responses as failures instead of empty success', async () => {
    seedPrincipal()
    coladaState.dataById.set('listBusinessConsoleQualityInspectionTasks', {
      value: { success: false, message: '待检任务查询失败' },
    })

    const result = useBusinessQualityInspectionTasks()

    expect(result.tasks.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)

    coladaState.dataById.get('listBusinessConsoleQualityInspectionTasks')!.value = {
      data: { items: [], total: 0 },
    }
    await nextTick()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
  })

  it('unbinds base and already aggregated extra tasks when the org/env scope changes', async () => {
    seedPrincipal()
    coladaState.dataById.set('listBusinessConsoleQualityInspectionTasks', {
      value: {
        success: true,
        data: {
          items: [{ inspectionTaskId: 'OLD-1', sourceType: 'receiving' }],
          total: 2,
        },
      },
    })
    coladaState.listPlain.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ inspectionTaskId: 'OLD-2', sourceType: 'receiving' }],
          total: 2,
        },
      },
    })

    const result = useBusinessQualityInspectionTasks()
    await result.ensureAllLoaded()
    expect(result.tasks.value).toHaveLength(2)
    expect(result.hasSuccessfulResponse.value).toBe(true)

    seedPrincipal({ organizationId: 'org-002', environmentId: 'env-prod' })
    await nextTick()

    expect(result.tasks.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)
    expect(result.lastUpdatedAt.value).toBeNull()
  })

  it('discards an in-flight extra page when it resolves after the org/env scope changes', async () => {
    seedPrincipal()
    coladaState.dataById.set('listBusinessConsoleQualityInspectionTasks', {
      value: {
        success: true,
        data: {
          items: [{ inspectionTaskId: 'OLD-1', sourceType: 'receiving' }],
          total: 2,
        },
      },
    })
    const oldPage = deferred<{
      data: {
        success: true
        data: {
          items: Array<{ inspectionTaskId: string; sourceType: string }>
          total: number
        }
      }
    }>()
    coladaState.listPlain.mockReturnValue(oldPage.promise)

    const result = useBusinessQualityInspectionTasks()
    const loadPromise = result.ensureAllLoaded()
    expect(coladaState.listPlain).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 1,
      }),
    })

    seedPrincipal({ organizationId: 'org-002', environmentId: 'env-prod' })
    await nextTick()
    oldPage.resolve({
      data: {
        success: true,
        data: {
          items: [{ inspectionTaskId: 'OLD-2', sourceType: 'receiving' }],
          total: 2,
        },
      },
    })

    await expect(loadPromise).resolves.toEqual([])
    expect(result.tasks.value).toEqual([])
    expect(result.total.value).toBe(0)
  })

  it('fails closed when an extra task page resolves with success:false', async () => {
    seedPrincipal()
    coladaState.dataById.set('listBusinessConsoleQualityInspectionTasks', {
      value: {
        success: true,
        data: {
          items: [{ inspectionTaskId: 'T1', sourceType: 'receiving' }],
          total: 2,
        },
      },
    })
    coladaState.listPlain.mockResolvedValue({
      data: { success: false, message: '下一页查询失败' },
    })

    const result = useBusinessQualityInspectionTasks()

    await expect(result.ensureAllLoaded()).rejects.toThrow('下一页查询失败')
    expect(result.tasks.value).toHaveLength(1)
  })
})
