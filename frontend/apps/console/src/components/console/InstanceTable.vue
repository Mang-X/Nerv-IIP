<script setup lang="ts">
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
import type { InstanceListItem } from '@nerv-iip/api-client'

const props = defineProps<{
  instances: InstanceListItem[]
  pending?: boolean
  restartPending?: boolean
  selectedInstanceKey?: string
}>()

const emit = defineEmits<{
  restartInstance: [instanceKey: string]
  selectInstance: [instanceKey: string]
}>()

function badgeVariant(status?: string | null) {
  const s = status?.toLowerCase()
  return s === 'failed' || s === 'unhealthy' || s === 'stopped' || s === 'cancelled' || s === 'canceled'
    ? 'destructive'
    : s === 'running' || s === 'healthy'
      ? 'success'
      : 'secondary'
}

function instanceLabel(instance: InstanceListItem) {
  return instance.instanceName ?? instance.instanceKey ?? 'Unknown instance'
}

function rowKey(instance: InstanceListItem, index: number) {
  return instance.instanceKey ?? `instance:${instance.instanceName ?? instance.applicationKey ?? 'unknown'}:${index}`
}
</script>

<template>
  <div class="overflow-hidden rounded-lg border bg-background">
    <div class="flex items-center justify-between border-b px-5 py-4">
      <div class="flex flex-col gap-0.5">
        <p class="text-xs font-bold uppercase tracking-wider text-primary">Console</p>
        <h1 class="text-xl font-semibold text-foreground">Instances</h1>
      </div>
      <span class="text-sm font-semibold text-muted-foreground">{{ instances.length }} total</span>
    </div>

    <div class="overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>App</TableHead>
            <TableHead>Instance</TableHead>
            <TableHead>Node</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Health</TableHead>
            <TableHead class="w-24">Action</TableHead>
          </TableRow>
        </TableHeader>

        <TableBody>
          <template v-if="pending">
            <TableRow v-for="i in 5" :key="i">
              <TableCell><Skeleton class="h-4 w-32" /></TableCell>
              <TableCell><Skeleton class="h-4 w-24" /></TableCell>
              <TableCell><Skeleton class="h-4 w-20" /></TableCell>
              <TableCell><Skeleton class="h-5 w-16 rounded-full" /></TableCell>
              <TableCell><Skeleton class="h-5 w-16 rounded-full" /></TableCell>
              <TableCell><Skeleton class="h-8 w-16" /></TableCell>
            </TableRow>
          </template>

          <template v-else-if="instances.length">
            <TableRow
              v-for="(instance, index) in instances"
              :key="rowKey(instance, index)"
              :class="instance.instanceKey === selectedInstanceKey ? 'bg-primary/5' : ''"
              class="cursor-pointer"
              @click="instance.instanceKey && emit('selectInstance', instance.instanceKey)"
            >
              <TableCell>
                <div class="flex flex-col gap-0.5">
                  <span class="font-semibold">
                    {{ instance.applicationName ?? instance.applicationKey ?? 'Unknown app' }}
                  </span>
                  <span class="text-xs text-muted-foreground">
                    {{ instance.version ?? 'Unversioned' }}
                  </span>
                </div>
              </TableCell>
              <TableCell>{{ instanceLabel(instance) }}</TableCell>
              <TableCell>{{ instance.nodeName ?? instance.nodeKey ?? 'Unassigned' }}</TableCell>
              <TableCell>
                <Badge :variant="badgeVariant(instance.reportedStatus)">
                  {{ instance.reportedStatus ?? 'unknown' }}
                </Badge>
              </TableCell>
              <TableCell>
                <Badge :variant="badgeVariant(instance.healthStatus)">
                  {{ instance.healthStatus ?? 'unknown' }}
                </Badge>
              </TableCell>
              <TableCell>
                <Button
                  :disabled="restartPending || !instance.instanceKey"
                  size="sm"
                  variant="outline"
                  type="button"
                  @click.stop="instance.instanceKey && emit('restartInstance', instance.instanceKey)"
                >
                  Restart
                </Button>
              </TableCell>
            </TableRow>
          </template>

          <TableEmpty v-else :colspan="6">No instances found for this environment.</TableEmpty>
        </TableBody>
      </Table>
    </div>
  </div>
</template>
