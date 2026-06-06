<script setup lang="ts">
import type { BusinessConsoleWmsWcsTaskItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useWmsWcsTasks } from '@/composables/useBusinessWms'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: 'WCS 任务' } })

const { filters, wcsTasks, wcsError, wcsPending, refreshWcs } = useWmsWcsTasks()

const errorMessage = computed(() => formatError(wcsError.value))
const failedCount = computed(() => wcsTasks.value.filter((t) => !!t.failedAtUtc || (t.status ?? '').toLowerCase() === 'failed').length)

type WcsRow = BusinessConsoleWmsWcsTaskItem
const columns: DataTableColumn<WcsRow>[] = [
  { key: 'externalTaskId', header: '外部任务号', cellClass: 'font-medium', accessor: (r) => r.externalTaskId ?? '无' },
  { key: 'adapterType', header: '适配器', accessor: (r) => r.adapterType ?? '无' },
  { key: 'warehouseTaskId', header: '仓库任务', cellClass: 'text-muted-foreground', accessor: (r) => r.warehouseTaskId ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'attemptCount', header: '尝试次数', align: 'end', width: 'w-24', accessor: (r) => r.attemptCount ?? 0 },
  { key: 'failure', header: '失败原因' },
  { key: 'dispatchedAtUtc', header: '派发时间', align: 'end', width: 'w-44', accessor: (r) => formatDateTime(r.dispatchedAtUtc) },
]

function rowKey(row: WcsRow) {
  return row.wcsTaskId ?? row.externalTaskId ?? 'WCS 任务'
}
function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="WCS 任务" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${wcsTasks.length} 个任务`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="wcsPending" @click="refreshWcs">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="WCS 任务" :value="wcsTasks.length" hint="当前返回总数" />
      <SectionCard description="失败任务" :value="failedCount" hint="需人工跟进重试" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.externalTaskId" class="h-9 w-40" placeholder="外部任务号" aria-label="外部任务号" />
        <Input v-model="filters.warehouseTaskId" class="h-9 w-40" placeholder="仓库任务" aria-label="仓库任务" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="wcsTasks"
      :row-key="rowKey"
      :loading="wcsPending"
      empty-message="暂无 WCS 任务。派发到设备控制系统的任务会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-failure="{ row }">
        <div v-if="row.failureCode || row.failureMessage" class="flex flex-col gap-0.5">
          <span class="text-sm text-destructive">{{ row.failureCode ?? '失败' }}</span>
          <span v-if="row.failureMessage" class="text-xs text-muted-foreground">{{ row.failureMessage }}</span>
        </div>
        <span v-else class="text-muted-foreground">无</span>
      </template>
    </DataTable>
  </BusinessLayout>
</template>
