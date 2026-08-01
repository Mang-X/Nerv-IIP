<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  capaActionTypeLabel,
  capaStatusLabel,
  capaStatusTone,
  useQualityCapaDetail,
  useQualityCapas,
  type QualityCapaActionItem,
  type QualityCapaItem,
} from '@/composables/useBusinessQualityLedgers'
import { usePagedList } from '@/composables/usePagedList'
import { formatDate, formatDateTime } from '@/utils/format'
import { friendlyErrorMessage } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvMetricCard,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
  NvToolbar,
  Spinner,
} from '@nerv-iip/ui'
import {
  CheckCircle2Icon,
  ClipboardListIcon,
  RefreshCwIcon,
  ShieldAlertIcon,
  ShieldCheckIcon,
  TriangleAlertIcon,
} from '@lucide/vue'
import { computed, shallowRef, watch } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '纠正措施',
    requiredPermissions: ['business.quality.ncr.read'],
  },
})

const {
  filters,
  capas,
  capasError,
  capasPending,
  capasTotal,
  capaOpenCount,
  capaEffectivenessVerifiedCount,
  capaClosedCount,
  capaOverdueCount,
  refreshCapas,
} = useQualityCapas()

const paging = usePagedList(filters, {
  initialPageSize: '20',
  resetOn: [() => filters.status, () => filters.overdueOnly, () => filters.keyword],
})

const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '进行中', value: 'open' },
  { label: '效果已验证', value: 'effectiveness-verified' },
  { label: '已关闭', value: 'closed' },
]
const overdueOptions = [
  { label: '全部单据', value: 'all' },
  { label: '仅看逾期', value: 'overdue' },
]

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const overdueFilter = computed({
  get: () => (filters.overdueOnly ? 'overdue' : 'all'),
  set: (value: string) => {
    filters.overdueOnly = value === 'overdue' ? true : undefined
  },
})
const keyword = computed({
  get: () => filters.keyword ?? '',
  set: (value: string) => {
    filters.keyword = value.trim() ? value : undefined
  },
})

const listErrorMessage = computed(() =>
  capasError.value
    ? friendlyErrorMessage(capasError.value, '纠正措施台账加载失败，请稍后重试。')
    : '',
)

// 详情抽屉：列表行给出即时上下文，明细项以单据详情读面为准。
const detailOpen = shallowRef(false)
const selectedCapa = shallowRef<QualityCapaItem>()
const { capaDetail, capaDetailError, capaDetailPending } = useQualityCapaDetail(() => ({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  correctiveActionId: detailOpen.value ? (selectedCapa.value?.correctiveActionId ?? '') : '',
}))
const detailCapa = computed<QualityCapaItem | undefined>(
  () => capaDetail.value ?? selectedCapa.value,
)
const detailActions = computed<QualityCapaActionItem[]>(() => detailCapa.value?.actions ?? [])
const detailErrorMessage = computed(() =>
  capaDetailError.value
    ? friendlyErrorMessage(capaDetailError.value, '纠正措施详情加载失败，请稍后重试。')
    : '',
)

watch(detailOpen, (open) => {
  if (!open) selectedCapa.value = undefined
})

function openCapa(row: QualityCapaItem) {
  selectedCapa.value = row
  detailOpen.value = true
}

function actionProgress(row: QualityCapaItem) {
  return `${row.completedActionCount ?? 0} / ${row.actionCount ?? 0}`
}

const columns: NvDataTableColumn<QualityCapaItem>[] = [
  {
    key: 'capaCode',
    header: 'CAPA 单号',
    cellClass: 'font-medium',
    accessor: (row) => row.capaCode?.trim() || row.correctiveActionId || '未知',
  },
  { key: 'sourceNcrId', header: '来源 NCR', accessor: (row) => row.sourceNcrId?.trim() || '无' },
  { key: 'rootCause', header: '根本原因', accessor: (row) => row.rootCause?.trim() || '待分析' },
  { key: 'ownerUserId', header: '负责人', accessor: (row) => row.ownerUserId?.trim() || '未指派' },
  { key: 'dueAtUtc', header: '到期', width: 'w-32', accessor: (row) => formatDate(row.dueAtUtc) },
  { key: 'status', header: '状态', width: 'w-32' },
  { key: 'progress', header: '措施进度', align: 'end', width: 'w-28', accessor: actionProgress },
  { key: 'overdue', header: '逾期', width: 'w-24' },
]

const actionColumns: NvDataTableColumn<QualityCapaActionItem>[] = [
  {
    key: 'actionType',
    header: '措施类型',
    width: 'w-28',
    accessor: (row) => capaActionTypeLabel(row.actionType),
  },
  {
    key: 'description',
    header: '措施内容',
    accessor: (row) => row.description?.trim() || '未填写',
  },
  { key: 'ownerUserId', header: '负责人', accessor: (row) => row.ownerUserId?.trim() || '未指派' },
  { key: 'dueAtUtc', header: '到期', width: 'w-28', accessor: (row) => formatDate(row.dueAtUtc) },
  { key: 'status', header: '状态', width: 'w-24' },
]

function capaRowKey(row: QualityCapaItem) {
  return row.correctiveActionId ?? row.capaCode ?? '未知'
}
function actionRowKey(row: QualityCapaActionItem) {
  return row.correctiveActionItemId ?? `${row.actionType ?? ''}-${row.description ?? ''}`
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="纠正措施"
      :breadcrumbs="[{ label: '质量管理' }]"
      :count="listErrorMessage ? '台账加载失败' : `${capasTotal} 张 CAPA`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/quality/ncrs"><ShieldAlertIcon aria-hidden="true" />不合格品</RouterLink>
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="capasPending"
          @click="refreshCapas"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <NvMetricCard
        variant="icon"
        label="进行中"
        :value="capaOpenCount"
        unit="张"
        tone="warning"
        :icon="ClipboardListIcon"
      />
      <NvMetricCard
        variant="icon"
        label="待关单"
        :value="capaEffectivenessVerifiedCount"
        unit="张"
        tone="brand"
        :icon="ShieldCheckIcon"
      />
      <NvMetricCard
        variant="icon"
        label="已关闭"
        :value="capaClosedCount"
        unit="张"
        tone="success"
        :icon="CheckCircle2Icon"
      />
      <NvMetricCard
        variant="icon"
        label="逾期"
        :value="capaOverdueCount"
        unit="张"
        tone="danger"
        :icon="TriangleAlertIcon"
      />
    </div>

    <NvToolbar
      v-model:search="keyword"
      search-placeholder="搜索 CAPA 单号 / 根本原因"
      search-label="搜索纠正措施"
    >
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="CAPA 状态">
            <NvSelectValue />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in statusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="overdueFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="逾期范围">
            <NvSelectValue />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in overdueOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p
      v-if="listErrorMessage"
      class="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive"
      role="alert"
    >
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      v-else
      manual
      :page="paging.page.value"
      :page-size="paging.pageSize.value"
      :total-items="capasTotal"
      :columns="columns"
      :rows="capas"
      :row-key="capaRowKey"
      :loading="capasPending"
      :searchable="false"
      :column-settings="false"
      row-class="cursor-pointer"
      empty-message="当前范围内没有纠正措施单。不合格品关单时开出的 8D 会出现在这里。"
      @update:page="paging.page.value = $event"
      @update:page-size="(value) => (paging.pageSize.value = String(value))"
      @row-click="openCapa"
    >
      <template #cell-status="{ row }">
        <NvStatusBadge
          :value="row.status"
          :label="capaStatusLabel(row.status)"
          :tone="capaStatusTone(row.status)"
        />
      </template>
      <template #cell-overdue="{ row }">
        <NvStatusBadge v-if="row.overdue" value="overdue" label="已逾期" tone="danger" />
        <span v-else class="text-sm text-muted-foreground">按期</span>
      </template>
    </NvDataTable>

    <NvSheet v-model:open="detailOpen">
      <NvSheetContent class="w-full gap-0 overflow-y-auto sm:max-w-2xl">
        <NvSheetHeader>
          <NvSheetTitle>{{ detailCapa?.capaCode ?? '纠正措施详情' }}</NvSheetTitle>
          <NvSheetDescription>
            8D 措施明细、效果验证结论与关单信息，来自该纠正措施单据。
          </NvSheetDescription>
        </NvSheetHeader>

        <!-- `grid-cols-1` 把隐式的 auto 轨道换成 `minmax(0,1fr)`；`[&>*]:min-w-0` 解掉栅格子项
             默认的 `min-width:auto`。两者缺一，8D 措施明细那张 5 列表格就会按「内容最小宽」
             把整块内容撑到 780px —— 抽屉只有 512px，右侧 285px 溢出到视口外（#1418）。
             钉住之后表格在自带的 overflow-auto 里横向滚动，抽屉本身不再被顶破。 -->
        <div class="grid grid-cols-1 content-start gap-4 px-4 pb-4 [&>*]:min-w-0">
          <p
            v-if="detailErrorMessage"
            class="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive"
            role="alert"
          >
            {{ detailErrorMessage }}
          </p>
          <p
            v-else-if="capaDetailPending && !detailCapa"
            class="flex items-center gap-2 text-sm text-muted-foreground"
            role="status"
          >
            <Spinner aria-hidden="true" />
            正在加载纠正措施详情…
          </p>

          <template v-if="detailCapa">
            <dl class="grid gap-3 sm:grid-cols-2">
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">状态</dt>
                <dd class="mt-1">
                  <NvStatusBadge
                    :value="detailCapa.status"
                    :label="capaStatusLabel(detailCapa.status)"
                    :tone="capaStatusTone(detailCapa.status)"
                  />
                </dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">措施进度</dt>
                <dd class="mt-1 text-lg font-semibold tabular-nums">
                  {{ actionProgress(detailCapa) }}
                </dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">来源 NCR</dt>
                <dd class="mt-1 text-sm">{{ detailCapa.sourceNcrId?.trim() || '无' }}</dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">负责人</dt>
                <dd class="mt-1 text-sm">{{ detailCapa.ownerUserId?.trim() || '未指派' }}</dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">到期</dt>
                <dd
                  class="mt-1 text-sm"
                  :class="detailCapa.overdue ? 'font-semibold text-destructive' : ''"
                >
                  {{ formatDate(detailCapa.dueAtUtc) }}
                  <span v-if="detailCapa.overdue">（已逾期）</span>
                </dd>
              </div>
              <div class="rounded-lg border bg-card p-3">
                <dt class="text-xs text-muted-foreground">临时围堵措施</dt>
                <dd class="mt-1 text-sm">{{ detailCapa.containmentAction?.trim() || '无' }}</dd>
              </div>
              <div class="rounded-lg border bg-card p-3 sm:col-span-2">
                <dt class="text-xs text-muted-foreground">根本原因</dt>
                <dd class="mt-1 text-sm">{{ detailCapa.rootCause?.trim() || '待分析' }}</dd>
              </div>
            </dl>

            <section class="grid grid-cols-1 gap-2 [&>*]:min-w-0">
              <h3 class="text-sm font-semibold text-foreground">8D 措施明细</h3>
              <NvDataTable
                :columns="actionColumns"
                :rows="detailActions"
                :row-key="actionRowKey"
                :loading="capaDetailPending"
                :searchable="false"
                :column-settings="false"
                :pagination="false"
                empty-message="该纠正措施单还没有登记措施项。"
              >
                <template #cell-status="{ row }">
                  <NvStatusBadge
                    :value="row.status"
                    :label="row.status === 'completed' ? '已完成' : '进行中'"
                    :tone="
                      row.status === 'completed' ? 'success' : row.overdue ? 'danger' : 'warning'
                    "
                  />
                </template>
              </NvDataTable>
            </section>

            <section class="grid grid-cols-1 gap-2 [&>*]:min-w-0">
              <h3 class="text-sm font-semibold text-foreground">效果验证与关单</h3>
              <dl class="grid gap-3 sm:grid-cols-2">
                <div class="rounded-lg border bg-card p-3 sm:col-span-2">
                  <dt class="text-xs text-muted-foreground">效果验证结论</dt>
                  <dd class="mt-1 text-sm">
                    {{ detailCapa.effectivenessResult?.trim() || '尚未给出验证结论' }}
                  </dd>
                </div>
                <div class="rounded-lg border bg-card p-3">
                  <dt class="text-xs text-muted-foreground">验证人</dt>
                  <dd class="mt-1 text-sm">
                    {{ detailCapa.effectivenessVerifiedByUserId?.trim() || '无' }}
                  </dd>
                </div>
                <div class="rounded-lg border bg-card p-3">
                  <dt class="text-xs text-muted-foreground">验证时间</dt>
                  <dd class="mt-1 text-sm">
                    {{ formatDateTime(detailCapa.effectivenessVerifiedAtUtc) }}
                  </dd>
                </div>
                <div class="rounded-lg border bg-card p-3">
                  <dt class="text-xs text-muted-foreground">关单人</dt>
                  <dd class="mt-1 text-sm">{{ detailCapa.closedByUserId?.trim() || '无' }}</dd>
                </div>
                <div class="rounded-lg border bg-card p-3">
                  <dt class="text-xs text-muted-foreground">关单时间</dt>
                  <dd class="mt-1 text-sm">{{ formatDateTime(detailCapa.closedAtUtc) }}</dd>
                </div>
              </dl>
            </section>
          </template>
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
