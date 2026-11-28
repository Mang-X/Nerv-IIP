declare module 'vue-router' {
  interface RouteMeta {
    /** A page can open if the principal holds any listed permission. */
    requiredPermissions?: readonly string[]
  }
}

export const consolePagePermissions = {
  '/': ['apphub.instances.read'],
  '/notifications': ['notifications.messages.read', 'notifications.tasks.read'],
  '/notifications/dlq': ['notifications.dlq.read'],
  '/business': ['apphub.instances.read'],
  '/iam/users': ['iam.users.read'],
  '/iam/roles': ['iam.roles.read'],
  '/iam/sessions': ['iam.sessions.read'],
  '/operations': ['ops.tasks.read'],
} as const

export function canAccess(
  permissions: readonly string[] | null | undefined,
  required: readonly string[],
): boolean {
  return required.some((code) => permissions?.includes(code))
}

export function firstConsolePage(permissions: readonly string[] | null | undefined): string {
  return (
    Object.entries(consolePagePermissions).find(
      ([path, required]) => path !== '/operations' && canAccess(permissions, required),
    )?.[0] ?? '/forbidden'
  )
}
