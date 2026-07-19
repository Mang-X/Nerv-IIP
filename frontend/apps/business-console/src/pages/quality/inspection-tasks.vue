<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import {
  useQualityInspectionTasks,
  isInspectionTaskOverdue,
} from '@/composables/useQualityInspectionTasks'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvField,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
} from '@nerv-iip/ui'
import { AlertCircleIcon, ArrowRightIcon, ClipboardCheckIcon, RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '待检工作台',
    requiredPermissions: ['business.quality.inspection-records.read'],
  },
})

const router = useRouter()
const { filters, tasks, total, pending, error, refreshTasks } = useQualityInspectionTasks({
  status: 'pending',
})
const { page, pageSize } = usePagedList(filters, {
  initialPageSize: '200',
  resetOn: [() => filters.sourceType, () => filters.skuCode],
})

const sourceTabs = [
  { label: '全部来源', value: 'all' as const },
  { label: '来料', value: 'receiving' as const },
  { label: '过程', value: 'operation' as const },
  { label: '终检', value: 'final' as const },
]

const listErrorMessage = computed(() => formatError(error.value))
const today = new Date()
const overdueCount = computed(
  () => tasks.value.filter((task) => isInspectionTaskOverdue(task, today)).length,
)
const completedToday = computed(() => '—')
const completedTodayHint = 'Quality facade 当前未返回完成时间，暂不伪造今日统计'

const columns: NvDataTableColumn<BusinessConsoleQualityInspectionTaskItem>[] = [
  {
    key: 'inspectionTaskId',
    header: '任务号',
    width: 'w-44',
    accessor: (row) => row.inspectionTaskId ?? '—',
  },
  {
    key: 'sourceDocumentId',
    header: '来源引用',
    width: 'w-40',
    accessor: (row) => row.sourceDocumentId ?? '—',
  },
  {
    key: 'sourceType',
    header: '来源类型',
    width: 'w-24',
    accessor: (row) => sourceLabel(row.sourceType),
  },
  { key: 'skuCode', header: 'SKU', width: 'w-36', accessor: (row) => row.skuCode ?? '—' },
  {
    key: 'inspectionPlanId',
    header: '检验计划',
    width: 'w-36',
    accessor: (row) => row.inspectionPlanId ?? '—',
  },
  {
    key: 'createdAtUtc',
    header: '生成时间',
    width: 'w-40',
    accessor: (row) => formatDateTime(row.createdAtUtc),
  },
  {
    key: 'dueAtUtc',
    header: '时限',
    width: 'w-36',
    accessor: (row) => formatDateTime(row.dueAtUtc),
  },
  { key: 'actions', header: '操作', width: 'w-32', accessor: () => '' },
]

function sourceLabel(value?: string | null) {
  return sourceTabs.find((tab) => tab.value === value)?.label ?? '其他来源'
}

function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('zh-CN', { dateStyle: 'short', timeStyle: 'short' }).format(date)
}

function formatError(errorValue: unknown) {
  if (!errorValue) return ''
  if (errorValue instanceof Error && errorValue.message.includes('403')) {
    return '当前账号没有查看质检待检任务的权限。'
  }
  return '待检任务加载失败，请稍后重试。'
}

function sourceDocumentRoute(task: BusinessConsoleQualityInspectionTaskItem) {
  const sourceService = task.sourceService?.trim().toLowerCase()
  if (task.sourceType === 'receiving') return sourceService === 'wms' ? '/wms/inbound' : ''
  if (sourceService !== 'mes') return ''
  const workOrderId =
    task.sourceType === 'final' ? task.sourceDocumentLineId : task.sourceDocumentId
  return workOrderId ? `/mes/work-orders/${encodeURIComponent(workOrderId)}` : ''
}

function goToInspectionForm(task: BusinessConsoleQualityInspectionTaskItem) {
  const inspectionTaskId = task.inspectionTaskId?.trim()
  if (!inspectionTaskId) return
  void router.push({
    path: '/quality/inspections',
    query: {
      inspectionTaskId,
      sourceDocumentId: task.sourceDocumentId ?? undefined,
      sourceType: task.sourceType ?? undefined,
      sourceService: task.sourceService ?? undefined,
      skuCode: task.skuCode ?? undefined,
      inspectionPlanId: task.inspectionPlanId ?? undefined,
      quantity: task.quantity?.toString() ?? undefined,
      batchNo: task.batchNo ?? undefined,
      serialNo: task.serialNo ?? undefined,
      action: 'create',
    },
  })
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="质检待检工作台"
      :breadcrumbs="[{ label: '质量管理' }]"
      :count="`${total} 个待检任务`"
    >
      <template #actions>
        <NvButton size="sm" variant="outline" :disabled="pending" @click="refreshTasks">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="3">
      <NvSectionCard description="待检总量" :value="total" hint="来自当前业务范围的待检任务总数" />
      <NvSectionCard
        description="超期任务"
        :value="overdueCount"
        hint="当前返回页中已超过检验时限的任务"
      />
      <NvSectionCard description="今日完成" :value="completedToday" :hint="completedTodayHint" />
    </NvSectionCards>

    <div class="grid gap-4 rounded-xl border bg-card p-4 shadow-sm">
      <div class="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h2 class="text-base font-semibold">先处理最紧急的任务</h2>
          <p class="mt-1 text-sm text-muted-foreground">超期任务会置顶，并显示文字与时间提示。</p>
        </div>
        <div class="flex flex-wrap gap-2" role="tablist" aria-label="来源类型">
          <NvButton
            v-for="tab in sourceTabs"
            :key="tab.value"
            size="sm"
            :variant="filters.sourceType === tab.value ? 'default' : 'outline'"
            role="tab"
            :aria-selected="filters.sourceType === tab.value"
            @click="filters.sourceType = tab.value"
          >
            {{ tab.label }}
          </NvButton>
        </div>
      </div>

      <div class="grid gap-3 sm:grid-cols-[minmax(0,280px)_auto] sm:items-end">
        <NvField>
          <NvFieldLabel for="inspection-task-sku">按 SKU 查找</NvFieldLabel>
          <NvInput id="inspection-task-sku" v-model="filters.skuCode" placeholder="输入 SKU 编码" />
        </NvField>
        <p class="text-sm text-muted-foreground">
          共 {{ total }} 个待检任务；当前批次最多加载 200 条，来源筛选作用于当前批次。
        </p>
      </div>
    </div>

    <p
      v-if="listErrorMessage"
      class="flex items-center gap-2 text-sm text-destructive"
      role="alert"
    >
      <AlertCircleIcon aria-hidden="true" />
      {{ listErrorMessage }}
      <NvButton size="sm" variant="outline" @click="refreshTasks">重试</NvButton>
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :page-size-options="[50, 100, 200]"
      :total-items="total"
      :columns="columns"
      :rows="tasks"
      row-key="inspectionTaskId"
      :loading="pending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前没有待检任务。免检 SKU 不会生成任务；若刚完成收货或报工，请刷新后再查看。"
      @update:page="page = $event"
      @update:page-size="(value) => (pageSize = String(value))"
    >
      <template #cell-sourceDocumentId="{ row }">
        <RouterLink
          v-if="sourceDocumentRoute(row)"
          class="font-medium underline underline-offset-2"
          :to="sourceDocumentRoute(row)"
        >
          {{ row.sourceDocumentId ?? '—' }}
        </RouterLink>
        <span v-else>{{ row.sourceDocumentId ?? '—' }}</span>
      </template>
      <template #cell-sourceType="{ row }">{{ sourceLabel(row.sourceType) }}</template>
      <template #cell-dueAtUtc="{ row }">
        <span
          v-if="isInspectionTaskOverdue(row)"
          class="inline-flex items-center gap-1 font-medium text-destructive"
        >
          <AlertCircleIcon aria-hidden="true" />
          已超期 · {{ formatDateTime(row.dueAtUtc) }}
        </span>
        <span v-else>{{ formatDateTime(row.dueAtUtc) }}</span>
      </template>
      <template #cell-actions="{ row }">
        <NvButton size="sm" :disabled="!row.inspectionTaskId" @click="goToInspectionForm(row)">
          <ClipboardCheckIcon aria-hidden="true" />
          开始检验
          <ArrowRightIcon aria-hidden="true" />
        </NvButton>
      </template>
    </NvDataTable>

    <p class="text-xs text-muted-foreground">
      任务来源、SKU、计划和时限均来自 Quality 待检任务；开始检验后会带入既有检验记录流程。
      <RouterLink class="underline underline-offset-2" to="/quality/inspections"
        >查看检验记录</RouterLink
      >
    </p>
  </BusinessLayout>
</template>
