import { getBusinessConsoleSchedulingDowntimeImpactQueryOptions } from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { useNow } from '@vueuse/core'
import { hasBusinessContext, type BusinessContextFields } from './businessContextBinding'
import { assertEnvelopeSuccess } from './serviceEnvelope'

export function useSchedulingDowntime(
  planId: MaybeRefOrGetter<string | undefined>,
  context: () => BusinessContextFields,
) {
  const now = useNow({ interval: 1000 })
  const query = useQuery(() => {
    const id = toValue(planId) ?? ''
    const scope = context()
    const options = getBusinessConsoleSchedulingDowntimeImpactQueryOptions({
      path: { planId: id },
      query: scope,
    })
    return {
      ...options,
      enabled: Boolean(id) && hasBusinessContext(scope),
      autoRefetch: () => 5000,
      query: async (...args: Parameters<typeof options.query>) => {
        const envelope = await options.query(...args)
        assertEnvelopeSuccess(envelope, '未能读取设备停机影响。')
        return envelope
      },
    }
  })
  return {
    now,
    impact: computed(() => {
      const impact = query.data.value?.data
      return impact?.baselinePlanId === toValue(planId) ? (impact ?? undefined) : undefined
    }),
    error: query.error,
  }
}
