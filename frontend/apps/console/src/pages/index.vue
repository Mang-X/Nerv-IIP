<script setup lang="ts">
import { consolePagePermissions } from '@/router/access'
import InstanceDetailPanel from '@/components/console/InstanceDetailPanel.vue'
import InstanceTable from '@/components/console/InstanceTable.vue'
import { useConsoleInstances, useRestartOperation } from '@/composables/useConsoleOperations'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Alert, AlertDescription, AlertTitle, Button, NvPageHeader } from '@nerv-iip/ui'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useHasPermission } from '@/composables/usePermissions'
import { useRoute, useRouter } from 'vue-router'

definePage({
  alias: '/instances',
  meta: {
    requiresAuth: true,
    requiredPermissions: consolePagePermissions['/'],
    title: 'routes.instances',
  },
})

const { t } = useI18n()
const auth = useAuthStore()
const route = useRoute('/')
const canRestart = useHasPermission('ops.tasks.create')
const canReadTasks = useHasPermission('ops.tasks.read')
const {
  detail,
  detailError,
  detailPending,
  effectiveInstanceKey,
  instances,
  listError,
  listPending,
  refreshDetail,
  refreshInstances,
  selectInstance,
} = useConsoleInstances(typeof route.query.instance === 'string' ? route.query.instance : undefined)

const { latestOperationTask, restartError, restartInstance, restartPending } = useRestartOperation()
const router = useRouter()

const latestOperationPath = computed(() => {
  const operationTaskId = latestOperationTask.value?.operationTaskId
  return canReadTasks.value && operationTaskId ? `/operations/${operationTaskId}` : undefined
})

async function handleRestart(instanceKey: string) {
  const principal = auth.principal
  if (!canRestart.value || !principal?.organizationId || !principal.environmentId) return

  const instance = instances.value.find((row) => row.instanceKey === instanceKey)
  if (!instance) return
  if (
    !window.confirm(
      `重启实例 ${instance.instanceName ?? instanceKey}（${instanceKey}）？\n组织：${principal.organizationId}\n环境：${principal.environmentId}`,
    )
  )
    return

  const task = await restartInstance(instanceKey)
  const operationTaskId = task?.operationTaskId
  if (operationTaskId && canReadTasks.value) {
    await router.push(`/operations/${operationTaskId}`)
  }
}

async function handleRefreshInstances() {
  try {
    await refreshInstances()
  } catch {
    // The query error state renders the failure message.
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
      <NvPageHeader
        :title="t('nav.instances')"
        :breadcrumbs="[{ label: t('nav.platform') }]"
        :count="listError || listPending ? undefined : `${instances.length} 个实例`"
      />

      <div class="grid items-start gap-4 lg:grid-cols-[1fr_22rem] xl:grid-cols-[1fr_26rem]">
        <div class="flex min-w-0 flex-col gap-3">
          <Alert v-if="listError" variant="destructive" class="flex flex-col gap-3">
            <AlertTitle>{{ t('home.unableToLoadInstances') }}</AlertTitle>
            <AlertDescription>{{ listError.message }}</AlertDescription>
            <Button
              :disabled="listPending"
              size="sm"
              type="button"
              variant="outline"
              class="self-start"
              @click="handleRefreshInstances"
            >
              {{ t('action.retry') }}
            </Button>
          </Alert>
          <InstanceTable
            v-else
            :instances="instances"
            :pending="listPending"
            :can-restart="canRestart"
            :restart-pending="restartPending"
            :selected-instance-key="effectiveInstanceKey"
            @restart-instance="handleRestart"
            @select-instance="selectInstance"
          />

          <Alert v-if="restartError" variant="destructive">
            <AlertDescription>{{ restartError.message }}</AlertDescription>
          </Alert>

          <p v-if="latestOperationTask" role="status" class="text-sm">重启请求已受理。</p>
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
