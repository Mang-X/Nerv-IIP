<script setup lang="ts">
import WarehouseTaskExecutionView, {
  type WarehouseTaskExecutionIntent,
} from '@/components/wms/WarehouseTaskExecutionView.vue'
import { useWmsPutaway } from '@/composables/useBusinessWms'
import { useWmsOperationalCandidates } from '@/composables/useWmsOperationalCandidates'
import { NvAppShellMobile } from '@nerv-iip/ui-mobile'

definePage({
  meta: {
    requiresAuth: true,
    title: '上架',
  },
})

const {
  filters,
  organizationId,
  environmentId,
  principalId,
  scopeKind,
  scopeId,
  scopeReady,
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

const candidates = useWmsOperationalCandidates('receipt', {
  organizationId,
  environmentId,
  scopeKind,
  scopeId,
  scopeReady,
  filters,
})

async function refreshAll() {
  await Promise.all([refresh(), candidates.refresh()])
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
        v-model:location-code="filters.locationCode"
        v-model:lot-no="filters.lotNo"
        v-model:candidate-search-keyword="candidates.searchKeyword.value"
        title="上架"
        task-type="putaway"
        :tasks="tasks"
        :total="total"
        :pending="pending"
        :refreshing="refreshing"
        :loading-more="loadingMore"
        :current-principal-id="principalId"
        :scope-options="scopeOptions"
        :location-options="candidates.locationOptions.value"
        :lot-options="candidates.lotOptions.value"
        :candidate-ready="candidates.ready.value"
        :candidate-source-label="candidates.sourceLabel.value"
        :candidate-as-of-utc="candidates.asOfUtc.value"
        :candidate-freshness-utc="candidates.freshnessUtc.value"
        :candidate-truncated="candidates.truncated.value"
        :candidate-pending="candidates.pending.value"
        :candidate-error="candidates.error.value"
        :error="error"
        :action-pending="actionPending"
        @refresh="refreshAll"
        @retry="refreshAll"
        @candidate-retry="candidates.refresh"
        @load-more="loadMore"
        @execute="execute"
      />
    </div>
  </NvAppShellMobile>
</template>
