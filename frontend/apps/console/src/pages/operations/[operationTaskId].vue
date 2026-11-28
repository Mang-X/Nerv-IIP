<script setup lang="ts">
import { consolePagePermissions } from '@/router/access'
import OperationTimeline from '@/components/console/OperationTimeline.vue'
import { useOperationTask } from '@/composables/useConsoleOperations'
import { isForbiddenRequestError } from '@nerv-iip/api-client'
import { useHasPermission } from '@/composables/usePermissions'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Button, NvPageHeader } from '@nerv-iip/ui'
import { ArrowLeftIcon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    requiredPermissions: consolePagePermissions['/operations'],
    title: '运维任务',
  },
})

const route = useRoute('/operations/[operationTaskId]')
const operationTaskId = computed(() => String(route.params.operationTaskId ?? ''))

const { operationError, operationPending, operationTask, refreshOperation } =
  useOperationTask(operationTaskId)

const canReadInstances = useHasPermission('apphub.instances.read')
const missing = computed(() => {
  const error = operationError.value as
    | { status?: number; response?: { status?: number } }
    | undefined
  return error?.status === 404 || error?.response?.status === 404
})
const errorMessage = computed(() => {
  if (!operationError.value || missing.value) return undefined
  return isForbiddenRequestError(operationError.value)
    ? '当前账号无法读取此任务。'
    : '无法读取任务，请重试。'
})

async function retry() {
  await refreshOperation().catch(() => {})
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <NvPageHeader
        title="运维任务"
        :breadcrumbs="[{ label: '平台' }, { label: '实例' }]"
        :count="operationTaskId"
      >
        <template #actions>
          <Button v-if="canReadInstances" size="sm" type="button" variant="outline" as-child>
            <RouterLink to="/">
              <ArrowLeftIcon class="size-4" aria-hidden="true" />
              返回实例
            </RouterLink>
          </Button>
        </template>
      </NvPageHeader>

      <div v-if="errorMessage" class="flex items-center gap-3">
        <p class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>
        <Button size="sm" variant="outline" @click="retry">重试</Button>
      </div>
      <p v-else-if="operationPending" role="status" class="text-sm">正在加载任务。</p>
      <OperationTimeline v-else-if="operationTask" :operation-task="operationTask" />
      <p v-else class="text-sm text-muted-foreground">未找到此运维任务。</p>
    </section>
  </DefaultLayout>
</template>
