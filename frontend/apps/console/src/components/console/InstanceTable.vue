<script setup lang="ts">
import { UiBadge, UiButton } from '@nerv-iip/ui'
import type { InstanceListItem } from '@nerv-iip/api-client'
import { computed } from 'vue'

const props = defineProps<{
  instances: InstanceListItem[]
  restartPending?: boolean
  selectedInstanceKey?: string
}>()

const emit = defineEmits<{
  restartInstance: [instanceKey: string]
  selectInstance: [instanceKey: string]
}>()

const hasInstances = computed(() => props.instances.length > 0)

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

function instanceLabel(instance: InstanceListItem) {
  return instance.instanceName ?? instance.instanceKey ?? 'Unknown instance'
}

function instanceRowKey(instance: InstanceListItem, index: number) {
  return (
    instance.instanceKey ??
    `instance:${instance.instanceName ?? instance.applicationKey ?? 'unknown'}:${index}`
  )
}

function selectInstance(instance: InstanceListItem) {
  if (instance.instanceKey) {
    emit('selectInstance', instance.instanceKey)
  }
}

function restartInstance(instance: InstanceListItem) {
  if (instance.instanceKey) {
    emit('restartInstance', instance.instanceKey)
  }
}
</script>

<template>
  <section class="instance-table" aria-labelledby="instance-table-title">
    <div class="instance-table__header">
      <div>
        <p class="instance-table__eyebrow">Console</p>
        <h1 id="instance-table-title" class="instance-table__title">Instances</h1>
      </div>
      <span class="instance-table__count">{{ instances.length }} total</span>
    </div>

    <div v-if="hasInstances" class="instance-table__scroller">
      <table class="instance-table__table">
        <thead>
          <tr>
            <th scope="col">App</th>
            <th scope="col">Instance</th>
            <th scope="col">Node</th>
            <th scope="col">Status</th>
            <th scope="col">Health</th>
            <th scope="col">Action</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(instance, index) in instances"
            :key="instanceRowKey(instance, index)"
            :class="{
              'instance-table__row--selected': instance.instanceKey === selectedInstanceKey,
            }"
          >
            <td>
              <button
                class="instance-table__select"
                type="button"
                @click="selectInstance(instance)"
              >
                <span class="instance-table__primary">{{
                  instance.applicationName ?? instance.applicationKey ?? 'Unknown app'
                }}</span>
                <span class="instance-table__secondary">{{
                  instance.version ?? 'Unversioned'
                }}</span>
              </button>
            </td>
            <td>{{ instanceLabel(instance) }}</td>
            <td>{{ instance.nodeName ?? instance.nodeKey ?? 'Unassigned' }}</td>
            <td>
              <UiBadge :tone="badgeTone(instance.reportedStatus)">
                {{ instance.reportedStatus ?? 'unknown' }}
              </UiBadge>
            </td>
            <td>
              <UiBadge :tone="badgeTone(instance.healthStatus)">
                {{ instance.healthStatus ?? 'unknown' }}
              </UiBadge>
            </td>
            <td>
              <UiButton
                :disabled="restartPending || !instance.instanceKey"
                variant="secondary"
                @click="restartInstance(instance)"
              >
                Restart
              </UiButton>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <p v-else class="instance-table__empty">No instances found for this environment.</p>
  </section>
</template>

<style scoped>
.instance-table {
  background: var(--legacy-color-surface);
  border: 1px solid var(--legacy-color-border);
  border-radius: 0.5rem;
  box-shadow: 0 10px 30px rgb(15 23 42 / 0.06);
  min-width: 0;
  overflow: hidden;
}

.instance-table__header {
  align-items: center;
  border-bottom: 1px solid var(--legacy-color-border);
  display: flex;
  justify-content: space-between;
  padding: 1.15rem 1.25rem;
}

.instance-table__eyebrow {
  color: var(--legacy-color-accent);
  font-size: 0.75rem;
  font-weight: 800;
  letter-spacing: 0;
  margin: 0 0 0.25rem;
  text-transform: uppercase;
}

.instance-table__title {
  font-size: 1.35rem;
  line-height: 1.2;
  margin: 0;
}

.instance-table__count {
  color: var(--legacy-color-text-muted);
  font-size: 0.85rem;
  font-weight: 700;
  white-space: nowrap;
}

.instance-table__scroller {
  overflow-x: auto;
}

.instance-table__table {
  border-collapse: collapse;
  min-width: 760px;
  width: 100%;
}

.instance-table__table th,
.instance-table__table td {
  border-bottom: 1px solid #e8edf4;
  padding: 0.9rem 1rem;
  text-align: left;
  vertical-align: middle;
}

.instance-table__table th {
  color: var(--legacy-color-text-muted);
  font-size: 0.76rem;
  letter-spacing: 0;
  text-transform: uppercase;
}

.instance-table__table tr:last-child td {
  border-bottom: 0;
}

.instance-table__row--selected {
  background: #f2fbf9;
}

.instance-table__select {
  background: transparent;
  border: 0;
  color: inherit;
  cursor: pointer;
  display: grid;
  gap: 0.2rem;
  padding: 0;
  text-align: left;
}

.instance-table__select:focus-visible {
  border-radius: 0.35rem;
  box-shadow: 0 0 0 3px rgb(37 111 103 / 0.22);
  outline: none;
}

.instance-table__primary {
  font-weight: 800;
}

.instance-table__secondary {
  color: var(--legacy-color-text-muted);
  font-size: 0.82rem;
}

.instance-table__empty {
  color: var(--legacy-color-text-muted);
  margin: 0;
  padding: 1.25rem;
}
</style>
