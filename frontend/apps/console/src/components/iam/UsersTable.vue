<script setup lang="ts">
import type { ConsoleIamUserResponse } from '@nerv-iip/api-client'
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
    pending?: boolean
    users: ConsoleIamUserResponse[]
  }>(),
  {
    pending: false,
  },
)

const emit = defineEmits<{
  disable: [user: ConsoleIamUserResponse]
  edit: [user: ConsoleIamUserResponse]
  resetPassword: [user: ConsoleIamUserResponse]
}>()
</script>

<template>
  <div class="overflow-hidden rounded-lg border bg-background">
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Login name</TableHead>
          <TableHead>Email</TableHead>
          <TableHead>User ID</TableHead>
          <TableHead>Status</TableHead>
          <TableHead class="w-16 text-right"> Actions </TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <template v-if="props.pending">
          <TableRow v-for="index in 5" :key="index">
            <TableCell><Skeleton class="h-5 w-32" /></TableCell>
            <TableCell><Skeleton class="h-5 w-48" /></TableCell>
            <TableCell><Skeleton class="h-5 w-40" /></TableCell>
            <TableCell><Skeleton class="h-5 w-20" /></TableCell>
            <TableCell><Skeleton class="ml-auto h-8 w-8" /></TableCell>
          </TableRow>
        </template>

        <TableEmpty v-else-if="props.users.length === 0" :colspan="5">
          No users match the current filters.
        </TableEmpty>

        <TableRow v-for="user in props.users" v-else :key="user.userId ?? user.loginName">
          <TableCell class="font-medium">
            {{ user.loginName || '-' }}
          </TableCell>
          <TableCell>{{ user.email || '-' }}</TableCell>
          <TableCell class="font-mono text-xs text-muted-foreground">
            {{ user.userId || '-' }}
          </TableCell>
          <TableCell>
            <Badge
              :variant="user.enabled === false ? 'secondary' : 'outline'"
              :class="
                user.enabled === false
                  ? undefined
                  : 'border-emerald-200 bg-emerald-50 text-emerald-700'
              "
            >
              {{ user.enabled === false ? 'Disabled' : 'Enabled' }}
            </Badge>
          </TableCell>
          <TableCell class="text-right">
            <DropdownMenu>
              <DropdownMenuTrigger as-child>
                <Button
                  size="icon"
                  type="button"
                  variant="ghost"
                  :aria-label="`Open actions for ${user.loginName || user.userId || 'user'}`"
                >
                  <MoreHorizontalIcon class="size-4" aria-hidden="true" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem @select="emit('edit', user)"> Edit </DropdownMenuItem>
                <DropdownMenuItem @select="emit('resetPassword', user)">
                  Reset password
                </DropdownMenuItem>
                <DropdownMenuItem
                  :disabled="user.enabled === false"
                  variant="destructive"
                  @select="emit('disable', user)"
                >
                  Disable
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>
</template>
