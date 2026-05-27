import { useBusinessContextStore } from '@/stores/businessContext'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'

export function useMesWorkbenchContext() {
  const context = useBusinessContextStore()
  const { environmentId, organizationId } = storeToRefs(context)

  return {
    environmentId,
    organizationId,
    queryContext: computed(() => ({
      organizationId: organizationId.value,
      environmentId: environmentId.value,
    })),
  }
}
