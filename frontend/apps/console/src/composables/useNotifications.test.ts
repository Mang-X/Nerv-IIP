import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import { useAuthStore } from '@/stores/auth'
import {
  listConsoleNotificationMessagesQueryOptions,
  listConsoleNotificationTasksQueryOptions,
  markConsoleNotificationMessageReadMutationOptions,
  markConsoleNotificationMessagesReadMutationOptions,
  submitConsoleNotificationIntentMutationOptions,
} from '@nerv-iip/api-client'
import { useNotifications } from './useNotifications'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  refetchError: undefined as Error | undefined,
  singleMutationResult: undefined as unknown,
  batchMutationResult: undefined as unknown,
  intentMutationResult: undefined as unknown,
  queryOptionsById: new Map<string, { key?: unknown }>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listConsoleNotificationMessagesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listConsoleNotificationMessages' }],
    query: vi.fn(),
  })),
  listConsoleNotificationTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listConsoleNotificationTasks' }],
    query: vi.fn(),
  })),
  markConsoleNotificationMessageReadMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async () => coladaState.singleMutationResult),
  })),
  markConsoleNotificationMessagesReadMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async () => coladaState.batchMutationResult),
  })),
  submitConsoleNotificationIntentMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async () => coladaState.intentMutationResult),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      await options.onSuccess?.(result)
      return result
    }),
  })),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const id = Array.isArray(options.key) ? options.key[0]?._id : undefined
    if (typeof id === 'string') {
      coladaState.queryOptionsById.set(id, { key: options.key })
    }

    return {
      data: shallowRef({
        success: true,
        data: {
          items:
            id === 'listConsoleNotificationTasks'
              ? [
                  {
                    taskId: 'task-1',
                    messageId: 'msg-1',
                    taskType: 'acknowledge',
                    status: 'open',
                    createdAtUtc: '2026-05-21T00:10:00Z',
                  },
                ]
              : [
                  {
                    messageId: 'msg-1',
                    status: 'unread',
                    severity: 'warning',
                    title: 'Disk pressure',
                    summary: 'Node A is above threshold',
                    createdAtUtc: '2026-05-21T00:00:00Z',
                  },
                  {
                    messageId: 'msg-2',
                    status: 'read',
                    severity: 'info',
                    title: 'Deployment complete',
                    createdAtUtc: '2026-05-20T23:00:00Z',
                    readAtUtc: '2026-05-21T00:05:00Z',
                  },
                ],
        },
      }),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(async () => {
        if (coladaState.refetchError) {
          throw coladaState.refetchError
        }
      }),
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: coladaState.invalidateQueries,
  })),
}))

describe('useNotifications', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.refetchError = undefined
    coladaState.singleMutationResult = {
      success: true,
      data: {
        messageId: 'msg-1',
        status: 'read',
        readAtUtc: '2026-05-21T00:00:00Z',
      },
    }
    coladaState.batchMutationResult = {
      success: true,
      data: [
        {
          messageId: 'msg-1',
          status: 'read',
          readAtUtc: '2026-05-21T00:00:00Z',
        },
      ],
    }
    coladaState.intentMutationResult = {
      success: true,
      data: {
        intentId: 'intent-1',
        duplicate: false,
        messages: [],
      },
    }
    coladaState.queryOptionsById.clear()

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

  it('loads notification messages and tasks from gateway query options', () => {
    const notifications = useNotifications()

    expect(notifications.unreadMessages.value).toHaveLength(1)
    expect(notifications.readMessages.value).toHaveLength(1)
    expect(notifications.openTasks.value).toHaveLength(1)
    expect(listConsoleNotificationMessagesQueryOptions).toHaveBeenCalledWith({
      headers: {
        'X-Organization-Id': 'org-customer',
        'X-Environment-Id': 'env-prod',
      },
    })
    expect(listConsoleNotificationTasksQueryOptions).toHaveBeenCalledWith({
      headers: {
        'X-Organization-Id': 'org-customer',
        'X-Environment-Id': 'env-prod',
      },
    })
    expect(coladaState.queryOptionsById.get('listConsoleNotificationMessages')?.key).toEqual([
      {
        _id: 'listConsoleNotificationMessages',
        environmentId: 'env-prod',
        organizationId: 'org-customer',
      },
    ])
    expect(coladaState.queryOptionsById.get('listConsoleNotificationTasks')?.key).toEqual([
      {
        _id: 'listConsoleNotificationTasks',
        environmentId: 'env-prod',
        organizationId: 'org-customer',
      },
    ])
  })

  it('marks a single message read and refreshes notification queries', async () => {
    const { markRead } = useNotifications()

    await markRead('msg-1')

    expect(markConsoleNotificationMessageReadMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(markConsoleNotificationMessageReadMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      headers: {
        'X-Organization-Id': 'org-customer',
        'X-Environment-Id': 'env-prod',
      },
      path: {
        messageId: 'msg-1',
      },
    })
    expect(coladaState.invalidateQueries).toHaveBeenCalled()
  })

  it('surfaces business failures from mark read envelopes', async () => {
    coladaState.singleMutationResult = {
      success: false,
      message: 'Mark read denied',
    }
    const notifications = useNotifications()

    await expect(notifications.markRead('msg-1')).rejects.toThrow('Mark read denied')

    expect(notifications.allError.value?.message).toBe('Mark read denied')
  })

  it('marks unread messages read in a batch', async () => {
    const { markAllUnreadRead } = useNotifications()

    await markAllUnreadRead()

    expect(
      vi.mocked(markConsoleNotificationMessagesReadMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      body: {
        messageIds: ['msg-1'],
      },
      headers: {
        'X-Organization-Id': 'org-customer',
        'X-Environment-Id': 'env-prod',
      },
    })
  })

  it('submits notification intents with the logged-in principal context headers', async () => {
    const { submitIntent } = useNotifications()

    await submitIntent({
      sourceService: 'ops',
      sourceEventType: 'operation.failed',
      sourceEventId: 'evt-1',
      intentType: 'ops.operation.failed',
      severity: 'warning',
      dedupeKey: 'ops:evt-1',
      title: 'Operation failed',
      suggestedRecipientRefs: ['user-admin'],
    })

    expect(
      vi.mocked(submitConsoleNotificationIntentMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      body: {
        sourceService: 'ops',
        sourceEventType: 'operation.failed',
        sourceEventId: 'evt-1',
        intentType: 'ops.operation.failed',
        severity: 'warning',
        dedupeKey: 'ops:evt-1',
        title: 'Operation failed',
        suggestedRecipientRefs: ['user-admin'],
      },
      headers: {
        'X-Organization-Id': 'org-customer',
        'X-Environment-Id': 'env-prod',
      },
    })
  })

  it('keeps refresh failures visible without throwing from all-settled refresh', async () => {
    coladaState.refetchError = new Error('Refresh failed')
    const notifications = useNotifications()

    await notifications.refreshNotifications()

    expect(notifications.allError.value?.message).toBe('Refresh failed')
  })
})
