<script setup lang="ts">
import type { ConsoleIamRoleResponse } from '@nerv-iip/api-client'
import PermissionCodeBadge from '@/components/iam/PermissionCodeBadge.vue'
import {
  Badge,
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { MoreHorizontalIcon } from 'lucide-vue-next'

const props = withDefaults(
  defineProps<{
    canManage?: boolean
    pending?: boolean
    roles: ConsoleIamRoleResponse[]
  }>(),
  {
    canManage: false,
    pending: false,
  },
)

const emit = defineEmits<{
  editPermissions: [role: ConsoleIamRoleResponse]
}>()

function roleLabel(role: ConsoleIamRoleResponse) {
  return role.roleName || role.roleId || 'role'
}

function keyPermissions(role: ConsoleIamRoleResponse) {
  return [...(role.permissionCodes ?? [])].sort().slice(0, 3)
}
</script>

<template>
  <div class="overflow-hidden rounded-lg border bg-background">
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Role name</TableHead>
          <TableHead>Role ID</TableHead>
          <TableHead>Permission count</TableHead>
          <TableHead>Key permissions</TableHead>
          <TableHead class="w-16 text-right"> Actions </TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <template v-if="props.pending">
          <TableRow v-for="index in 5" :key="index">
            <TableCell><Skeleton class="h-5 w-40" /></TableCell>
            <TableCell><Skeleton class="h-5 w-44" /></TableCell>
            <TableCell><Skeleton class="h-5 w-20" /></TableCell>
            <TableCell><Skeleton class="h-5 w-64" /></TableCell>
            <TableCell><Skeleton class="ml-auto h-8 w-8" /></TableCell>
          </TableRow>
        </template>

        <TableEmpty v-else-if="props.roles.length === 0" :colspan="5">
          No roles match the current filters.
        </TableEmpty>

        <TableRow v-for="role in props.roles" v-else :key="role.roleId ?? role.roleName">
          <TableCell class="font-medium">
            {{ role.roleName || '-' }}
          </TableCell>
          <TableCell class="font-mono text-xs text-muted-foreground">
            {{ role.roleId || '-' }}
          </TableCell>
          <TableCell>
            <Badge variant="secondary">
              {{ role.permissionCodes?.length ?? 0 }}
            </Badge>
          </TableCell>
          <TableCell>
            <div class="flex flex-wrap gap-1.5">
              <PermissionCodeBadge v-for="code in keyPermissions(role)" :key="code" :code="code" />
              <span
                v-if="(role.permissionCodes?.length ?? 0) > keyPermissions(role).length"
                class="text-xs text-muted-foreground"
              >
                +{{ (role.permissionCodes?.length ?? 0) - keyPermissions(role).length }} more
              </span>
              <span
                v-if="(role.permissionCodes?.length ?? 0) === 0"
                class="text-sm text-muted-foreground"
              >
                No permissions
              </span>
            </div>
          </TableCell>
          <TableCell class="text-right">
            <DropdownMenu>
              <DropdownMenuTrigger as-child>
                <Button
                  size="icon"
                  type="button"
                  variant="ghost"
                  :aria-label="`Open actions for ${roleLabel(role)}`"
                  :disabled="!props.canManage"
                >
                  <MoreHorizontalIcon class="size-4" aria-hidden="true" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem @select="emit('editPermissions', role)">
                  Edit permissions
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>
</template>
