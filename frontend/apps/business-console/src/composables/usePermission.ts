import { useAuthStore } from '@/stores/auth'
import { usePermission as usePermissionCodes } from '@nerv-iip/auth'

export function usePermission() {
  const auth = useAuthStore()
  return usePermissionCodes(() => auth.principal?.permissionCodes)
}
