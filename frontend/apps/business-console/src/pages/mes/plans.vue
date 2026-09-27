<script setup lang="ts">
import type { BusinessConsoleMesProductionPlanRow } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvDataTableSort } from '@nerv-iip/ui'
import { useMesProductionPlans } from '@/composables/useBusinessMes'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { inlineErrorMessage } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({
  meta: { requiresAuth: true, title: '生产计划', requiredPermissions: ['business.mes.plans.read'] },
})

const {
  filters,
  productionPlans,
  productionPlansError,
  productionPlansPending,
  productionPlansTotal,
  refreshProductionPlans,
} = useMesProductionPlans()
const route = useRoute()
const { resolveSkuLabel } = useMesDisplayNames()

const keyword = ref('')
const sourceFilter = ref(normalizeSourceQuery(route.query.source))
const readinessFilter = ref('all')
const sort = ref<NvDataTableSort | null>(null)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [keyword, sourceFilter, readinessFilter],
})

const sourceOptions = [
  { label: '全部来源', value: 'all' },
  { label: '正常订单', value: 'sales' },
  { label: '备货生产', value: 'stock' },
  { label: '安全库存补充', value: 'safety' },
  { label: '预测需求', value: 'forecast' },
]
const readinessOptions = [
  { label: '全部就绪状态', value: 'all' },
  { label: '可转工单', value: 'Ready' },
  { label: '有预警', value: 'Warning' },
  { label: '受阻', value: 'Blocked' },
]

watchDebounced(
  keyword,
  (value) => {
    filters.keyword = value.trim() || undefined
  },
  { debounce: 300, maxWait: 1000 },
)
watch(
  sourceFilter,
  (value) => {
    filters.source = value === 'all' ? undefined : value
  },
  { immediate: true },
)
watch(
  readinessFilter,
  (value) => {
    filters.readinessStatus = value === 'all' ? undefined : value
  },
  { immediate: true },
)
const visiblePlans = computed(() => productionPlans.value)

const sortedPlans = computed(() => {
  if (!sort.value) return visiblePlans.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...visiblePlans.value].sort((a, b) => {
    const av = sortValue(a, key)
    const bv = sortValue(b, key)
    if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * factor
    return String(av).localeCompare(String(bv), 'zh-Hans-CN') * factor
  })
})
const pagedPlans = computed(() => sortedPlans.value)

const errorMessage = computed(() => inlineErrorMessage(productionPlansError.value))
const hasActiveFilters = computed(
  () =>
    Boolean(keyword.value.trim()) ||
    sourceFilter.value !== 'all' ||
    readinessFilter.value !== 'all',
)
const emptyMessage = computed(() =>
  hasActiveFilters.value
    ? '没有符合当前筛选的计划。可点上方「重置」清空筛选。'
    : '还没有可执行的生产计划。需求与计划（MRP/MPS）下达后，计划会自动出现在这里。',
)

const columns: NvDataTableColumn<BusinessConsoleMesProductionPlanRow>[] = [
  { key: 'sourceDemandReference', header: '来源需求号', cellClass: 'font-medium' },
  { key: 'sourceSystem', header: '来源' },
  { key: 'skuId', header: '物料' },
  {
    key: 'plannedQuantity',
    header: '数量',
    align: 'end',
    width: 'w-24',
    accessor: (r) => r.plannedQuantity ?? 0,
  },
  {
    key: 'plannedStartUtc',
    header: '计划开始',
    width: 'w-44',
    accessor: (r) => (r.plannedStartUtc ? new Date(r.plannedStartUtc).getTime() : 0),
  },
  { key: 'readinessStatus', header: '就绪状态', width: 'w-28' },
]

function resetFilters() {
  keyword.value = ''
  sourceFilter.value = 'all'
  readinessFilter.value = 'all'
}

function isPlanCompleted(plan: BusinessConsoleMesProductionPlanRow) {
  return plan.status === 'completed'
}
function planRowReadiness(plan: BusinessConsoleMesProductionPlanRow) {
  return isPlanCompleted(plan)
    ? { label: '已完工', tone: 'neutral' as const }
    : { label: '已转工单', tone: 'neutral' as const }
}
function sortValue(plan: BusinessConsoleMesProductionPlanRow, key: string) {
  if (key === 'plannedQuantity') return plan.plannedQuantity ?? 0
  if (key === 'plannedStartUtc')
    return plan.plannedStartUtc ? new Date(plan.plannedStartUtc).getTime() : 0
  return (plan[key as keyof BusinessConsoleMesProductionPlanRow] as string | null) ?? ''
}
function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}
// 来源计划词表。注意后端同一列既放「需求性质」（sales / forecast / safety / stock），
// 也放「来源系统」（erp / mrp / aps / manual）——两类都要收，缺词就把英文码印到列上。
const PLAN_SOURCE_LABELS: Record<string, string> = {
  forecast: '预测需求',
  sales: '正常订单',
  'sales-order': '正常订单',
  safety: '安全库存补充',
  'safety-stock': '安全库存补充',
  stock: '备货生产',
  'stock-build': '备货生产',
  erp: 'ERP 下达',
  mrp: 'MRP 运算',
  aps: 'APS 排程',
  mes: 'MES 自建',
  manual: '手工新建',
}
function formatPlanSource(value?: string | null) {
  const raw = (value ?? '').trim()
  if (!raw) return '未指定'
  const label = PLAN_SOURCE_LABELS[raw.toLowerCase()]
  if (label === undefined && import.meta.env.DEV) {
    console.warn(`[生产计划] 词表缺失: ${raw}，请补 PLAN_SOURCE_LABELS`)
  }
  return label ?? raw
}
function normalizeSourceQuery(value: unknown): string {
  const text = Array.isArray(value) ? value[0] : value
  const allowed = ['sales', 'stock', 'safety', 'forecast']
  return typeof text === 'string' && allowed.includes(text) ? text : 'all'
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="生产计划"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${productionPlansTotal} 个计划`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="productionPlansPending"
          @click="refreshProductionPlans"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="搜索需求号、来源、物料">
      <template #filters>
        <NvSelect v-model="sourceFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="来源"><NvSelectValue /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="o in sourceOptions" :key="o.value" :value="o.value">{{
              o.label
            }}</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="readinessFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="就绪状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="o in readinessOptions" :key="o.value" :value="o.value">{{
              o.label
            }}</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
      <template #actions>
        <NvButton type="button" variant="ghost" size="sm" @click="resetFilters">重置</NvButton>
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="productionPlansTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedPlans"
      row-key="productionPlanId"
      :client-sort="false"
      :loading="productionPlansPending"
      :error="productionPlansError"
      :error-message="errorMessage"
      :searchable="false"
      :column-settings="false"
      :empty-message="emptyMessage"
      @retry="refreshProductionPlans"
    >
      <template #cell-sourceDemandReference="{ row }">
        <span v-if="row.sourceDemandReference">{{ row.sourceDemandReference }}</span>
        <span v-else class="text-muted-foreground">暂无需求号</span>
      </template>
      <template #cell-sourceSystem="{ row }">
        <span>{{ formatPlanSource(row.sourceSystem) }}</span>
      </template>
      <template #cell-skuId="{ row }">
        <span v-if="row.skuId && resolveSkuLabel(row.skuId) !== '未指定物料'">{{
          resolveSkuLabel(row.skuId)
        }}</span>
        <span v-else class="text-muted-foreground">未指定物料</span>
      </template>
      <template #cell-plannedQuantity="{ row }">
        <span class="tabular-nums">{{ formatQuantity(row.plannedQuantity) }}</span>
        <span v-if="row.uomCode" class="ml-1 text-xs text-muted-foreground">{{ row.uomCode }}</span>
      </template>
      <template #cell-plannedStartUtc="{ row }">{{ formatDateTime(row.plannedStartUtc) }}</template>
      <template #cell-readinessStatus="{ row }">
        <NvStatusBadge :label="planRowReadiness(row).label" :tone="planRowReadiness(row).tone" />
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
