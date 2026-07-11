<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import { useWmsPicking } from '@/composables/useBusinessWms'
import { warehouseTaskStatusLabel } from '@nerv-iip/business-core'
import { NvAppShellMobile, NvListRow, NvScanBar } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '拣货',
  },
})

// 只读任务清单：拣货无逐任务 complete 端点，写闭环经父单复核发货过账。
const { filters, tasks, pending, error, refresh } = useWmsPicking()

// 空态仅在「无任务且无加载/错误」时出现，避免与错误/加载态打架。
const showEmpty = computed(() => !pending.value && !error.value && tasks.value.length === 0)

// 当前有库位筛选时才显示「清除」入口。
const hasFilter = computed(() => Boolean(filters.locationCode))

function onScan(value: string) {
  filters.locationCode = value
}

function clearFilter() {
  filters.locationCode = undefined
}

// 行标题优先任务号，退回源单号；副标题汇总 SKU/库位流向/数量/中文状态。
function rowTitle(taskNo?: string, sourceOrderNo?: string) {
  return `任务 ${taskNo || sourceOrderNo || ''}`.trim()
}

function rowSubtitle(task: {
  skuCode?: string
  fromLocationCode?: string
  toLocationCode?: string
  plannedQuantity?: number
  status?: string
}) {
  const parts = [
    `SKU ${task.skuCode ?? ''}`,
    `${task.fromLocationCode ?? ''}→${task.toLocationCode ?? ''}`,
    `数量 ${task.plannedQuantity ?? 0}`,
    warehouseTaskStatusLabel(task.status),
  ]
  return parts.join(' · ')
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">拣货</h1>
      </div>
    </template>

    <div class="space-y-4 p-4">
      <NvScanBar placeholder="扫描库位" @scan="onScan" />

      <div
        v-if="hasFilter"
        class="flex items-center justify-between rounded-lg border border-border bg-card px-4 py-2 text-sm"
      >
        <span class="truncate text-muted-foreground">库位 {{ filters.locationCode }}</span>
        <button
          type="button"
          data-testid="clear-filter"
          class="shrink-0 text-brand"
          @click="clearFilter"
        >
          清除
        </button>
      </div>

      <p class="text-xs text-muted-foreground">拣货完成经复核发货过账</p>

      <RetryableListError
        v-if="error"
        :error="error"
        :pending="pending"
        fallback="任务加载失败，请下拉重试或检查网络。"
        test-id="error-banner"
        @retry="() => refresh()"
      />

      <div
        v-if="showEmpty"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无拣货任务
      </div>

      <div v-else-if="tasks.length > 0" class="overflow-hidden rounded-lg border border-border">
        <NvListRow
          v-for="task in tasks"
          :key="task.warehouseTaskId"
          :interactive="false"
          :title="rowTitle(task.taskNo, task.sourceOrderNo)"
          :subtitle="rowSubtitle(task)"
        />
      </div>
    </div>
  </NvAppShellMobile>
</template>
