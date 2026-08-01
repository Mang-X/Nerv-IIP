import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, shallowRef, type ShallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { useBusinessMaintenance } from './useBusinessMaintenance'
import { useAuthStore } from '@/stores/auth'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryDataRefById: new Map<string, ShallowRef<unknown>>(),
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
  confirmBusinessConsoleOperation: vi.fn(async (value) => value),
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
    const data = shallowRef(coladaState.queryDataById.get(id))
    coladaState.queryDataRefById.set(id, data)
    return {
      data,
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useMutation: vi.fn((options: { mutation?: unknown }) => {
    // Identify which mutation by structural tag injected by the mocked options.
    const tag = (options as { _tag?: string })._tag
    const mutateAsync =
      tag === 'createWorkOrder'
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

describe('useBusinessMaintenance', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionStorage.clear()
    coladaState.queryDataById.clear()
    coladaState.queryDataRefById.clear()
    coladaState.queryOptionsById.clear()
  })

  it('keeps every list query disabled when the principal has no org/env scope', () => {
    useBusinessMaintenance()

    expect(
      coladaState.queryOptionsById.get('listBusinessConsoleMaintenanceWorkOrders')?.enabled,
    ).toBe(false)
    expect(
      coladaState.queryOptionsById.get('listBusinessConsoleMaintenanceInspections')?.enabled,
    ).toBe(false)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMaintenancePlans')?.enabled).toBe(
      false,
    )
  })

  it('enables list queries once the principal carries an org/env scope', () => {
    seedPrincipal()
    const result = useBusinessMaintenance()

    expect(
      coladaState.queryOptionsById.get('listBusinessConsoleMaintenanceWorkOrders')?.enabled,
    ).toBe(true)
    expect(
      coladaState.queryOptionsById.get('listBusinessConsoleMaintenanceInspections')?.enabled,
    ).toBe(true)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMaintenancePlans')?.enabled).toBe(
      true,
    )
    expect(result.organizationId.value).toBe('org-001')
    expect(result.environmentId.value).toBe('env-dev')
    expect(result.scopeReady.value).toBe(true)
    expect(result.workOrdersTotal.value).toBe(0)
  })

  it('keeps task paging at 20 while auxiliary inspection and plan history retain 100 rows', () => {
    seedPrincipal()
    const result = useBusinessMaintenance()

    expect(result.workOrderFilters.take).toBe(20)
    expect(result.inspectionFilters.take).toBe(100)
    expect(result.planFilters.take).toBe(100)
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

  it('clears a work-order intent after a determinate 422 so a corrected attempt can use a new key', async () => {
    seedPrincipal()
    coladaState.mutate.createWorkOrder
      .mockRejectedValueOnce({ status: 422, message: 'invalid request' })
      .mockResolvedValueOnce({ success: true, data: {} })
    const { createWorkOrder } = useBusinessMaintenance()
    const intent = {
      deviceAssetId: 'D-DETERMINATE',
      priority: 'high',
      assetUnavailableReason: 'bearing damage',
    } as const

    await expect(
      createWorkOrder({ ...intent, idempotencyKey: 'maintenance-key-1' }),
    ).rejects.toMatchObject({ status: 422 })
    await createWorkOrder({ ...intent, idempotencyKey: 'maintenance-key-2' })

    expect(coladaState.mutate.createWorkOrder.mock.calls[0][0].body.idempotencyKey).toBe(
      'maintenance-key-1',
    )
    expect(coladaState.mutate.createWorkOrder.mock.calls[1][0].body.idempotencyKey).toBe(
      'maintenance-key-2',
    )
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
      createWorkOrder({
        deviceAssetId: 'D1',
        priority: 'high',
        assetUnavailableReason: 'x',
      } as never),
    ).rejects.toThrow('登录态未就绪')
    expect(coladaState.mutate.createWorkOrder).not.toHaveBeenCalled()
  })

  it('refuses recordInspection when the principal lacks org/env scope (no mutation, throws)', async () => {
    // Principal restored but missing environmentId → scope not ready.
    seedPrincipal({ environmentId: '' })
    const { recordInspection } = useBusinessMaintenance()

    await expect(recordInspection({ planId: 'P1', result: 'pass' } as never)).rejects.toThrow(
      '登录态未就绪',
    )
    expect(coladaState.mutate.recordInspection).not.toHaveBeenCalled()
  })

  it('marks every Maintenance list success:false or malformed raw response as failed', () => {
    seedPrincipal()
    coladaState.queryDataById.set('listBusinessConsoleMaintenanceWorkOrders', {
      success: false,
      message: '维修工单查询失败',
    })
    coladaState.queryDataById.set('listBusinessConsoleMaintenanceInspections', [])
    coladaState.queryDataById.set('listBusinessConsoleMaintenancePlans', {
      data: { items: [], total: 0 },
    })

    const result = useBusinessMaintenance()

    expect(result.workOrders.value).toHaveLength(0)
    expect(result.workOrdersTotal.value).toBe(0)
    expect(result.workOrdersHasSuccessfulResponse.value).toBe(false)
    expect(result.workOrdersHasFailedResponse.value).toBe(true)
    expect(result.inspections.value).toHaveLength(0)
    expect(result.inspectionsTotal.value).toBe(0)
    expect(result.inspectionsHasSuccessfulResponse.value).toBe(false)
    expect(result.inspectionsHasFailedResponse.value).toBe(true)
    expect(result.plans.value).toHaveLength(0)
    expect(result.plansTotal.value).toBe(0)
    expect(result.plansHasSuccessfulResponse.value).toBe(false)
    expect(result.plansHasFailedResponse.value).toBe(true)
  })

  it('unbinds all Maintenance list projections on an org/env scope switch', async () => {
    seedPrincipal()
    for (const id of [
      'listBusinessConsoleMaintenanceWorkOrders',
      'listBusinessConsoleMaintenanceInspections',
      'listBusinessConsoleMaintenancePlans',
    ]) {
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: [{ id: `old-${id}` }], total: 6 },
      })
    }

    const result = useBusinessMaintenance()
    expect(result.workOrders.value).toHaveLength(1)
    expect(result.inspections.value).toHaveLength(1)
    expect(result.plans.value).toHaveLength(1)
    expect(result.workOrdersLastUpdatedAt.value).not.toBeNull()
    expect(result.inspectionsLastUpdatedAt.value).not.toBeNull()
    expect(result.plansLastUpdatedAt.value).not.toBeNull()

    seedPrincipal({ organizationId: 'org-002', environmentId: 'env-prod' })
    await nextTick()

    expect(result.workOrders.value).toHaveLength(0)
    expect(result.workOrdersTotal.value).toBe(0)
    expect(result.workOrdersHasSuccessfulResponse.value).toBe(false)
    expect(result.inspections.value).toHaveLength(0)
    expect(result.inspectionsTotal.value).toBe(0)
    expect(result.inspectionsHasSuccessfulResponse.value).toBe(false)
    expect(result.plans.value).toHaveLength(0)
    expect(result.plansTotal.value).toBe(0)
    expect(result.plansHasSuccessfulResponse.value).toBe(false)
    expect(result.workOrdersLastUpdatedAt.value).toBeNull()
    expect(result.inspectionsLastUpdatedAt.value).toBeNull()
    expect(result.plansLastUpdatedAt.value).toBeNull()
  })
})
