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
    <div class="flex flex-col gap-4">
      <RouterLink
        class="w-fit text-sm font-semibold text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded"
        to="/"
      >
        ← Back to instances
      </RouterLink>
      <Alert v-if="operationError" variant="destructive">
        <AlertDescription>{{ operationError.message }}</AlertDescription>
      </Alert>
      <OperationTimeline :operation-task="operationTask" :pending="operationPending" />
    </div>
  </DefaultLayout>
</template>
