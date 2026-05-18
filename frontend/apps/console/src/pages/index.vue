<script setup lang="ts">
import InstanceDetailPanel from '@/components/console/InstanceDetailPanel.vue'
import InstanceTable from '@/components/console/InstanceTable.vue'
import { useConsoleInstances, useRestartOperation } from '@/composables/useConsoleOperations'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Alert, AlertDescription, Skeleton } from '@nerv-iip/ui'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'Instances',
  },
})

const {
  detail,
  detailPending,
  effectiveInstanceKey,
  instances,
  listError,
  listPending,
  selectInstance,
} = useConsoleInstances()

const { latestOperationTask, restartError, restartInstance, restartPending } = useRestartOperation()

const latestOperationPath = computed(() => {
  const operationTaskId = latestOperationTask.value?.operationTaskId

  return operationTaskId ? `/operations/${operationTaskId}` : undefined
})

async function handleRestart(instanceKey: string) {
  await restartInstance(instanceKey)
}
</script>

<template>
  <DefaultLayout>
    <div class="console-page">
      <div class="console-page__content">
        <InstanceTable
          :instances="instances"
          :restart-pending="restartPending"
          :selected-instance-key="effectiveInstanceKey"
          @restart-instance="handleRestart"
          @select-instance="selectInstance"
        />

        <div v-if="listPending" role="status" aria-label="Loading instances">
          <Skeleton class="h-12 w-full" />
          <span class="sr-only">Loading instances...</span>
        </div>
        <Alert v-if="listError" variant="destructive">
          <AlertDescription>{{ listError.message }}</AlertDescription>
        </Alert>
        <Alert v-if="restartError" variant="destructive">
          <AlertDescription>{{ restartError.message }}</AlertDescription>
        </Alert>
        <RouterLink
          v-if="latestOperationPath"
          class="console-page__operation-link"
          :to="latestOperationPath"
        >
          Latest operation task
        </RouterLink>
      </div>

      <InstanceDetailPanel :instance="detail" :pending="detailPending" />
    </div>
  </DefaultLayout>
</template>

<style scoped>
.console-page {
  align-items: start;
  display: grid;
  gap: 1rem;
  grid-template-columns: minmax(0, 1fr) minmax(18rem, 24rem);
}

.console-page__content {
  display: grid;
  gap: 0.75rem;
  min-width: 0;
}

.console-page__operation-link {
  background: var(--legacy-color-surface);
  border: 1px solid var(--legacy-color-border);
  border-radius: 0.5rem;
  margin: 0;
  padding: 0.75rem 0.9rem;
}

.console-page__operation-link {
  color: var(--legacy-color-accent);
  font-weight: 800;
  text-decoration: none;
}

.console-page__operation-link:hover,
.console-page__operation-link:focus-visible {
  text-decoration: underline;
}

@media (max-width: 1080px) {
  .console-page {
    grid-template-columns: 1fr;
  }
}
</style>
