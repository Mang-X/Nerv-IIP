import { handleUnauthorizedRedirect, type AuthSessionController } from '@nerv-iip/auth'
import type { Router } from 'vue-router'

export function handleUnauthorized(auth: AuthSessionController, router: Router) {
  handleUnauthorizedRedirect(auth, router, { loginPath: '/login' })
}
