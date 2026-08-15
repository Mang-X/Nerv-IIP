<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import { mesQualityStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import { useMesRelatedQualityItems } from '@/composables/useBusinessMes'
import { useQualityReasonCodes } from '@/composables/usePromotedCatalogs'
import {
  labelFor,
  MES_QUALITY_ITEM_STATUS_LABELS,
  QUALITY_SOURCE_TYPE_LABELS,
} from '@/data/businessLabels'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { inlineErrorMessage } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '质量与不良',
    requiredPermissions: ['business.mes.quality.read'],
  },
})

const route = useRoute()
const router = useRouter()
const {
  filters,
  qualityItems,
  qualityItemsError,
  qualityItemsPending,
  qualityItemsTotal,
  refreshQualityItems,
} = useMesRelatedQualityItems()
const { keyword } = useMesKeywordFilter(filters)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})
// 缺陷代码的中文名在质量原因码目录里；目录查不到就只显代码，不编造缺陷名。
const { reasons: qualityReasons } = useQualityReasonCodes()
const reasonNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const reason of qualityReasons.value) {
    if (reason.reasonCode && reason.reasonName) map.set(reason.reasonCode, reason.reasonName)
  }
  return map
})
function defectLabel(code?: string | null) {
  if (!code) return '无'
  return reasonNameByCode.value.get(code) ?? code
}

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const errorMessage = computed(() => formatError(qualityItemsError.value))
// 上下文穿透：从工单/工序带入时显示来源并提供返回链接。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
const openCount = computed(
  () => qualityItems.value.filter((r) => (r.status ?? '').toLowerCase() !== 'closed').length,
)
const ncrCount = computed(() => qualityItems.value.filter((r) => r.ncrId).length)
// 质量项的决策点是「还有多少没关闭」——构成卡按处理状态拆分；已开 NCR 单独标注。
const qualitySegments = computed(() =>
  pagedBreakdownSegments(qualityItemsTotal.value, [
    { key: 'open', label: '未关闭', value: openCount.value, tone: 'danger' },
    {
      key: 'closed',
      label: '已关闭',
      value: qualityItems.value.length - openCount.value,
      tone: 'success',
    },
  ]),
)

type QualityRow = (typeof qualityItems)['value'][number]
const columns: NvDataTableColumn<QualityRow>[] = [
  {
    key: 'qualityItemId',
    header: '质量项',
    cellClass: 'font-medium',
    accessor: (r) => r.qualityItemId ?? '无',
  },
  {
    key: 'sourceType',
    header: '来源类型',
    accessor: (r) => labelFor(QUALITY_SOURCE_TYPE_LABELS, r.sourceType) || '未指定',
  },
  { key: 'sourceDocumentId', header: '来源单据', accessor: (r) => r.sourceDocumentId ?? '未指定' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'defectCode', header: '缺陷', accessor: (r) => defectLabel(r.defectCode) },
  { key: 'ncrId', header: 'NCR', accessor: (r) => r.ncrId ?? '无' },
]

function isWorkOrder(value?: string | null) {
  return !!value && /^WO/i.test(value)
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="质量与不良"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${qualityItemsTotal} 条质量项`"
    >
      <template #actions>
        <NvButton v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`"
            >返回工单 {{ contextWorkOrderId }}</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="qualityItemsPending"
          @click="refreshQualityItems"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <NvMetricCard
        variant="breakdown"
        label="质量项"
        :value="qualityItemsTotal"
        unit="条"
        :segments="qualitySegments"
      />
      <NvMetricCard
        variant="alert"
        label="已开不合格品单"
        :value="ncrCount"
        unit="张"
        :tone="ncrCount > 0 ? 'danger' : 'neutral'"
        :status="
          ncrCount > 0 ? { label: '待处置', tone: 'danger' } : { label: '无', tone: 'success' }
        "
        :foot-start="
          ncrCount > 0
            ? '不合格品单需给出返工、让步或报废结论后才能关闭对应质量项。'
            : '当前质量项都未升级为不合格品单。'
        "
        :action="ncrCount > 0 ? { label: '去质量处置' } : undefined"
        @action="router.push({ path: '/quality/ncrs' })"
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="质量项 / 来源单据 / 缺陷代码"
          aria-label="搜索质量项"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="质量状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesQualityStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="qualityItemsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="qualityItems"
      row-key="qualityItemId"
      :loading="qualityItemsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无质量或不良记录。先在工序执行登记检验结果或不良，再回到这里跟进处置与关闭。"
    >
      <template #cell-sourceDocumentId="{ row }">
        <RouterLink
          v-if="isWorkOrder(row.sourceDocumentId)"
          :to="`/mes/work-orders/${encodeURIComponent(row.sourceDocumentId!)}`"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.sourceDocumentId }}
        </RouterLink>
        <span v-else>{{ row.sourceDocumentId ?? '未指定' }}</span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge
          :value="row.status"
          :label="labelFor(MES_QUALITY_ITEM_STATUS_LABELS, row.status) || '未知'"
        />
      </template>
      <template #cell-ncrId="{ row }">
        <RouterLink
          v-if="row.ncrId"
          :to="{
            path: '/quality/ncrs',
            query: {
              ncrId: row.ncrId,
              workOrderId: isWorkOrder(row.sourceDocumentId) ? row.sourceDocumentId : undefined,
            },
          }"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.ncrId }}
        </RouterLink>
        <span v-else class="text-muted-foreground">无</span>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
