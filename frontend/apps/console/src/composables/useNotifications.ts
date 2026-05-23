import {
  listConsoleNotificationMessagesQueryOptions,
  listConsoleNotificationTasksQueryOptions,
  markConsoleNotificationMessageReadMutationOptions,
  markConsoleNotificationMessagesReadMutationOptions,
  type MarkNotificationMessageReadEnvelope,
  type MarkNotificationMessagesReadEnvelope,
  type NotificationIntentEnvelope,
  type NotificationMessageListEnvelope,
  type NotificationMessageResponse,
  type NotificationTaskListEnvelope,
  type NotificationTaskResponse,
  type SubmitNotificationIntentRequest,
  submitConsoleNotificationIntentMutationOptions,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { computed, shallowRef } from 'vue'

const CONTEXT_UNAVAILABLE_MESSAGE = 'Console organization and environment context is unavailable.'
const ignoreBackgroundError = (_error: unknown) => {}

interface ConsoleContext {
  environmentId: string
  organizationId: string
}

function isNotificationEntry(entry: UseQueryEntry) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

  return keyParts.some((part) => {
    return (
      typeof part === 'object' &&
      part !== null &&
      '_id' in part &&
      (part._id === 'listConsoleNotificationMessages' ||
        part._id === 'listConsoleNotificationTasks')
    )
  })
}

export function useNotifications() {
  const auth = useAuthStore()
  const { principal } = storeToRefs(auth)
  const queryCache = useQueryCache()
  const actionError = shallowRef<Error>()
  const consoleContext = computed<ConsoleContext | undefined>(() => {
    const organizationId = principal.value?.organizationId
    const environmentId = principal.value?.environmentId

    if (!isNonEmptyString(organizationId) || !isNonEmptyString(environmentId)) {
      return undefined
    }

    return {
      environmentId,
      organizationId,
    }
  })

  const messagesQuery = useQuery(() => {
    const context = consoleContext.value

    if (!context) {
      return disabledNotificationQueryOptions<NotificationMessageListEnvelope>(
        'listConsoleNotificationMessages',
      )
    }

    return withConsoleContextQueryKey(
      listConsoleNotificationMessagesQueryOptions({
        headers: consoleContextHeaders(context),
      } as Parameters<typeof listConsoleNotificationMessagesQueryOptions>[0]),
      context,
    )
  })

  const tasksQuery = useQuery(() => {
    const context = consoleContext.value

    if (!context) {
      return disabledNotificationQueryOptions<NotificationTaskListEnvelope>(
        'listConsoleNotificationTasks',
      )
    }

    return withConsoleContextQueryKey(
      listConsoleNotificationTasksQueryOptions({
        headers: consoleContextHeaders(context),
      } as Parameters<typeof listConsoleNotificationTasksQueryOptions>[0]),
      context,
    )
  })

  const intentMutation = useMutation({
    ...submitConsoleNotificationIntentMutationOptions(),
    onSuccess(result) {
      if (isSuccessfulEnvelope(result)) {
        void invalidateNotifications(queryCache)
      }
    },
  })

  const markReadMutation = useMutation({
    ...markConsoleNotificationMessageReadMutationOptions(),
    onSuccess(result) {
      if (isSuccessfulEnvelope(result)) {
        void invalidateNotifications(queryCache)
      }
    },
  })

  const batchReadMutation = useMutation({
    ...markConsoleNotificationMessagesReadMutationOptions(),
    onSuccess(result) {
      if (isSuccessfulEnvelope(result)) {
        void invalidateNotifications(queryCache)
      }
    },
  })

  const messages = computed<NotificationMessageResponse[]>(
    () => unwrapResponseData(messagesQuery.data.value)?.items ?? [],
  )
  const tasks = computed<NotificationTaskResponse[]>(
    () => unwrapResponseData(tasksQuery.data.value)?.items ?? [],
  )
  const unreadMessages = computed(() => messages.value.filter((message) => !isReadMessage(message)))
  const readMessages = computed(() => messages.value.filter(isReadMessage))
  const openTasks = computed(() =>
    tasks.value.filter((task) => (task.status ?? '').toLowerCase() === 'open'),
  )
  const messagesEnvelopeError = computed(() =>
    responseEnvelopeError(messagesQuery.data.value, 'Unable to load notification messages.'),
  )
  const tasksEnvelopeError = computed(() =>
    responseEnvelopeError(tasksQuery.data.value, 'Unable to load notification tasks.'),
  )
  const allError = computed(
    () =>
      messagesQuery.error.value ??
      tasksQuery.error.value ??
      messagesEnvelopeError.value ??
      tasksEnvelopeError.value ??
      markReadMutation.error.value ??
      batchReadMutation.error.value ??
      intentMutation.error.value ??
      actionError.value,
  )

  async function refreshNotifications() {
    actionError.value = undefined
    const results = await Promise.allSettled([messagesQuery.refetch(), tasksQuery.refetch()])
    const failed = results.find((result) => result.status === 'rejected')
    if (failed?.status === 'rejected') {
      actionError.value = toError(failed.reason, 'Unable to refresh notifications.')
    }
  }

  async function markRead(messageId: string) {
    if (!messageId) {
      return undefined
    }

    actionError.value = undefined
    try {
      const context = getRequiredConsoleContext(consoleContext.value)

      return requireResponseData(
        (await markReadMutation.mutateAsync({
          headers: consoleContextHeaders(context),
          path: {
            messageId,
          },
        } as Parameters<
          typeof markReadMutation.mutateAsync
        >[0])) as MarkNotificationMessageReadEnvelope,
        'Unable to mark notification read.',
      )
    } catch (error) {
      actionError.value = toError(error, 'Unable to mark notification read.')
      throw actionError.value
    }
  }

  async function markAllUnreadRead() {
    const messageIds = unreadMessages.value
      .map((message) => message.messageId)
      .filter(isNonEmptyString)

    if (messageIds.length === 0) {
      return []
    }

    actionError.value = undefined
    try {
      const context = getRequiredConsoleContext(consoleContext.value)

      return requireResponseData(
        (await batchReadMutation.mutateAsync({
          body: {
            messageIds,
          },
          headers: consoleContextHeaders(context),
        } as Parameters<
          typeof batchReadMutation.mutateAsync
        >[0])) as MarkNotificationMessagesReadEnvelope,
        'Unable to mark notifications read.',
      )
    } catch (error) {
      actionError.value = toError(error, 'Unable to mark notifications read.')
      throw actionError.value
    }
  }

  async function submitIntent(request: SubmitNotificationIntentRequest) {
    actionError.value = undefined
    try {
      const context = getRequiredConsoleContext(consoleContext.value)

      return requireResponseData(
        (await intentMutation.mutateAsync({
          body: request,
          headers: consoleContextHeaders(context),
        } as Parameters<typeof intentMutation.mutateAsync>[0])) as NotificationIntentEnvelope,
        'Unable to submit notification intent.',
      )
    } catch (error) {
      actionError.value = toError(error, 'Unable to submit notification intent.')
      throw actionError.value
    }
  }

  return {
    allError,
    actionError,
    batchError: batchReadMutation.error,
    batchPending: batchReadMutation.isLoading,
    markAllUnreadRead,
    markRead,
    markReadError: markReadMutation.error,
    markReadPending: markReadMutation.isLoading,
    messages,
    messagesError: messagesQuery.error,
    messagesPending: messagesQuery.isLoading,
    openTasks,
    readMessages,
    refreshNotifications,
    submitError: intentMutation.error,
    submitIntent,
    submitPending: intentMutation.isLoading,
    tasks,
    tasksError: tasksQuery.error,
    tasksPending: tasksQuery.isLoading,
    unreadMessages,
  }
}

function isReadMessage(message: NotificationMessageResponse) {
  return Boolean(message.readAtUtc) || (message.status ?? '').toLowerCase() === 'read'
}

function disabledNotificationQueryOptions<TData>(id: string) {
  return {
    key: [{ _id: id, disabledReason: 'missing-org-env-context' }],
    query: async (): Promise<TData> => {
      throw new Error(CONTEXT_UNAVAILABLE_MESSAGE)
    },
    enabled: false,
  }
}

function getRequiredConsoleContext(context: ConsoleContext | undefined) {
  if (!context) {
    throw new Error(CONTEXT_UNAVAILABLE_MESSAGE)
  }

  return context
}

function consoleContextHeaders(context: ConsoleContext) {
  return {
    'X-Organization-Id': context.organizationId,
    'X-Environment-Id': context.environmentId,
  }
}

function withConsoleContextQueryKey<TOptions extends { key: unknown }>(
  options: TOptions,
  context: ConsoleContext,
): TOptions {
  const keyPart = Array.isArray(options.key) && isRecord(options.key[0]) ? options.key[0] : {}

  return {
    ...options,
    key: [
      {
        ...keyPart,
        environmentId: context.environmentId,
        organizationId: context.organizationId,
      },
    ],
  }
}

function invalidateNotifications(queryCache: ReturnType<typeof useQueryCache>) {
  return queryCache
    .invalidateQueries({ predicate: isNotificationEntry })
    .catch(ignoreBackgroundError)
}

function unwrapResponseData<T>(envelope: ResponseEnvelope<T> | undefined): T | undefined {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

function requireResponseData<T>(envelope: ResponseEnvelope<T> | undefined, fallback: string): T {
  if (envelope?.success && envelope.data !== undefined && envelope.data !== null) {
    return envelope.data
  }

  throw new Error(responseEnvelopeMessage(envelope, fallback))
}

function responseEnvelopeError<T>(
  envelope: ResponseEnvelope<T> | undefined,
  fallback: string,
): Error | undefined {
  if (!envelope || envelope.success !== false) {
    return undefined
  }

  return new Error(responseEnvelopeMessage(envelope, fallback))
}

function responseEnvelopeMessage<T>(envelope: ResponseEnvelope<T> | undefined, fallback: string) {
  return isNonEmptyString(envelope?.message) ? envelope.message : fallback
}

function toError(error: unknown, fallback: string) {
  if (error instanceof Error) {
    return error
  }

  return new Error(isNonEmptyString(error) ? error : fallback)
}

function isSuccessfulEnvelope(value: unknown) {
  return !isRecord(value) || value.success !== false
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

interface ResponseEnvelope<T> {
  data?: T | null
  message?: string | null
  success?: boolean
}
