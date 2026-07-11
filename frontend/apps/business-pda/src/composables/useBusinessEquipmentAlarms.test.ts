import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  acknowledgeBusinessConsoleEquipmentAlarmMutationOptions,
  listBusinessConsoleEquipmentAlarmsQueryOptions,
  shelveBusinessConsoleEquipmentAlarmMutationOptions,
} from '@nerv-iip/api-client'
import { useBusinessEquipmentAlarms } from './useBusinessEquipmentAlarms'
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

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleEquipmentAlarmsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleEquipmentAlarms' }],
    query: vi.fn(),
  })),
  acknowledgeBusinessConsoleEquipmentAlarmMutationOptions: vi.fn(() => ({})),
  shelveBusinessConsoleEquipmentAlarmMutationOptions: vi.fn(() => ({})),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
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

describe('useBusinessEquipmentAlarms', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
  })

  it('keeps the alarms query disabled when the principal carries no org/env scope', () => {
    const { alarms } = useBusinessEquipmentAlarms()

    expect(coladaState.queryOptionsById.get('listBusinessConsoleEquipmentAlarms')?.enabled).toBe(
      false,
    )
    expect(alarms.value).toEqual([])
  })

  it('enables the query with the principal scope and exposes alarms/total/pending/error/refresh', () => {
    seedPrincipal()
    coladaState.queryDataById.set('listBusinessConsoleEquipmentAlarms', {
      success: true,
      data: {
        items: [
          {
            alarmEventId: 'alarm-1',
            deviceAssetId: 'DEV-OIL-01',
            severity: 'critical',
            status: 'raised',
          },
        ],
        total: 1,
      },
    })

    const result = useBusinessEquipmentAlarms()

    expect(listBusinessConsoleEquipmentAlarmsQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
      }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleEquipmentAlarms')?.enabled).toBe(
      true,
    )
    expect(result.alarms.value).toHaveLength(1)
    expect(result.total.value).toBe(1)
    expect(result.pending).toBeDefined()
    expect(result.error).toBeDefined()
    expect(typeof result.refresh).toBe('function')
  })

  it('passes an optional deviceAssetId filter into the query and omits it when empty', () => {
    seedPrincipal()
    const { filters } = useBusinessEquipmentAlarms()

    expect(listBusinessConsoleEquipmentAlarmsQueryOptions).toHaveBeenLastCalledWith({
      query: expect.not.objectContaining({ deviceAssetId: expect.anything() }),
    })

    filters.deviceAssetId = 'DEV-OIL-01'
    useBusinessEquipmentAlarms({ deviceAssetId: 'DEV-OIL-01' })
    expect(listBusinessConsoleEquipmentAlarmsQueryOptions).toHaveBeenLastCalledWith({
      query: expect.objectContaining({ deviceAssetId: 'DEV-OIL-01' }),
    })
  })

  it('sorts alarms 未确认 > 已搁置 > 已确认 > 已清除, newest-first within a tier', () => {
    seedPrincipal()
    coladaState.queryDataById.set('listBusinessConsoleEquipmentAlarms', {
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

    const { alarms, unacknowledgedCount } = useBusinessEquipmentAlarms()

    expect(alarms.value.map((a) => a.alarmEventId)).toEqual([
      'raised-new',
      'raised-old',
      'shelved',
      'ack',
      'cleared',
    ])
    // 未确认计数只数 raised（不含已搁置/已确认/已清除）
    expect(unacknowledgedCount.value).toBe(2)
  })

  it('acknowledge posts scope + actor + a timestamp and invalidates the list on success', async () => {
    seedPrincipal()
    const { acknowledge } = useBusinessEquipmentAlarms()
    expect(acknowledgeBusinessConsoleEquipmentAlarmMutationOptions).toHaveBeenCalled()

    await acknowledge('alarm-9')

    expect(coladaState.mutateAsync).toHaveBeenCalledWith({
      path: { alarmEventId: 'alarm-9' },
      body: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        acknowledgedBy: 'admin',
        acknowledgedAtUtc: expect.any(String),
      }),
    })

    // onSuccess wiring invalidates the shared list query
    coladaState.lastMutationConfig?.onSuccess?.()
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({ predicate: expect.any(Function) })
  })

  it('shelve posts durationMinutes + actor and includes the reason only when provided', async () => {
    seedPrincipal()
    const { shelve } = useBusinessEquipmentAlarms()

    await shelve('alarm-7', 120)
    expect(coladaState.mutateAsync).toHaveBeenLastCalledWith({
      path: { alarmEventId: 'alarm-7' },
      body: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        durationMinutes: 120,
        shelvedBy: 'admin',
        shelvedAtUtc: expect.any(String),
      }),
    })
    expect(coladaState.mutateAsync.mock.calls.at(-1)?.[0].body).not.toHaveProperty('reason')

    await shelve('alarm-7', 30, '  等待备件  ')
    expect(coladaState.mutateAsync.mock.calls.at(-1)?.[0].body).toMatchObject({
      reason: '等待备件',
    })
  })
})
