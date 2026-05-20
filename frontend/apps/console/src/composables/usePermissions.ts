import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'

export function useHasPermission(permissionCode: string) {
  const auth = useAuthStore()
  const { principal } = storeToRefs(auth)

  return computed(() => principal.value?.permissionCodes?.includes(permissionCode) ?? false)
}
