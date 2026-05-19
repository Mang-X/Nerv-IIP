<script setup lang="ts">
import OperationTimeline from '@/components/console/OperationTimeline.vue'
import { useOperationTask } from '@/composables/useConsoleOperations'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Alert, AlertDescription } from '@nerv-iip/ui'
import { computed } from 'vue'
import { useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: 'Operation task',
  },
})

const route = useRoute('/operations/[operationTaskId]')
const operationTaskId = computed(() => String(route.params.operationTaskId ?? ''))

const { operationError, operationPending, operationTask } = useOperationTask(operationTaskId)
</script>

<template>
  <DefaultLayout>
    <div class="operation-page">
      <RouterLink class="operation-page__back" to="/">Back to instances</RouterLink>
      <Alert v-if="operationError" variant="destructive">
        <AlertDescription>{{ operationError.message }}</AlertDescription>
      </Alert>
      <OperationTimeline :operation-task="operationTask" :pending="operationPending" />
    </div>
  </DefaultLayout>
</template>

<style scoped>
.operation-page {
  display: grid;
  gap: 0.9rem;
}

.operation-page__back {
  color: var(--legacy-color-accent);
  font-weight: 800;
  text-decoration: none;
  width: fit-content;
}

.operation-page__back:hover,
.operation-page__back:focus-visible {
  text-decoration: underline;
}
</style>
