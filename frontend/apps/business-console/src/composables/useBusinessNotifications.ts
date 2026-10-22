import {
  listBusinessConsoleNotificationMessagesQueryOptions,
  markBusinessConsoleNotificationMessageReadMutationOptions,
  type BusinessConsoleNotificationMessageItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useAuthStore } from '@/stores/auth'
import { hasBusinessContext } from './businessContextBinding'

export function isReadMessage(message: BusinessConsoleNotificationMessageItem) {
  return Boolean(message.readAtUtc) || message.status?.toLowerCase() === 'read'
}

export function useBusinessNotifications() {
  const context = useBusinessContextStore()
  const auth = useAuthStore()
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
  const messagesError = computed(
    () => query.error.value ?? (query.data.value?.success === false ? query.data.value : undefined),
  )

  async function markRead(messageId: string) {
    const response = await mutation.mutateAsync({ path: { messageId }, body: contextFields() })
    if (!response.success) throw response
    await query.refetch()
  }

  return {
    messages,
    unreadMessages,
    messagesError,
    markRead,
    markReadPending: mutation.isLoading,
    messagesPending: query.isLoading,
    refreshNotifications: query.refetch,
  }
}
