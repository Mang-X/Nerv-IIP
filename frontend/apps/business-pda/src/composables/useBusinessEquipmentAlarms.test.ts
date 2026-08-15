import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, shallowRef, type ShallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  acknowledgeBusinessConsoleEquipmentAlarmMutationOptions,
  listBusinessConsoleEquipmentAlarmsQueryOptions,
  shelveBusinessConsoleEquipmentAlarmMutationOptions,
} from '@nerv-iip/api-client'
import {
  useBusinessEquipmentAlarms,
  useUnacknowledgedAlarmCount,
} from './useBusinessEquipmentAlarms'
import { acquirePendingBusinessIntent } from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryDataRefById: new Map<string, ShallowRef<unknown>>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  mutateAsync: vi.fn(
    async (_payload: { path: { alarmEventId: string }; body: Record<string, unknown> }) => ({
      success: true,
    }),
  ),
  invalidateQueries: vi.fn(async () => {}),
  lastMutationConfig: undefined as { onSuccess?: () => void } | undefined,
  listPlain: vi.fn(),
}))

// key 里带 _status，区分「全量」列表读与 useUnacknowledgedAlarmCount 的 status=raised 读。
vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleEquipmentAlarms: coladaState.listPlain,
  confirmBusinessConsoleOperation: vi.fn(async (value) => value),
  listBusinessConsoleEquipmentAlarmsQueryOptions: vi.fn(
    (opts: { query?: { status?: string } }) => ({
      key: [{ _id: 'listBusinessConsoleEquipmentAlarms', _status: opts?.query?.status ?? 'all' }],
      query: opts?.query,
    }),
  ),
  acknowledgeBusinessConsoleEquipmentAlarmMutationOptions: vi.fn(() => ({})),
  shelveBusinessConsoleEquipmentAlarmMutationOptions: vi.fn(() => ({})),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory: () => { key?: unknown[] }) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key)
      ? (options.key[0] as { _id?: string; _status?: string })
      : undefined
    const id = `${key?._id ?? ''}:${key?._status ?? 'all'}`
    coladaState.queryOptionsById.set(id, options as { enabled?: boolean })
    const data = shallowRef(coladaState.queryDataById.get(id))
    coladaState.queryDataRefById.set(id, data)

    return {
      data,
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(async () => {}),
    }
  }),
  useMutation: vi.fn((config: { onSuccess?: () => void }) => {
    coladaState.lastMutationConfig = config
    return { mutateAsync: coladaState.mutateAsync, isLoading: shallowRef(false) }
  }),
  useQueryCache: vi.fn(() => ({ invalidateQueries: coladaState.invalidateQueries })),
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

const ALL_KEY = 'listBusinessConsoleEquipmentAlarms:all'
const RAISED_KEY = 'listBusinessConsoleEquipmentAlarms:raised'

describe('useBusinessEquipmentAlarms', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionStorage.clear()
    coladaState.queryDataById.clear()
    coladaState.queryDataRefById.clear()
    coladaState.queryOptionsById.clear()
    coladaState.listPlain.mockImplementation(
      async ({ query }: { query: { alarmEventId?: string } }) => ({
        data: {
          success: true,
          data: {
            items: query.alarmEventId
              ? [{ alarmEventId: query.alarmEventId, status: 'raised' }]
              : [],
            total: query.alarmEventId ? 1 : 0,
          },
        },
      }),
    )
  })

  it('keeps the alarms list query disabled when the principal carries no org/env scope', () => {
    const { alarms } = useBusinessEquipmentAlarms()

    expect(coladaState.queryOptionsById.get(ALL_KEY)?.enabled).toBe(false)
    expect(alarms.value).toEqual([])
  })

  it('issues a single scoped list read (server orders lifecycle; no status filter on the page read)', () => {
    seedPrincipal()
    useBusinessEquipmentAlarms()

    expect(listBusinessConsoleEquipmentAlarmsQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
      }),
    })
    // page read carries no status filter (relies on server-side lifecycle ordering)
    expect(listBusinessConsoleEquipmentAlarmsQueryOptions).toHaveBeenLastCalledWith({
      query: expect.not.objectContaining({ status: expect.anything() }),
    })
    expect(coladaState.queryOptionsById.get(ALL_KEY)?.enabled).toBe(true)
  })

  it('exposes the real scope and server total for the alarm list', () => {
    seedPrincipal()
    coladaState.queryDataById.set(ALL_KEY, {
      success: true,
      data: { items: [{ alarmEventId: 'a-1' }], total: 4 },
    })

    const result = useBusinessEquipmentAlarms()

    expect(result.organizationId.value).toBe('org-001')
    expect(result.environmentId.value).toBe('env-dev')
    expect(result.scopeReady.value).toBe(true)
    expect(result.total.value).toBe(4)
  })

  it('re-affirms the server lifecycle order client-side: 未确认 > 已搁置 > 已确认 > 已清除, newest-first', () => {
    seedPrincipal()
    coladaState.queryDataById.set(ALL_KEY, {
      success: true,
      data: {
        items: [
          { alarmEventId: 'ack', status: 'acknowledged', raisedAtUtc: '2026-06-10T09:00:00Z' },
          { alarmEventId: 'cleared', status: 'cleared', raisedAtUtc: '2026-06-10T12:00:00Z' },
          { alarmEventId: 'raised-old', status: 'raised', raisedAtUtc: '2026-06-10T08:00:00Z' },
          { alarmEventId: 'shelved', status: 'shelved', raisedAtUtc: '2026-06-10T07:00:00Z' },
          { alarmEventId: 'raised-new', status: 'raised', raisedAtUtc: '2026-06-10T11:00:00Z' },
        ],
        total: 5,
      },
    })

    const { alarms } = useBusinessEquipmentAlarms()

    expect(alarms.value.map((a) => a.alarmEventId)).toEqual([
      'raised-new',
      'raised-old',
      'shelved',
      'ack',
      'cleared',
    ])
  })

  it('acknowledge posts the stable intent timestamp, actor, and idempotency key', async () => {
    seedPrincipal()
    const { acknowledge } = useBusinessEquipmentAlarms()

    await acknowledge('alarm-9', '2026-06-10T08:30:00.000Z')

    expect(coladaState.mutateAsync).toHaveBeenCalledWith({
      path: { alarmEventId: 'alarm-9' },
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        acknowledgedAtUtc: '2026-06-10T08:30:00.000Z',
        acknowledgedBy: 'admin',
        idempotencyKey: expect.any(String),
      },
    })
    coladaState.lastMutationConfig?.onSuccess?.()
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({ predicate: expect.any(Function) })
  })

  it('re-reads the exact alarm and does not acknowledge after it was cleared', async () => {
    seedPrincipal()
    coladaState.listPlain.mockResolvedValue({
      data: {
        success: true,
        data: { items: [{ alarmEventId: 'alarm-9', status: 'cleared' }], total: 1 },
      },
    })
    const { acknowledge } = useBusinessEquipmentAlarms()

    await expect(acknowledge('alarm-9', '2026-06-10T08:30:00.000Z')).rejects.toThrow(
      '状态已被其他操作更新',
    )

    expect(coladaState.listPlain).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        alarmEventId: 'alarm-9',
        skip: 0,
        take: 2,
      },
      throwOnError: true,
    })
    expect(coladaState.mutateAsync).not.toHaveBeenCalled()
  })

  it('reuses the acknowledge intent key and timestamp after an indeterminate failure', async () => {
    seedPrincipal()
    coladaState.mutateAsync.mockRejectedValueOnce(new TypeError('network failed'))
    const { acknowledge } = useBusinessEquipmentAlarms()

    await expect(acknowledge('alarm-9', '2026-06-10T08:30:00.000Z')).rejects.toThrow(
      'network failed',
    )
    const first = coladaState.mutateAsync.mock.calls.at(-1)?.[0]

    await acknowledge('alarm-9', '2026-06-10T09:45:00.000Z')
    const retry = coladaState.mutateAsync.mock.calls.at(-1)?.[0]

    expect(retry).toEqual(first)
    expect(retry?.body.idempotencyKey).toEqual(expect.any(String))
    expect(retry?.body.acknowledgedAtUtc).toBe('2026-06-10T08:30:00.000Z')
  })

  it.each(['acknowledge', 'shelve'] as const)(
    'clears the %s intent after a determinate 422 so the next attempt uses a new key',
    async (action) => {
      seedPrincipal()
      coladaState.mutateAsync.mockRejectedValueOnce({ status: 422, message: 'invalid request' })
      const { acknowledge, shelve } = useBusinessEquipmentAlarms()

      if (action === 'acknowledge') {
        await expect(
          acknowledge('alarm-determinate-ack', '2026-06-10T08:30:00.000Z'),
        ).rejects.toMatchObject({ status: 422 })
        const firstKey = coladaState.mutateAsync.mock.calls.at(-1)?.[0].body.idempotencyKey

        await acknowledge('alarm-determinate-ack', '2026-06-10T08:30:00.000Z')
        expect(coladaState.mutateAsync.mock.calls.at(-1)?.[0].body.idempotencyKey).not.toBe(
          firstKey,
        )
        return
      }

      await expect(
        shelve('alarm-determinate-shelve', 120, '2026-06-10T08:30:00.000Z', 'shelve-key-1'),
      ).rejects.toMatchObject({ status: 422 })
      await shelve('alarm-determinate-shelve', 120, '2026-06-10T08:30:00.000Z', 'shelve-key-2')

      expect(coladaState.mutateAsync.mock.calls.at(-1)?.[0].body.idempotencyKey).toBe(
        'shelve-key-2',
      )
    },
  )

  it.each(['acknowledge', 'shelve'] as const)(
    'falls back to the caller-frozen %s payload when a restored intent has no snapshot',
    async (action) => {
      seedPrincipal()
      const alarmEventId = `alarm-missing-snapshot-${action}`
      const atUtc = '2026-06-10T08:30:00.000Z'
      const scope = {
        principalId: 'user-admin',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        operationType: `iiot.alarm.${action}`,
        payloadFingerprint:
          action === 'acknowledge'
            ? JSON.stringify({ alarmEventId })
            : JSON.stringify({ alarmEventId, durationMinutes: 120, reason: '' }),
      }
      acquirePendingBusinessIntent(scope, () => `restored-${action}-key`)
      const { acknowledge, shelve } = useBusinessEquipmentAlarms()

      if (action === 'acknowledge') {
        await acknowledge(alarmEventId, atUtc)
        expect(coladaState.mutateAsync.mock.calls.at(-1)?.[0].body).toMatchObject({
          acknowledgedAtUtc: atUtc,
          idempotencyKey: 'restored-acknowledge-key',
        })
        return
      }

      await shelve(alarmEventId, 120, atUtc, 'restored-shelve-key')
      expect(coladaState.mutateAsync.mock.calls.at(-1)?.[0].body).toMatchObject({
        durationMinutes: 120,
        shelvedAtUtc: atUtc,
        idempotencyKey: 'restored-shelve-key',
      })
    },
  )

  it('shelve posts durationMinutes + stable atUtc + the persistent idempotencyKey; reason only when provided', async () => {
    seedPrincipal()
    const { shelve } = useBusinessEquipmentAlarms()

    await shelve('alarm-7', 120, '2026-06-10T08:30:00.000Z', 'idem-key-1')
    expect(coladaState.mutateAsync).toHaveBeenLastCalledWith({
      path: { alarmEventId: 'alarm-7' },
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        durationMinutes: 120,
        shelvedAtUtc: '2026-06-10T08:30:00.000Z',
        shelvedBy: 'admin',
        idempotencyKey: 'idem-key-1',
      },
    })
    expect(coladaState.mutateAsync.mock.calls.at(-1)?.[0].body).not.toHaveProperty('reason')

    await shelve('alarm-7', 30, '2026-06-10T09:00:00.000Z', 'idem-key-2', '  等待备件  ')
    expect(coladaState.mutateAsync.mock.calls.at(-1)?.[0].body).toMatchObject({
      reason: '等待备件',
      idempotencyKey: 'idem-key-2',
    })
  })

  it('reusing the same idempotencyKey + atUtc across a retry keeps an identical shelve payload', async () => {
    seedPrincipal()
    const { shelve } = useBusinessEquipmentAlarms()
    const atUtc = '2026-06-10T08:30:00.000Z'
    const key = 'idem-stable-1'

    await shelve('alarm-7', 120, atUtc, key)
    const first = coladaState.mutateAsync.mock.calls.at(-1)?.[0]
    await shelve('alarm-7', 120, atUtc, key) // retry: same stable key + atUtc
    const retry = coladaState.mutateAsync.mock.calls.at(-1)?.[0]

    expect(retry).toEqual(first)
    expect(retry?.body.idempotencyKey).toBe(key)
  })

  it('exposes success:false and malformed raw alarm responses as retryable failures', async () => {
    seedPrincipal()
    coladaState.queryDataById.set(ALL_KEY, {
      success: false,
      message: '报警查询失败',
    })

    const result = useBusinessEquipmentAlarms()

    expect(result.alarms.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)

    coladaState.queryDataRefById.get(ALL_KEY)!.value = { data: { items: [], total: 0 } }
    await nextTick()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
  })

  it('unbinds alarm rows, total, and freshness immediately on an org/env scope switch', async () => {
    seedPrincipal()
    coladaState.queryDataById.set(ALL_KEY, {
      success: true,
      data: { items: [{ alarmEventId: 'old-alarm', status: 'raised' }], total: 5 },
    })

    const result = useBusinessEquipmentAlarms()
    expect(result.alarms.value).toHaveLength(1)
    expect(result.total.value).toBe(5)
    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(result.lastUpdatedAt.value).not.toBeNull()

    seedPrincipal({ organizationId: 'org-002', environmentId: 'env-prod' })
    await nextTick()

    expect(result.alarms.value).toEqual([])
    expect(result.total.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)
    expect(result.lastUpdatedAt.value).toBeNull()
  })
})

describe('useUnacknowledgedAlarmCount', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
    coladaState.queryDataRefById.clear()
    coladaState.queryOptionsById.clear()
  })

  it('is disabled and zero without scope', () => {
    const { unacknowledgedCount } = useUnacknowledgedAlarmCount()
    expect(coladaState.queryOptionsById.get(RAISED_KEY)?.enabled).toBe(false)
    expect(unacknowledgedCount.value).toBe(0)
  })

  it('reads the full count from the status=raised total (not first-page items)', () => {
    seedPrincipal()
    coladaState.queryDataById.set(RAISED_KEY, {
      success: true,
      data: { items: [{ alarmEventId: 'x', status: 'raised' }], total: 137 },
    })

    const { unacknowledgedCount } = useUnacknowledgedAlarmCount()

    expect(listBusinessConsoleEquipmentAlarmsQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ status: 'raised', take: 1 }),
    })
    expect(unacknowledgedCount.value).toBe(137)
  })

  it('does not turn a failed raised-count envelope into a successful zero', () => {
    seedPrincipal()
    coladaState.queryDataById.set(RAISED_KEY, {
      success: false,
      message: '未确认报警数查询失败',
    })

    const result = useUnacknowledgedAlarmCount()

    expect(result.unacknowledgedCount.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
  })

  it('unbinds the raised count when the principal scope changes', async () => {
    seedPrincipal()
    coladaState.queryDataById.set(RAISED_KEY, {
      success: true,
      data: { items: [], total: 12 },
    })
    const result = useUnacknowledgedAlarmCount()
    expect(result.unacknowledgedCount.value).toBe(12)

    seedPrincipal({ organizationId: 'org-002', environmentId: 'env-prod' })
    await nextTick()

    expect(result.unacknowledgedCount.value).toBe(0)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)
  })
})
