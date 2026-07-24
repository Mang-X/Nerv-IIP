import type { BusinessConsoleOrderUrgencyDetail } from '@nerv-iip/api-client'
import {
  getBusinessConsoleOrderUrgencyQueryOptions,
  setBusinessConsoleOrderUrgencyBusinessPriorityMutationOptions,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { bindBusinessContext, hasBusinessContext } from './businessContextBinding'

interface OrderUrgencyDetailEnvelope {
  success?: boolean
  data?: BusinessConsoleOrderUrgencyDetail | null
}

// MAN-590 / #1061: authoritative detail (current + append-only priority audit
// history) for the urgency explanation Sheet. Consumes the #1053 facade only —
// no second urgency calculation lives here.
export function useOrderUrgencyDetail(
  orderReference: MaybeRefOrGetter<string | null | undefined>,
  options: { enabled: MaybeRefOrGetter<boolean> },
) {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(
    reactive({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
    }),
  )
  const reference = computed(() => toValue(orderReference)?.trim() ?? '')
  const enabled = computed(
    () =>
      Boolean(toValue(options.enabled)) &&
      hasBusinessContext(filters) &&
      reference.value.length > 0,
  )

  const query = useQuery(() => ({
    ...getBusinessConsoleOrderUrgencyQueryOptions({
      path: { orderReference: reference.value },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
    enabled: enabled.value,
  }))

  const detail = computed<BusinessConsoleOrderUrgencyDetail | undefined>(() => {
    const envelope = query.data.value as OrderUrgencyDetailEnvelope | undefined
    return envelope?.success ? (envelope.data ?? undefined) : undefined
  })

  return {
    detail,
    error: query.error,
    pending: query.isLoading,
    refresh: () => (enabled.value ? query.refetch() : Promise.resolve(undefined)),
  }
}

export interface SetOrderUrgencyBusinessPriorityInput {
  orderReference: string
  level: string
  reason: string
  expiresAtUtc?: string | null
}

// Governed business-priority write. The authenticated Gateway actor is stamped
// server-side; the client only supplies level/reason/expiry.
export function useSetOrderUrgencyBusinessPriority() {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(
    reactive({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
    }),
  )
  const mutation = useMutation(setBusinessConsoleOrderUrgencyBusinessPriorityMutationOptions())

  return {
    error: mutation.error,
    pending: mutation.isLoading,
    setBusinessPriority: (input: SetOrderUrgencyBusinessPriorityInput) =>
      mutation.mutateAsync({
        path: { orderReference: input.orderReference.trim() },
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          level: input.level,
          reason: input.reason,
          expiresAtUtc: input.expiresAtUtc ?? null,
        },
      }),
  }
}
