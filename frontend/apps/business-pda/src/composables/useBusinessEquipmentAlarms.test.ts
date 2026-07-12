import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
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
import { useAuthStore } from '@/stores/auth'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  mutateAsync: vi.fn(
    async (_payload: { path: { alarmEventId: string }; body: Record<string, unknown> }) => ({
      success: true,
    }),
  ),
  invalidateQueries: vi.fn(async () => {}),
  lastMutationConfig: undefined as { onSuccess?: () => void } | undefined,
}))

// key 里带 _status，区分「全量」列表读与 useUnacknowledgedAlarmCount 的 status=raised 读。
vi.mock('@nerv-iip/api-client', () => ({
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

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
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
  auth.$patch({
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

const ALL_KEY = 'listBusinessConsoleEquipmentAlarms:all'
const RAISED_KEY = 'listBusinessConsoleEquipmentAlarms:raised'

describe('useBusinessEquipmentAlarms', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
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

  it('acknowledge posts the caller-supplied stable atUtc + actor, and wires list invalidation', async () => {
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
      },
    })
    coladaState.lastMutationConfig?.onSuccess?.()
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({ predicate: expect.any(Function) })
  })

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
})

describe('useUnacknowledgedAlarmCount', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
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
})
