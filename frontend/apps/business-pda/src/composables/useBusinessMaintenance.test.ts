import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { useBusinessMaintenance } from './useBusinessMaintenance'
import { useAuthStore } from '@/stores/auth'

const coladaState = vi.hoisted(() => ({
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  mutate: {
    createWorkOrder: vi.fn(),
    recordInspection: vi.fn(),
  },
}))

// The composable consumes the Maintenance facade through the curated
// `@nerv-iip/api-client` barrel; mock it here. The auth-API functions are also
// stubbed because `@/stores/auth` lazily references them (never called in these
// tests — we only `$patch` the principal).
vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMaintenanceWorkOrders' }],
    query: vi.fn(),
  })),
  createBusinessConsoleMaintenanceWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
    _tag: 'createWorkOrder',
  })),
  listBusinessConsoleMaintenanceInspectionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMaintenanceInspections' }],
    query: vi.fn(),
  })),
  recordBusinessConsoleMaintenanceInspectionMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
    _tag: 'recordInspection',
  })),
  listBusinessConsoleMaintenancePlansQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMaintenancePlans' }],
    query: vi.fn(),
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
    return {
      data: shallowRef(undefined),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useMutation: vi.fn((options: { mutation?: unknown }) => {
    // Identify which mutation by structural tag injected by the mocked options.
    const tag = (options as { _tag?: string })._tag
    const mutateAsync
      = tag === 'createWorkOrder'
        ? coladaState.mutate.createWorkOrder
        : coladaState.mutate.recordInspection
    return {
      mutateAsync,
      isLoading: shallowRef(false),
      error: shallowRef(),
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

describe('useBusinessMaintenance', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryOptionsById.clear()
  })

  it('keeps every list query disabled when the principal has no org/env scope', () => {
    useBusinessMaintenance()

    expect(coladaState.queryOptionsById.get('listBusinessConsoleMaintenanceWorkOrders')?.enabled).toBe(false)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMaintenanceInspections')?.enabled).toBe(false)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMaintenancePlans')?.enabled).toBe(false)
  })

  it('enables list queries once the principal carries an org/env scope', () => {
    seedPrincipal()
    useBusinessMaintenance()

    expect(coladaState.queryOptionsById.get('listBusinessConsoleMaintenanceWorkOrders')?.enabled).toBe(true)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMaintenanceInspections')?.enabled).toBe(true)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMaintenancePlans')?.enabled).toBe(true)
  })

  it('injects org/env/openedBy into the work-order create body — caller cannot override them', async () => {
    seedPrincipal()
    const { createWorkOrder } = useBusinessMaintenance()

    await createWorkOrder({
      // Hostile caller attempts to override injected fields via `as never`.
      organizationId: 'EVIL',
      environmentId: 'EVIL',
      openedBy: 'evil',
      deviceAssetId: 'D1',
      priority: 'high',
      assetUnavailableReason: 'x',
    } as never)

    expect(coladaState.mutate.createWorkOrder).toHaveBeenCalledTimes(1)
    const arg = coladaState.mutate.createWorkOrder.mock.calls[0][0]
    expect(arg.body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      openedBy: 'admin',
      deviceAssetId: 'D1',
      priority: 'high',
      assetUnavailableReason: 'x',
    })
    // Injection wins over hostile input.
    expect(arg.body.organizationId).toBe('org-001')
    expect(arg.body.openedBy).toBe('admin')
  })

  it('injects org/env/inspector/inspectedAtUtc into the inspection body — caller cannot override them', async () => {
    seedPrincipal()
    const { recordInspection } = useBusinessMaintenance()

    await recordInspection({
      organizationId: 'EVIL',
      environmentId: 'EVIL',
      inspector: 'evil',
      inspectedAtUtc: '1999-01-01T00:00:00.000Z',
      planId: 'P1',
      result: 'pass',
    } as never)

    expect(coladaState.mutate.recordInspection).toHaveBeenCalledTimes(1)
    const arg = coladaState.mutate.recordInspection.mock.calls[0][0]
    expect(arg.body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      inspector: 'admin',
      planId: 'P1',
      result: 'pass',
    })
    expect(arg.body.organizationId).toBe('org-001')
    expect(arg.body.inspector).toBe('admin')
    expect(arg.body.inspectedAtUtc).not.toBe('1999-01-01T00:00:00.000Z')
    expect(typeof arg.body.inspectedAtUtc).toBe('string')
  })

  it('refuses createWorkOrder when the principal lacks org/env scope (no mutation, throws)', async () => {
    // No principal seeded → org/env empty → scope not ready.
    const { createWorkOrder } = useBusinessMaintenance()

    await expect(
      createWorkOrder({ deviceAssetId: 'D1', priority: 'high', assetUnavailableReason: 'x' } as never),
    ).rejects.toThrow('登录态未就绪')
    expect(coladaState.mutate.createWorkOrder).not.toHaveBeenCalled()
  })

  it('refuses recordInspection when the principal lacks org/env scope (no mutation, throws)', async () => {
    // Principal restored but missing environmentId → scope not ready.
    seedPrincipal({ environmentId: '' })
    const { recordInspection } = useBusinessMaintenance()

    await expect(
      recordInspection({ planId: 'P1', result: 'pass' } as never),
    ).rejects.toThrow('登录态未就绪')
    expect(coladaState.mutate.recordInspection).not.toHaveBeenCalled()
  })
})
