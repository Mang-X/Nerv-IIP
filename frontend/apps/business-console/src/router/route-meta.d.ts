import type { BusinessPermissionCode } from '@/permissions'
import 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    requiredPermissions?: BusinessPermissionCode[]
  }
}
