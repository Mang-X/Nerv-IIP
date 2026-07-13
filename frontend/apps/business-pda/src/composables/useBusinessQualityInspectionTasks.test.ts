import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { useBusinessQualityInspectionTasks } from './useBusinessQualityInspectionTasks'
import { useAuthStore } from '@/stores/auth'

const coladaState = vi.hoisted(() => ({
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  submit: vi.fn(),
}))

// The composable consumes the Quality facade through the curated
// `@nerv-iip/api-client` barrel; mock it here. Auth-API functions are stubbed
// because `@/stores/auth` lazily references them (never called — we only $patch).
vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleQualityInspectionTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleQualityInspectionTasks' }],
    query: vi.fn(),
  })),
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
  useMutation: vi.fn(() => ({
    mutateAsync: coladaState.submit,
    isLoading: shallowRef(false),
    error: shallowRef(),
  })),
  useQueryCache: vi.fn(() => ({ invalidateQueries: vi.fn(() => Promise.resolve()) })),
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

describe('useBusinessQualityInspectionTasks', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryOptionsById.clear()
  })

  it('keeps list + reason-code queries disabled when the principal has no org/env scope', () => {
    useBusinessQualityInspectionTasks()
    expect(coladaState.queryOptionsById.get('listBusinessConsoleQualityInspectionTasks')?.enabled).toBe(false)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleQualityReasonCodes')?.enabled).toBe(false)
  })

  it('enables the queries once the principal carries an org/env scope', () => {
    seedPrincipal()
    useBusinessQualityInspectionTasks()
    expect(coladaState.queryOptionsById.get('listBusinessConsoleQualityInspectionTasks')?.enabled).toBe(true)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleQualityReasonCodes')?.enabled).toBe(true)
  })

  it('injects inspectorUserId (principalId) + org/env into the submit call — caller only supplies lines', async () => {
    seedPrincipal()
    const { submitInspection } = useBusinessQualityInspectionTasks()

    await submitInspection('TASK-1', LINES)

    expect(coladaState.submit).toHaveBeenCalledTimes(1)
    const arg = coladaState.submit.mock.calls[0][0]
    expect(arg.path).toEqual({ inspectionTaskId: 'TASK-1' })
    expect(arg.query).toEqual({ organizationId: 'org-001', environmentId: 'env-dev' })
    expect(arg.body.inspectorUserId).toBe('user-admin')
    expect(arg.body.resultLines).toEqual(LINES)
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
})
