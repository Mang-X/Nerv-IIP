import {
  listBusinessConsoleNotificationMessagesQueryOptions,
  markBusinessConsoleNotificationMessageReadMutationOptions,
  type BusinessConsoleNotificationMessageItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, shallowRef } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useAuthStore } from '@/stores/auth'
import { hasBusinessContext } from './businessContextBinding'

export function isReadMessage(message: BusinessConsoleNotificationMessageItem) {
  return Boolean(message.readAtUtc) || message.status?.toLowerCase() === 'read'
}

export function useBusinessNotifications() {
  const context = useBusinessContextStore()
  const auth = useAuthStore()
  const actionError = shallowRef<Error>()
  const contextFields = () => ({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
  })
  const query = useQuery(() => {
    const options = listBusinessConsoleNotificationMessagesQueryOptions({ query: contextFields() })
    return {
      ...options,
      key: [...options.key, auth.principal?.principalId ?? null],
      enabled: hasBusinessContext(context) && Boolean(auth.principal),
    }
  })
  const mutation = useMutation(markBusinessConsoleNotificationMessageReadMutationOptions())
  const messages = computed(() =>
    query.data.value?.success ? (query.data.value.data?.items ?? []) : [],
  )
  const unreadMessages = computed(() => messages.value.filter((message) => !isReadMessage(message)))
  const allError = computed(
    () =>
      actionError.value ??
      query.error.value ??
      (query.data.value?.success === false
        ? new Error(query.data.value.message || '无法加载通知')
        : undefined),
  )

  async function markRead(messageId: string) {
    actionError.value = undefined
    try {
      const response = await mutation.mutateAsync({ path: { messageId }, body: contextFields() })
      if (!response.success) throw new Error(response.message || '无法标记已读')
      await query.refetch()
    } catch (error) {
      actionError.value = error as Error
      throw error
    }
  }

  async function refreshNotifications() {
    actionError.value = undefined
    try {
      await query.refetch()
    } catch (error) {
      actionError.value = error as Error
    }
  }

  return {
    messages,
    unreadMessages,
    allError,
    markRead,
    markReadPending: mutation.isLoading,
    messagesPending: query.isLoading,
    refreshNotifications,
  }
}
