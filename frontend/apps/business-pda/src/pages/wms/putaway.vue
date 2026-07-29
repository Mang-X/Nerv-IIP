<script setup lang="ts">
import WarehouseTaskExecutionView, {
  type WarehouseTaskExecutionIntent,
} from '@/components/wms/WarehouseTaskExecutionView.vue'
import { useWmsPutaway } from '@/composables/useBusinessWms'
import { NvAppShellMobile } from '@nerv-iip/ui-mobile'

definePage({
  meta: {
    requiresAuth: true,
    title: '上架',
  },
})

const {
  filters,
  scopeKey,
  scopeOptions,
  tasks,
  total,
  pending,
  error,
  refreshing,
  loadingMore,
  actionPending,
  refresh,
  loadMore,
  executeTask,
} = useWmsPutaway({ status: 'Open' })

function scanLocation(value: string) {
  filters.locationCode = value.trim() || undefined
}

async function execute(intent: WarehouseTaskExecutionIntent) {
  try {
    await executeTask(intent)
  } catch {
    // Mutation error is exposed by the composable and rendered by the shared retry banner.
  }
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">上架</h1>
      </div>
    </template>

    <div class="flex h-full min-h-0 flex-col">
      <WarehouseTaskExecutionView
        v-model:status="filters.status"
        v-model:scope-key="scopeKey"
        v-model:keyword="filters.keyword"
        title="上架"
        task-type="putaway"
        :tasks="tasks"
        :total="total"
        :pending="pending"
        :refreshing="refreshing"
        :loading-more="loadingMore"
        :scope-options="scopeOptions"
        :location-code="filters.locationCode"
        :error="error"
        :action-pending="actionPending"
        @scan-location="scanLocation"
        @refresh="refresh"
        @retry="refresh"
        @load-more="loadMore"
        @execute="execute"
      />
    </div>
  </NvAppShellMobile>
</template>
