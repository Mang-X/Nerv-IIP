<script setup lang="ts">
import OperationTimeline from '@/components/console/OperationTimeline.vue'
import { useOperationTask } from '@/composables/useConsoleOperations'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Button, PageHeader } from '@nerv-iip/ui'
import { ArrowLeftIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '运维任务',
  },
})

const route = useRoute('/operations/[operationTaskId]')
const operationTaskId = computed(() => String(route.params.operationTaskId ?? ''))

const { operationError, operationPending, operationTask } = useOperationTask(operationTaskId)

const errorMessage = computed(() => (operationError.value ? operationError.value.message : ''))
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <PageHeader title="运维任务" :breadcrumbs="[{ label: '平台' }, { label: '实例' }]" :count="operationTaskId">
        <template #actions>
          <Button size="sm" type="button" variant="outline" as-child>
            <RouterLink to="/">
              <ArrowLeftIcon class="size-4" aria-hidden="true" />
              返回实例
            </RouterLink>
          </Button>
        </template>
      </PageHeader>

      <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

      <OperationTimeline :operation-task="operationTask" :pending="operationPending" />
    </section>
  </DefaultLayout>
</template>
