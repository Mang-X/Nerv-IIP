<script setup lang="ts">
import type { BusinessConsoleMesEngineeringChangeImpact } from '@nerv-iip/api-client'
import { NvDataTable, NvStatusBadge, type NvDataTableColumn, type StatusTone } from '@nerv-iip/ui'
import { computed, watch } from 'vue'
import { RouterLink } from 'vue-router'
import type { MesSelectedWorkScope } from '@/composables/mes/useMesWorkScope'
import { formatDateTime } from '@/utils/format'
import { useEngineeringWorkOrderImpacts } from '@/composables/mes/useEngineeringWorkOrderImpacts'

const props = defineProps<{
  organizationId: string
  environmentId: string
  changeNumber?: string
  workOrderId?: string
  scope?: MesSelectedWorkScope
  refreshVersion?: number
}>()

const { canRead, ready, page, pageSize, result, pending, failed, refresh } =
  useEngineeringWorkOrderImpacts(() => ({
    organizationId: props.organizationId,
    environmentId: props.environmentId,
    ...(props.workOrderId
      ? { workOrderId: props.workOrderId }
      : { changeNumber: props.changeNumber }),
    ...(props.scope ? { scopeKind: props.scope.kind, scopeId: props.scope.id } : {}),
  }))

watch(() => props.refreshVersion, refresh)

const title = computed(() => (props.workOrderId ? '工程变更影响' : '在制影响'))
const columns = computed<NvDataTableColumn<BusinessConsoleMesEngineeringChangeImpact>[]>(() => [
  props.workOrderId
    ? { key: 'changeNumber', header: '变更单号', width: 'w-44' }
    : { key: 'workOrderId', header: '工单号' },
  { key: 'disposition', header: '处置状态', width: 'w-44' },
  ...(props.workOrderId ? [{ key: 'decision', header: '决策留痕' }] : []),
])

const dispositionDisplay = {
  'pending-decision': { label: '待确认', tone: 'warning' },
  'blocked-for-manual-confirmation': { label: '已阻塞 · 待人工确认', tone: 'danger' },
  'auto-rebound': { label: '已重绑', tone: 'success' },
  'continue-with-archived-version': { label: '继续旧版', tone: 'info' },
  'abort-work-order': { label: '已中止', tone: 'danger' },
} satisfies Record<
  BusinessConsoleMesEngineeringChangeImpact['disposition'],
  { label: string; tone: StatusTone }
>
</script>

<template>
  <section v-if="canRead && ready" class="grid min-w-0 gap-2" :aria-label="title">
    <h2 class="text-sm font-semibold">{{ title }}</h2>
    <NvDataTable
      manual
      v-model:page="page"
      :page-size="pageSize"
      :total-items="result?.total ?? 0"
      :columns="columns"
      :rows="result?.items ?? []"
      :row-key="workOrderId ? 'changeNumber' : 'workOrderId'"
      :loading="pending"
      :error="failed"
      :error-message="`读取${title}失败，请重试。`"
      :searchable="false"
      :column-settings="false"
      :skeleton-rows="2"
      :empty-message="`当前作业范围暂无${title}记录。`"
      refreshable
      @update:page-size="pageSize = String($event)"
      @refresh="refresh"
      @retry="refresh"
    >
      <template #cell-workOrderId="{ row }">
        <RouterLink
          class="break-all text-primary underline-offset-4 hover:underline"
          :to="`/mes/work-orders/${encodeURIComponent(row.workOrderId)}`"
          >{{ row.workOrderId }}</RouterLink
        >
      </template>
      <template #cell-changeNumber="{ row }">
        <RouterLink
          class="break-all text-primary underline-offset-4 hover:underline"
          :to="`/engineering/eco?changeNumber=${encodeURIComponent(row.changeNumber)}`"
          >{{ row.changeNumber }}</RouterLink
        >
      </template>
      <template #cell-decision="{ row }">
        <dl
          v-if="row.decidedBy"
          class="grid grid-cols-[auto_1fr] gap-x-2 gap-y-1 whitespace-normal break-words text-sm"
        >
          <dt class="text-muted-foreground">决策人</dt>
          <dd>{{ row.decidedBy }}</dd>
          <dt class="text-muted-foreground">决策时间</dt>
          <dd>
            <time :datetime="row.decidedAtUtc ?? undefined">{{
              formatDateTime(row.decidedAtUtc)
            }}</time>
          </dd>
          <dt class="text-muted-foreground">决策原因</dt>
          <dd>{{ row.decisionReason }}</dd>
        </dl>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-disposition="{ row }">
        <NvStatusBadge v-bind="dispositionDisplay[row.disposition]" />
      </template>
    </NvDataTable>
  </section>
</template>
