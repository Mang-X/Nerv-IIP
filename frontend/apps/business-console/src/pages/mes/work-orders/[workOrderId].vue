<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { describeMesReadinessReason, useMesWorkOrderDetail } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  NvStatusBadge,
} from '@nerv-iip/ui'
import {
  ClipboardCheckIcon,
  PackageCheckIcon,
  RefreshCwIcon,
  ShieldCheckIcon,
} from 'lucide-vue-next'
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工单详情',
    requiredPermissions: ['business.mes.work-orders.read'],
  },
})

const route = useRoute()
const router = useRouter()
const {
  detail,
  detailError,
  detailPending,
  filters,
  materialReadiness,
  materialReadinessError,
  materialReadinessPending,
  refreshDetail,
  refreshMaterialReadiness,
} = useMesWorkOrderDetail()

watch(
  () => (route.params as Record<string, string | string[] | undefined>).workOrderId,
  (value) => {
    filters.workOrderId = (Array.isArray(value) ? value[0] : value) ?? ''
  },
  { immediate: true },
)

const operationTasks = computed(() => detail.value?.operationTasks ?? [])
const materialRows = computed(() => materialReadiness.value?.items ?? [])
const blockingReasons = computed(() => [
  ...(detail.value?.blockingReasons ?? []),
  ...(materialReadiness.value?.blockingReasons ?? []),
])
const blockingReasonDisplays = computed(() => blockingReasons.value.map(describeMesReadinessReason))
const errorMessage = computed(
  () => formatError(detailError.value) || formatError(materialReadinessError.value),
)

type TaskRow = (typeof operationTasks)['value'][number]
const taskColumns: NvDataTableColumn<TaskRow>[] = [
  {
    key: 'operationTaskId',
    header: '任务',
    cellClass: 'font-medium',
    accessor: (r) => r.operationTaskId ?? '无',
  },
  { key: 'status', header: '状态', width: 'w-24' },
  {
    key: 'operationSequence',
    header: '序号',
    align: 'end',
    width: 'w-16',
    accessor: (r) => r.operationSequence ?? 0,
  },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterId ?? '无' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '未指定' },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '未指定' },
  { key: 'startedAtUtc', header: '开始', width: 'w-44' },
  { key: 'qualityStatus', header: '质量', accessor: (r) => r.qualityStatus ?? '未检' },
]

type MaterialRow = (typeof materialRows)['value'][number]
const materialColumns: NvDataTableColumn<MaterialRow>[] = [
  {
    key: 'materialId',
    header: '物料',
    cellClass: 'font-medium',
    accessor: (r) => r.materialId ?? '无',
  },
  { key: 'materialLotId', header: '批次', accessor: (r) => r.materialLotId ?? '未指定' },
  { key: 'requiredQuantity', header: '需求', align: 'end', width: 'w-20' },
  { key: 'availableQuantity', header: '可用', align: 'end', width: 'w-20' },
  { key: 'stagedQuantity', header: '已备', align: 'end', width: 'w-20' },
  { key: 'shortageQuantity', header: '短缺', align: 'end', width: 'w-20' },
  { key: 'status', header: '状态', width: 'w-24' },
]

function refreshAll() {
  void refreshDetail()
  void refreshMaterialReadiness()
}
function openRoute(path: string) {
  void router.push({
    path,
    query: {
      workOrderId: filters.workOrderId,
      skuId: detail.value?.skuId ?? undefined,
      quantity: detail.value?.quantity?.toString() ?? undefined,
    },
  })
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatStatus(value?: string | null) {
  const map: Record<string, string> = {
    blocked: '阻塞',
    closed: '已关闭',
    completed: '已完成',
    ready: '可开工',
    released: '已下达',
    running: '执行中',
    warning: '预警',
  }
  return value ? (map[value.toLowerCase()] ?? value) : '未知'
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      :title="`工单 ${filters.workOrderId}`"
      :breadcrumbs="[{ label: '制造执行' }, { label: '工单与派工' }]"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          @click="openRoute('/mes/production-reports')"
        >
          <ClipboardCheckIcon aria-hidden="true" />
          报工记录
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" @click="openRoute('/mes/receipts')">
          <PackageCheckIcon aria-hidden="true" />
          完工入库
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          @click="openRoute('/quality/inspections')"
        >
          <ShieldCheckIcon aria-hidden="true" />
          质量检验
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="detailPending || materialReadinessPending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvSectionCards :columns="4">
      <NvSectionCard
        description="工单状态"
        :value="formatStatus(detail?.status)"
        :hint="detail?.skuId ?? '无物料'"
      />
      <NvSectionCard
        description="计划数量"
        :value="formatQuantity(detail?.quantity)"
        hint="工单计划量"
      />
      <NvSectionCard description="工序数" :value="operationTasks.length" hint="执行任务" />
      <NvSectionCard
        description="用料状态"
        :value="formatStatus(materialReadiness?.readinessStatus)"
        hint="齐套检查"
      />
    </NvSectionCards>

    <div v-if="blockingReasons.length" class="rounded-lg border bg-background p-4">
      <h2 class="text-sm font-semibold text-foreground">开工阻塞</h2>
      <div class="mt-3 grid gap-2">
        <div
          v-for="reason in blockingReasonDisplays"
          :key="reason.code"
          class="rounded-md border border-warning/30 bg-warning/10 p-3"
        >
          <NvStatusBadge :label="reason.label" tone="warning" />
          <p class="mt-2 text-sm text-muted-foreground">{{ reason.nextStep }}</p>
        </div>
      </div>
    </div>

    <div class="grid gap-2">
      <span class="text-sm font-semibold text-foreground">工序任务</span>
      <NvDataTable
        :columns="taskColumns"
        :rows="operationTasks"
        row-key="operationTaskId"
        :loading="detailPending"
        empty-message="暂无工序任务。"
        :searchable="false"
        :column-settings="false"
      >
        <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
        <template #cell-operationSequence="{ row }"
          ><span class="tabular-nums">{{ row.operationSequence ?? 0 }}</span></template
        >
        <template #cell-startedAtUtc="{ row }">{{
          formatDateTime(row.startedAtUtc ?? row.plannedStartUtc)
        }}</template>
      </NvDataTable>
    </div>

    <div class="grid gap-2">
      <span class="text-sm font-semibold text-foreground">用料齐套</span>
      <NvDataTable
        :columns="materialColumns"
        :rows="materialRows"
        :row-key="(r) => `${r.materialId}-${r.materialLotId}`"
        :loading="materialReadinessPending"
        empty-message="暂无用料行。"
        :searchable="false"
        :column-settings="false"
      >
        <template #cell-requiredQuantity="{ row }"
          ><span class="tabular-nums">{{ formatQuantity(row.requiredQuantity) }}</span></template
        >
        <template #cell-availableQuantity="{ row }"
          ><span class="tabular-nums">{{ formatQuantity(row.availableQuantity) }}</span></template
        >
        <template #cell-stagedQuantity="{ row }"
          ><span class="tabular-nums">{{ formatQuantity(row.stagedQuantity) }}</span></template
        >
        <template #cell-shortageQuantity="{ row }"
          ><span class="tabular-nums">{{ formatQuantity(row.shortageQuantity) }}</span></template
        >
        <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      </NvDataTable>
    </div>
  </BusinessLayout>
</template>
