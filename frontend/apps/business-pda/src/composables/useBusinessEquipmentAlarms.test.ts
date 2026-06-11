import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { listBusinessConsoleEquipmentAlarmsQueryOptions } from '@nerv-iip/api-client'
import { useBusinessEquipmentAlarms } from './useBusinessEquipmentAlarms'
import { useAuthStore } from '@/stores/auth'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleEquipmentAlarmsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleEquipmentAlarms' }],
    query: vi.fn(),
  })),
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

    expect(coladaState.queryOptionsById.get('listBusinessConsoleEquipmentAlarms')?.enabled).toBe(false)
    expect(alarms.value).toEqual([])
  })

  it('enables the query with the principal scope and exposes alarms/total/pending/error/refresh', () => {
    seedPrincipal()
    coladaState.queryDataById.set('listBusinessConsoleEquipmentAlarms', {
      success: true,
      data: {
        items: [{ alarmEventId: 'alarm-1', deviceAssetId: 'DEV-OIL-01', severity: 'critical' }],
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
    expect(coladaState.queryOptionsById.get('listBusinessConsoleEquipmentAlarms')?.enabled).toBe(true)
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
})
