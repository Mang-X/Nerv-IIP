<script setup lang="ts">
import InstanceDetailPanel from '@/components/console/InstanceDetailPanel.vue'
import InstanceTable from '@/components/console/InstanceTable.vue'
import { useConsoleInstances, useRestartOperation } from '@/composables/useConsoleOperations'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Alert, AlertDescription, AlertTitle, Button, PageHeader } from '@nerv-iip/ui'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.instances',
  },
})

const { t } = useI18n()
const {
  detail,
  detailError,
  detailPending,
  effectiveInstanceKey,
  instances,
  listError,
  listPending,
  refreshDetail,
  selectInstance,
} = useConsoleInstances()

const { latestOperationTask, restartError, restartInstance, restartPending } = useRestartOperation()
const router = useRouter()

const latestOperationPath = computed(() => {
  const operationTaskId = latestOperationTask.value?.operationTaskId
  return operationTaskId ? `/operations/${operationTaskId}` : undefined
})

async function handleRestart(instanceKey: string) {
  const task = await restartInstance(instanceKey)
  const operationTaskId = task?.operationTaskId
  if (operationTaskId) {
    await router.push(`/operations/${operationTaskId}`)
  }
}

async function handleRefreshDetail() {
  try {
    await refreshDetail()
  } catch {
    // The query error state renders the failure message.
  }
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <PageHeader :title="t('nav.instances')" :breadcrumbs="[{ label: t('nav.platform') }]" :count="`${instances.length} 个实例`" />

      <div class="grid items-start gap-4 lg:grid-cols-[1fr_22rem] xl:grid-cols-[1fr_26rem]">
      <div class="flex min-w-0 flex-col gap-3">
        <InstanceTable
          :instances="instances"
          :pending="listPending"
          :restart-pending="restartPending"
          :selected-instance-key="effectiveInstanceKey"
          @restart-instance="handleRestart"
          @select-instance="selectInstance"
        />

        <Alert v-if="listError" variant="destructive">
          <AlertDescription>{{ listError.message }}</AlertDescription>
        </Alert>
        <Alert v-if="restartError" variant="destructive">
          <AlertDescription>{{ restartError.message }}</AlertDescription>
        </Alert>

        <RouterLink
          v-if="latestOperationPath"
          class="console-page__operation-link rounded-lg border bg-background px-4 py-3 text-sm font-semibold text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          :to="latestOperationPath"
        >
          {{ t('home.latestOperationTask') }}
        </RouterLink>
      </div>

      <div class="min-w-0">
        <Alert v-if="detailError" variant="destructive" class="flex flex-col gap-3">
          <AlertTitle>{{ t('home.unableToLoadInstanceDetail') }}</AlertTitle>
          <AlertDescription>{{ detailError.message }}</AlertDescription>
          <Button
            :disabled="detailPending"
            size="sm"
            type="button"
            variant="outline"
            class="self-start"
            @click="handleRefreshDetail"
          >
            {{ t('action.retry') }}
          </Button>
        </Alert>
        <InstanceDetailPanel v-else :instance="detail" :pending="detailPending" />
      </div>
      </div>
    </section>
  </DefaultLayout>
</template>
