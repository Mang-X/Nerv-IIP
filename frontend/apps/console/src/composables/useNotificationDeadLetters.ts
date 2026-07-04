import {
  getConsoleNotificationDeadLetterMetricsQueryOptions,
  getConsoleNotificationDeadLetterQueryOptions,
  ignoreConsoleNotificationDeadLetterMutationOptions,
  listConsoleNotificationDeadLettersQueryOptions,
  replayConsoleNotificationDeadLetterMutationOptions,
  replayConsoleNotificationDeadLettersMutationOptions,
  type NotificationDeadLetterBatchReplayEnvelope,
  type NotificationDeadLetterDetailEnvelope,
  type NotificationDeadLetterListEnvelope,
  type NotificationDeadLetterMetricsEnvelope,
  type NotificationDeadLetterResponse,
  type NotificationDeadLetterReplayEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { computed, shallowRef } from 'vue'

const CONTEXT_UNAVAILABLE_MESSAGE = 'Console organization and environment context is unavailable.'
const ignoreBackgroundError = (_error: unknown) => {}

export type DeadLetterStatusFilter = '' | 'Pending' | 'Failed' | 'Replayed' | 'Ignored'

interface ConsoleContext {
  environmentId: string
  organizationId: string
}

function isDeadLetterEntry(entry: UseQueryEntry) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

  return keyParts.some((part) => {
    return (
      typeof part === 'object' &&
      part !== null &&
      '_id' in part &&
      (part._id === 'listConsoleNotificationDeadLetters' ||
        part._id === 'getConsoleNotificationDeadLetterMetrics' ||
        part._id === 'getConsoleNotificationDeadLetter')
    )
  })
}

export function useNotificationDeadLetters() {
  const auth = useAuthStore()
  const { principal } = storeToRefs(auth)
  const queryCache = useQueryCache()
  const actionError = shallowRef<Error>()
  const selectedDeadLetterId = shallowRef<string>()
  const statusFilter = shallowRef<DeadLetterStatusFilter>('Pending')
  const eventTypeFilter = shallowRef('')
  const consumerNameFilter = shallowRef('')
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

  const filterQuery = computed(() => ({
    consumerName: optionalText(consumerNameFilter.value),
    eventType: optionalText(eventTypeFilter.value),
    status: optionalText(statusFilter.value),
    take: 100,
  }))

  const listQuery = useQuery(() => {
    const context = consoleContext.value
    if (!context) {
      return disabledDeadLetterQueryOptions<NotificationDeadLetterListEnvelope>(
        'listConsoleNotificationDeadLetters',
      )
    }

    return withConsoleContextQueryKey(
      listConsoleNotificationDeadLettersQueryOptions({
        headers: consoleContextHeaders(context),
        query: filterQuery.value,
      } as Parameters<typeof listConsoleNotificationDeadLettersQueryOptions>[0]),
      context,
      filterQuery.value,
    )
  })

  const detailQuery = useQuery(() => {
    const context = consoleContext.value
    const deadLetterId = selectedDeadLetterId.value
    if (!context || !deadLetterId) {
      return disabledDeadLetterQueryOptions<NotificationDeadLetterDetailEnvelope>(
        'getConsoleNotificationDeadLetter',
      )
    }

    return withConsoleContextQueryKey(
      getConsoleNotificationDeadLetterQueryOptions({
        headers: consoleContextHeaders(context),
        path: {
          deadLetterId,
        },
      } as Parameters<typeof getConsoleNotificationDeadLetterQueryOptions>[0]),
      context,
      { deadLetterId },
    )
  })

  const metricsQuery = useQuery(() => {
    const context = consoleContext.value
    if (!context) {
      return disabledDeadLetterQueryOptions<NotificationDeadLetterMetricsEnvelope>(
        'getConsoleNotificationDeadLetterMetrics',
      )
    }

    return withConsoleContextQueryKey(
      getConsoleNotificationDeadLetterMetricsQueryOptions({
        headers: consoleContextHeaders(context),
      } as Parameters<typeof getConsoleNotificationDeadLetterMetricsQueryOptions>[0]),
      context,
      {},
    )
  })

  const replayMutation = useMutation({
    ...replayConsoleNotificationDeadLetterMutationOptions(),
    onSuccess(result) {
      if (isSuccessfulEnvelope(result)) {
        void invalidateDeadLetters(queryCache)
      }
    },
  })

  const replayBatchMutation = useMutation({
    ...replayConsoleNotificationDeadLettersMutationOptions(),
    onSuccess(result) {
      if (isSuccessfulEnvelope(result)) {
        void invalidateDeadLetters(queryCache)
      }
    },
  })

  const ignoreMutation = useMutation({
    ...ignoreConsoleNotificationDeadLetterMutationOptions(),
    onSuccess(result) {
      if (isSuccessfulEnvelope(result)) {
        void invalidateDeadLetters(queryCache)
      }
    },
  })

  const deadLetters = computed<NotificationDeadLetterResponse[]>(
    () => unwrapResponseData(listQuery.data.value)?.items ?? [],
  )
  const selectedDeadLetter = computed(() => unwrapResponseData(detailQuery.data.value))
  const metrics = computed(() => unwrapResponseData(metricsQuery.data.value))
  const actionableCount = computed(() => metrics.value?.actionableCount ?? 0)
  const pendingCount = computed(() => metrics.value?.pendingCount ?? 0)
  const failedCount = computed(() => metrics.value?.failedCount ?? 0)
  const listEnvelopeError = computed(() =>
    responseEnvelopeError(listQuery.data.value, 'Unable to load dead letters.'),
  )
  const detailEnvelopeError = computed(() =>
    responseEnvelopeError(detailQuery.data.value, 'Unable to load dead-letter detail.'),
  )
  const metricsEnvelopeError = computed(() =>
    responseEnvelopeError(metricsQuery.data.value, 'Unable to load dead-letter metrics.'),
  )
  const allError = computed(
    () =>
      listQuery.error.value ??
      detailQuery.error.value ??
      metricsQuery.error.value ??
      listEnvelopeError.value ??
      detailEnvelopeError.value ??
      metricsEnvelopeError.value ??
      replayMutation.error.value ??
      replayBatchMutation.error.value ??
      ignoreMutation.error.value ??
      actionError.value,
  )

  async function refreshDeadLetters() {
    actionError.value = undefined
    try {
      await Promise.all([
        listQuery.refetch(),
        metricsQuery.refetch(),
        selectedDeadLetterId.value ? detailQuery.refetch() : Promise.resolve(),
      ])
    } catch (error) {
      actionError.value = toError(error, 'Unable to refresh dead letters.')
    }
  }

  async function replay(deadLetterId: string) {
    if (!deadLetterId) {
      return undefined
    }

    actionError.value = undefined
    try {
      const context = getRequiredConsoleContext(consoleContext.value)
      return requireResponseData(
        (await replayMutation.mutateAsync({
          headers: consoleContextHeaders(context),
          path: {
            deadLetterId,
          },
        } as Parameters<typeof replayMutation.mutateAsync>[0])) as NotificationDeadLetterReplayEnvelope,
        'Unable to replay dead letter.',
      )
    } catch (error) {
      actionError.value = toError(error, 'Unable to replay dead letter.')
      throw actionError.value
    }
  }

  async function replayFiltered() {
    actionError.value = undefined
    try {
      const context = getRequiredConsoleContext(consoleContext.value)
      return requireResponseData(
        (await replayBatchMutation.mutateAsync({
          body: {
            consumerName: optionalText(consumerNameFilter.value),
            eventType: optionalText(eventTypeFilter.value),
            status: optionalText(statusFilter.value) ?? 'Pending',
            take: 100,
          },
          headers: consoleContextHeaders(context),
        } as Parameters<typeof replayBatchMutation.mutateAsync>[0])) as NotificationDeadLetterBatchReplayEnvelope,
        'Unable to replay matching dead letters.',
      )
    } catch (error) {
      actionError.value = toError(error, 'Unable to replay matching dead letters.')
      throw actionError.value
    }
  }

  async function ignore(deadLetterId: string, reason: string) {
    if (!deadLetterId || !isNonEmptyString(reason)) {
      return undefined
    }

    actionError.value = undefined
    try {
      const context = getRequiredConsoleContext(consoleContext.value)
      return requireResponseData(
        (await ignoreMutation.mutateAsync({
          body: {
            reason: reason.trim(),
          },
          headers: consoleContextHeaders(context),
          path: {
            deadLetterId,
          },
        } as Parameters<typeof ignoreMutation.mutateAsync>[0])) as NotificationDeadLetterDetailEnvelope,
        'Unable to ignore dead letter.',
      )
    } catch (error) {
      actionError.value = toError(error, 'Unable to ignore dead letter.')
      throw actionError.value
    }
  }

  return {
    actionError,
    actionableCount,
    allError,
    consumerNameFilter,
    deadLetters,
    detailPending: detailQuery.isLoading,
    eventTypeFilter,
    failedCount,
    ignore,
    ignorePending: ignoreMutation.isLoading,
    listPending: listQuery.isLoading,
    metricsPending: metricsQuery.isLoading,
    pendingCount,
    refreshDeadLetters,
    replay,
    replayFiltered,
    replayPending: replayMutation.isLoading,
    replayBatchPending: replayBatchMutation.isLoading,
    selectedDeadLetter,
    selectedDeadLetterId,
    statusFilter,
  }
}

function disabledDeadLetterQueryOptions<TData>(id: string) {
  return {
    key: [{ _id: id, disabledReason: 'missing-context' }],
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
  extra: Record<string, unknown>,
): TOptions {
  const keyPart = Array.isArray(options.key) && isRecord(options.key[0]) ? options.key[0] : {}

  return {
    ...options,
    key: [
      {
        ...keyPart,
        ...extra,
        environmentId: context.environmentId,
        organizationId: context.organizationId,
      },
    ],
  }
}

function invalidateDeadLetters(queryCache: ReturnType<typeof useQueryCache>) {
  return queryCache
    .invalidateQueries({ predicate: isDeadLetterEntry })
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

function optionalText(value: string | undefined) {
  const trimmed = value?.trim()
  return trimmed ? trimmed : undefined
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
