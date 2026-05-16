<script setup lang="ts">
import { UiBadge } from '@nerv-iip/ui'
import type { InstanceDetailResponse } from '@nerv-iip/api-client'
import { computed } from 'vue'

const props = defineProps<{
  instance?: InstanceDetailResponse
  pending?: boolean
}>()

const metadataEntries = computed(() => Object.entries(props.instance?.metadata ?? {}))

function badgeTone(status?: string | null) {
  const normalized = status?.toLowerCase()

  if (normalized === 'running' || normalized === 'healthy') {
    return 'success'
  }

  if (normalized === 'degraded' || normalized === 'pending' || normalized === 'starting') {
    return 'warning'
  }

  if (normalized === 'failed' || normalized === 'unhealthy' || normalized === 'stopped') {
    return 'danger'
  }

  return 'neutral'
}
</script>

<template>
  <aside class="detail-panel" aria-labelledby="detail-panel-title">
    <div class="detail-panel__header">
      <p class="detail-panel__eyebrow">Selected</p>
      <h2 id="detail-panel-title" class="detail-panel__title">
        {{ instance?.instanceName ?? instance?.instanceKey ?? 'Instance detail' }}
      </h2>
    </div>

    <p v-if="pending" class="detail-panel__muted">Loading detail...</p>

    <template v-else-if="instance">
      <dl class="detail-panel__facts">
        <div class="detail-panel__fact">
          <dt>Application</dt>
          <dd>{{ instance.applicationName ?? instance.applicationKey ?? 'Unknown' }}</dd>
        </div>
        <div class="detail-panel__fact">
          <dt>Node</dt>
          <dd>{{ instance.nodeName ?? instance.nodeKey ?? 'Unassigned' }}</dd>
        </div>
        <div class="detail-panel__fact">
          <dt>Status</dt>
          <dd>
            <UiBadge :tone="badgeTone(instance.reportedStatus)">
              {{ instance.reportedStatus ?? 'unknown' }}
            </UiBadge>
          </dd>
        </div>
        <div class="detail-panel__fact">
          <dt>Health</dt>
          <dd>
            <UiBadge :tone="badgeTone(instance.healthStatus)">
              {{ instance.healthStatus ?? 'unknown' }}
            </UiBadge>
          </dd>
        </div>
        <div class="detail-panel__fact">
          <dt>Last heartbeat</dt>
          <dd>{{ instance.lastHeartbeatAtUtc ?? 'Not reported' }}</dd>
        </div>
        <div class="detail-panel__fact">
          <dt>Last state</dt>
          <dd>{{ instance.lastStateObservedAtUtc ?? 'Not reported' }}</dd>
        </div>
      </dl>

      <section class="detail-panel__section" aria-labelledby="capabilities-title">
        <h3 id="capabilities-title" class="detail-panel__section-title">Capabilities</h3>
        <ul v-if="instance.capabilities?.length" class="detail-panel__capabilities">
          <li
            v-for="capability in instance.capabilities"
            :key="`${capability.capabilityCode}-${capability.capabilityVersion}`"
            class="detail-panel__capability"
          >
            <span class="detail-panel__capability-code">{{ capability.capabilityCode ?? 'unknown' }}</span>
            <span class="detail-panel__muted">{{ capability.category ?? 'uncategorized' }}</span>
            <span class="detail-panel__muted">{{ capability.supportedOperations?.join(', ') ?? 'No operations' }}</span>
          </li>
        </ul>
        <p v-else class="detail-panel__muted">No capabilities reported.</p>
      </section>

      <section class="detail-panel__section" aria-labelledby="metadata-title">
        <h3 id="metadata-title" class="detail-panel__section-title">Metadata</h3>
        <dl v-if="metadataEntries.length" class="detail-panel__metadata">
          <div v-for="[key, value] in metadataEntries" :key="key" class="detail-panel__metadata-row">
            <dt>{{ key }}</dt>
            <dd>{{ value }}</dd>
          </div>
        </dl>
        <p v-else class="detail-panel__muted">No metadata reported.</p>
      </section>
    </template>

    <p v-else class="detail-panel__muted">Select an instance to inspect its runtime facts.</p>
  </aside>
</template>

<style scoped>
.detail-panel {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  box-shadow: 0 10px 30px rgb(15 23 42 / 0.05);
  min-width: 0;
  padding: 1.15rem;
}

.detail-panel__header {
  border-bottom: 1px solid var(--color-border);
  margin: 0 -1.15rem 1rem;
  padding: 0 1.15rem 1rem;
}

.detail-panel__eyebrow {
  color: var(--color-accent);
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0;
  margin: 0 0 0.25rem;
  text-transform: uppercase;
}

.detail-panel__title {
  font-size: 1.1rem;
  line-height: 1.25;
  margin: 0;
  overflow-wrap: anywhere;
}

.detail-panel__facts {
  display: grid;
  gap: 0.75rem;
  margin: 0;
}

.detail-panel__fact,
.detail-panel__metadata-row {
  display: grid;
  gap: 0.25rem;
}

.detail-panel__fact dt,
.detail-panel__metadata-row dt {
  color: var(--color-text-muted);
  font-size: 0.76rem;
  font-weight: 800;
  letter-spacing: 0;
  text-transform: uppercase;
}

.detail-panel__fact dd,
.detail-panel__metadata-row dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.detail-panel__section {
  border-top: 1px solid var(--color-border);
  margin-top: 1rem;
  padding-top: 1rem;
}

.detail-panel__section-title {
  font-size: 0.92rem;
  margin: 0 0 0.7rem;
}

.detail-panel__capabilities {
  display: grid;
  gap: 0.65rem;
  list-style: none;
  margin: 0;
  padding: 0;
}

.detail-panel__capability {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 0.45rem;
  display: grid;
  gap: 0.25rem;
  padding: 0.7rem;
}

.detail-panel__capability-code {
  font-weight: 800;
  overflow-wrap: anywhere;
}

.detail-panel__metadata {
  display: grid;
  gap: 0.65rem;
  margin: 0;
}

.detail-panel__muted {
  color: var(--color-text-muted);
  margin: 0;
}
</style>
