<script setup lang="ts">
import type { ConsoleIamSessionResponse } from '@nerv-iip/api-client'
import {
  Badge,
  Button,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'

const props = withDefaults(defineProps<{
  currentSessionId?: string
  pending?: boolean
  sessions: ConsoleIamSessionResponse[]
}>(), {
  pending: false,
})

const emit = defineEmits<{
  revoke: [session: ConsoleIamSessionResponse]
}>()

function sessionState(session: ConsoleIamSessionResponse) {
  return session.revokedAtUtc ? 'Revoked' : 'Active'
}

function formatDate(value?: string | null) {
  if (!value) {
    return '-'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString()
}

function sessionLabel(session: ConsoleIamSessionResponse) {
  return session.sessionId || 'session'
}

function canRevoke(session: ConsoleIamSessionResponse) {
  return Boolean(session.sessionId) && !session.revokedAtUtc
}
</script>

<template>
  <div class="overflow-hidden rounded-lg border bg-background">
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Session ID</TableHead>
          <TableHead>User ID</TableHead>
          <TableHead>Issued at</TableHead>
          <TableHead>Expires at</TableHead>
          <TableHead>State</TableHead>
          <TableHead>Permission version</TableHead>
          <TableHead class="w-28 text-right">
            Actions
          </TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <template v-if="props.pending">
          <TableRow v-for="index in 5" :key="index">
            <TableCell><Skeleton class="h-5 w-44" /></TableCell>
            <TableCell><Skeleton class="h-5 w-36" /></TableCell>
            <TableCell><Skeleton class="h-5 w-40" /></TableCell>
            <TableCell><Skeleton class="h-5 w-40" /></TableCell>
            <TableCell><Skeleton class="h-5 w-20" /></TableCell>
            <TableCell><Skeleton class="h-5 w-16" /></TableCell>
            <TableCell><Skeleton class="ml-auto h-9 w-20" /></TableCell>
          </TableRow>
        </template>

        <TableEmpty v-else-if="props.sessions.length === 0" :colspan="7">
          No sessions match the current filters.
        </TableEmpty>

        <TableRow v-for="session in props.sessions" v-else :key="session.sessionId ?? session.userId">
          <TableCell class="font-mono text-xs">
            <div class="flex flex-col gap-1">
              <span>{{ session.sessionId || '-' }}</span>
              <span
                v-if="session.sessionId && session.sessionId === props.currentSessionId"
                class="text-xs font-normal text-muted-foreground"
              >
                Current session
              </span>
            </div>
          </TableCell>
          <TableCell class="font-mono text-xs text-muted-foreground">
            {{ session.userId || '-' }}
          </TableCell>
          <TableCell>{{ formatDate(session.issuedAtUtc) }}</TableCell>
          <TableCell>{{ formatDate(session.expiresAtUtc) }}</TableCell>
          <TableCell>
            <Badge :variant="session.revokedAtUtc ? 'secondary' : 'default'">
              {{ sessionState(session) }}
            </Badge>
          </TableCell>
          <TableCell>{{ session.permissionVersion ?? '-' }}</TableCell>
          <TableCell class="text-right">
            <Button
              size="sm"
              type="button"
              variant="destructive"
              :aria-label="`Revoke session ${sessionLabel(session)}`"
              :disabled="!canRevoke(session)"
              @click="emit('revoke', session)"
            >
              Revoke
            </Button>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>
</template>
