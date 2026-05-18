<script setup lang="ts">
import OperationTimeline from '@/components/console/OperationTimeline.vue'
import { useOperationTask } from '@/composables/useConsoleOperations'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
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
      <p v-if="operationError" class="operation-page__error">{{ operationError.message }}</p>
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

.operation-page__error {
  background: var(--legacy-color-surface);
  border: 1px solid #fecaca;
  border-radius: 0.5rem;
  color: var(--legacy-color-danger);
  font-weight: 700;
  margin: 0;
  padding: 0.75rem 0.9rem;
}
</style>
