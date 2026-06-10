import { useAuthStore } from '@/stores/auth'
import { createAuthGuard } from '@nerv-iip/auth'

export const installAuthGuard = createAuthGuard({
  loginPath: '/login',
  useAuthStore,
})
