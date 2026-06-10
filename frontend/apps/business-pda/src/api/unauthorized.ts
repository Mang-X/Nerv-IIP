import type { Router } from 'vue-router'

interface AuthSessionController {
  clearSession(reason: string): void
}

export function handleUnauthorized(auth: AuthSessionController, router: Router) {
  auth.clearSession('api-unauthorized')

  const currentRoute = router.currentRoute.value
  if (currentRoute.path === '/login') {
    return
  }

  void router.push({
    path: '/login',
    query: {
      redirect: currentRoute.fullPath,
    },
  })
}
