import { getBusinessConsoleSchedulingTimeContextQueryOptions } from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { hasBusinessContext, type BusinessContextFields } from './businessContextBinding'
import { assertEnvelopeSuccess } from './serviceEnvelope'

export function useSchedulingTimeContext(
  planId: () => string | undefined,
  context: () => BusinessContextFields,
) {
  const query = useQuery(() => {
    const id = planId() ?? ''
    const scope = context()
    const options = getBusinessConsoleSchedulingTimeContextQueryOptions({
      path: { planId: id },
      query: scope,
    })
    return {
      ...options,
      enabled: Boolean(id) && hasBusinessContext(scope),
      query: async (...args: Parameters<typeof options.query>) => {
        const envelope = await options.query(...args)
        assertEnvelopeSuccess(envelope, '未能读取工厂时区，当前草案已保留。')
        return envelope
      },
    }
  })
  return { items: computed(() => query.data.value?.data ?? undefined), error: query.error }
}
