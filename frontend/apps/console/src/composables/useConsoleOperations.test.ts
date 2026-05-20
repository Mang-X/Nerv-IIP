import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import { useAuthStore } from '@/stores/auth'
import {
  getConsoleInstanceDetailQueryOptions,
  getConsoleOperationTaskQueryOptions,
  listConsoleInstancesQueryOptions,
  restartConsoleInstanceMutationOptions,
} from '@nerv-iip/api-client'
import {
  useConsoleInstances,
  useOperationTask,
  useRestartOperation,
} from './useConsoleOperations'

vi.mock('@nerv-iip/api-client', () => ({
  getConsoleInstanceDetailQueryOptions: vi.fn(() => ({
    key: ['console-instance-detail'],
    query: vi.fn(),
  })),
  getConsoleOperationTaskQueryOptions: vi.fn(() => ({
    key: ['console-operation-task'],
    query: vi.fn(),
  })),
  listConsoleInstancesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listConsoleInstances' }],
    query: vi.fn(),
  })),
  restartConsoleInstanceMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async () => ({
      success: true,
      data: {
        operationTaskId: 'task-1',
        status: 'queued',
      },
    })),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      options.onSuccess?.(result)
      return result
    }),
  })),
  useQuery: vi.fn((optionsFactory) => {
    optionsFactory()

    return {
      data: shallowRef({
        success: true,
        data: {
          items: [
            {
              instanceKey: 'instance-1',
            },
          ],
        },
      }),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: vi.fn(async () => undefined),
  })),
}))

describe('console operation composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()

    useAuthStore().$patch({
      accessToken: 'access-token',
      principal: {
        principalId: 'user-admin',
        principalType: 'user',
        loginName: 'admin',
        organizationId: 'org-customer',
        environmentId: 'env-prod',
      },
    })
  })

  it('lists and loads instance details with the logged-in principal context', () => {
    useConsoleInstances()

    expect(listConsoleInstancesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-customer',
        environmentId: 'env-prod',
        pageIndex: 1,
        pageSize: 20,
      },
    })
    expect(getConsoleInstanceDetailQueryOptions).toHaveBeenCalledWith({
      path: {
        instanceKey: 'instance-1',
      },
      query: {
        organizationId: 'org-customer',
        environmentId: 'env-prod',
      },
    })
  })

  it('restarts instances with the logged-in principal context', async () => {
    const { restartInstance } = useRestartOperation()

    await restartInstance('instance-1')

    expect(restartConsoleInstanceMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(restartConsoleInstanceMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        organizationId: 'org-customer',
        environmentId: 'env-prod',
      }),
      path: {
        instanceKey: 'instance-1',
      },
    })
  })

  it('exposes a restart error instead of rejecting when principal context is unavailable', async () => {
    useAuthStore().$patch({
      principal: undefined,
    })
    const { restartError, restartInstance } = useRestartOperation()

    await expect(restartInstance('instance-1')).resolves.toBeUndefined()

    expect(restartError.value?.message).toBe(
      'Console organization and environment context is unavailable.',
    )
    expect(
      vi.mocked(restartConsoleInstanceMutationOptions).mock.results[0]?.value.mutation,
    ).not.toHaveBeenCalled()
  })

  it('loads operation tasks with the logged-in principal context', () => {
    useOperationTask('task-1')

    expect(getConsoleOperationTaskQueryOptions).toHaveBeenCalledWith({
      path: {
        operationTaskId: 'task-1',
      },
      query: {
        organizationId: 'org-customer',
        environmentId: 'env-prod',
      },
    })
  })

  it('does not construct console instance queries when principal context is unavailable', () => {
    useAuthStore().$patch({
      principal: undefined,
    })

    useConsoleInstances()

    expect(listConsoleInstancesQueryOptions).not.toHaveBeenCalled()
    expect(getConsoleInstanceDetailQueryOptions).not.toHaveBeenCalled()
  })

  it('does not construct operation task queries when principal context is unavailable', () => {
    useAuthStore().$patch({
      principal: undefined,
    })

    useOperationTask('task-1')

    expect(getConsoleOperationTaskQueryOptions).not.toHaveBeenCalled()
  })
})
