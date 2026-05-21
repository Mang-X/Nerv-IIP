<script setup lang="ts">
import {
  Badge,
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  Separator,
  Skeleton,
} from '@nerv-iip/ui'
import type { InstanceDetailResponse } from '@nerv-iip/api-client'
import { computed } from 'vue'

const props = defineProps<{
  instance?: InstanceDetailResponse
  pending?: boolean
}>()

type Capability = NonNullable<InstanceDetailResponse['capabilities']>[number]

const metadataEntries = computed(() => Object.entries(props.instance?.metadata ?? {}))

function badgeVariant(status?: string | null) {
  const s = status?.toLowerCase()
  return s === 'failed' || s === 'unhealthy' || s === 'stopped' || s === 'cancelled' || s === 'canceled'
    ? 'destructive'
    : s === 'running' || s === 'healthy'
      ? 'success'
      : 'secondary'
}

function capabilityKey(capability: Capability, index: number) {
  const code = capability.capabilityCode ?? 'unknown'
  const version = capability.capabilityVersion ?? 'unversioned'
  return capability.capabilityCode && capability.capabilityVersion
    ? `capability:${code}:${version}`
    : `capability:${code}:${version}:${index}`
}
</script>

<template>
  <Card class="min-w-0" aria-labelledby="detail-panel-title">
    <CardHeader class="pb-3">
      <p class="text-xs font-bold uppercase tracking-wider text-primary">Selected</p>
      <CardTitle id="detail-panel-title" class="text-lg">
        {{ instance?.instanceName ?? instance?.instanceKey ?? 'Instance detail' }}
      </CardTitle>
    </CardHeader>

    <Separator />

    <CardContent class="pt-4">
      <template v-if="pending">
        <div class="flex flex-col gap-3">
          <Skeleton v-for="i in 6" :key="i" class="h-4 w-full" />
        </div>
      </template>

      <template v-else-if="instance">
        <dl class="grid gap-3">
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">Application</dt>
            <dd class="break-anywhere text-sm">{{ instance.applicationName ?? instance.applicationKey ?? 'Unknown' }}</dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">Node</dt>
            <dd class="break-anywhere text-sm">{{ instance.nodeName ?? instance.nodeKey ?? 'Unassigned' }}</dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">Status</dt>
            <dd>
              <Badge :variant="badgeVariant(instance.reportedStatus)">
                {{ instance.reportedStatus ?? 'unknown' }}
              </Badge>
            </dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">Health</dt>
            <dd>
              <Badge :variant="badgeVariant(instance.healthStatus)">
                {{ instance.healthStatus ?? 'unknown' }}
              </Badge>
            </dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">Last heartbeat</dt>
            <dd class="text-sm">{{ instance.lastHeartbeatAtUtc ?? 'Not reported' }}</dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">Last state</dt>
            <dd class="text-sm">{{ instance.lastStateObservedAtUtc ?? 'Not reported' }}</dd>
          </div>
        </dl>

        <Separator class="my-4" />

        <section aria-labelledby="capabilities-title">
          <h3 id="capabilities-title" class="mb-3 text-sm font-semibold">Capabilities</h3>
          <ul v-if="instance.capabilities?.length" class="flex flex-col gap-2 p-0 list-none m-0">
            <li
              v-for="(capability, index) in instance.capabilities"
              :key="capabilityKey(capability, index)"
              class="flex flex-col gap-0.5 rounded-md border bg-muted/40 p-3"
            >
              <span class="break-anywhere text-sm font-semibold">
                {{ capability.capabilityCode ?? 'unknown' }}
              </span>
              <span class="text-xs text-muted-foreground">{{ capability.category ?? 'uncategorized' }}</span>
              <span class="text-xs text-muted-foreground">
                {{ capability.supportedOperations?.join(', ') ?? 'No operations' }}
              </span>
            </li>
          </ul>
          <p v-else class="text-sm text-muted-foreground">No capabilities reported.</p>
        </section>

        <Separator class="my-4" />

        <section aria-labelledby="metadata-title">
          <h3 id="metadata-title" class="mb-3 text-sm font-semibold">Metadata</h3>
          <dl v-if="metadataEntries.length" class="flex flex-col gap-2 m-0">
            <div
              v-for="[key, value] in metadataEntries"
              :key="key"
              class="grid gap-0.5"
            >
              <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">{{ key }}</dt>
              <dd class="break-anywhere text-sm m-0">{{ value }}</dd>
            </div>
          </dl>
          <p v-else class="text-sm text-muted-foreground">No metadata reported.</p>
        </section>
      </template>

      <p v-else class="text-sm text-muted-foreground">
        Select an instance to inspect its runtime facts.
      </p>
    </CardContent>
  </Card>
</template>
