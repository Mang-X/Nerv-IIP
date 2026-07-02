<script setup lang="ts">
import type { BusinessConsoleMesProductionPlanRow, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableProColumn, DataTableSort, StatusTone } from '@nerv-iip/ui'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { describeMesReadinessReason, useMesProductionPlans } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  ButtonPro,
  DataTablePro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  FieldPro,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import { ArrowRightIcon, FactoryIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '生产计划', requiredPermissions: ['business.mes.plans.read'] } })

const {
  convertPlanToWorkOrder,
  convertPlanToWorkOrderPending,
  filters,
  productionPlans,
  productionPlansError,
  productionPlansPending,
  productionPlansTotal,
  refreshProductionPlans,
} = useMesProductionPlans()
const route = useRoute()
const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')

const keyword = ref('')
const sourceFilter = ref(normalizeSourceQuery(route.query.source))
const readinessFilter = ref('all')
const sort = ref<DataTableSort | null>(null)
const { page, pageSize } = usePagedList(filters, { resetOn: [keyword, sourceFilter, readinessFilter] })

const convertOpen = shallowRef(false)
const selectedPlan = shallowRef<BusinessConsoleMesProductionPlanRow>()
const convertForm = reactive({
  workCenterId: '',
  dueUtc: '',
  idempotencyKey: newPlanIdempotencyKey('convert-plan'),
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

const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
watchDebounced(keyword, (value) => {
  filters.keyword = value.trim() || undefined
}, { debounce: 300, maxWait: 1000 })
watch(sourceFilter, (value) => {
  filters.source = value === 'all' ? undefined : value
}, { immediate: true })
watch(readinessFilter, (value) => {
  filters.readinessStatus = value === 'all' ? undefined : value
}, { immediate: true })
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

const selectedBlockingReasons = computed(() => (selectedPlan.value?.blockingReasons ?? []).map(describeMesReadinessReason))
const selectedPlanBlocked = computed(
  () => selectedPlan.value?.readinessStatus === 'Blocked' || selectedBlockingReasons.value.length > 0,
)
const canConvert = computed(() => Boolean(selectedPlan.value?.productionPlanId) && !selectedPlanBlocked.value)
const errorMessage = computed(() => formatError(productionPlansError.value))
const hasActiveFilters = computed(
  () => Boolean(keyword.value.trim()) || sourceFilter.value !== 'all' || readinessFilter.value !== 'all',
)
const emptyMessage = computed(() =>
  hasActiveFilters.value
    ? '没有符合当前筛选的计划。可点上方「重置」清空筛选。'
    : '还没有可执行的生产计划。需求与计划（MRP/MPS）下达后，计划会自动出现在这里。',
)

const columns: DataTableProColumn<BusinessConsoleMesProductionPlanRow>[] = [
  { key: 'productionPlanId', header: '计划号', cellClass: 'font-medium' },
  { key: 'sourceSystem', header: '来源计划' },
  { key: 'skuId', header: '物料' },
  { key: 'plannedQuantity', header: '数量', align: 'end', width: 'w-24', accessor: (r) => r.plannedQuantity ?? 0 },
  { key: 'plannedStartUtc', header: '计划开始', width: 'w-44', accessor: (r) => (r.plannedStartUtc ? new Date(r.plannedStartUtc).getTime() : 0) },
  { key: 'readinessStatus', header: '就绪状态', width: 'w-28' },
  { key: 'actions', header: '转工单', align: 'end', width: 'w-40' },
]

function openConvert(plan: BusinessConsoleMesProductionPlanRow) {
  selectedPlan.value = plan
  convertForm.workCenterId = ''
  convertForm.dueUtc = toLocalDateTimeInput(plan.plannedEndUtc ?? plan.plannedStartUtc)
  convertForm.idempotencyKey = newPlanIdempotencyKey(`convert-${plan.productionPlanId ?? 'plan'}`)
  convertOpen.value = true
}
async function submitConvertPlan() {
  const planId = selectedPlan.value?.productionPlanId
  if (!planId || !canConvert.value) return
  try {
    await convertPlanToWorkOrder(planId, {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      workCenterId: optionalText(convertForm.workCenterId),
      dueUtc: convertForm.dueUtc ? toIsoFromLocalInput(convertForm.dueUtc) : undefined,
      idempotencyKey: convertForm.idempotencyKey,
    })
    notifySuccess('已下达工单：该计划已转为工单，进入工单与派工。')
    convertForm.idempotencyKey = newPlanIdempotencyKey(`convert-${planId}`)
    convertOpen.value = false
    refreshProductionPlans()
  }
  catch (error) {
    notifyError(error)
  }
}
function resetFilters() {
  keyword.value = ''
  sourceFilter.value = 'all'
  readinessFilter.value = 'all'
}

function planReadiness(status?: string | null): { label: string, tone: StatusTone } {
  if (status === 'Ready') return { label: '可转工单', tone: 'success' }
  if (status === 'Warning') return { label: '有预警', tone: 'warning' }
  if (status === 'Blocked') return { label: '受阻', tone: 'danger' }
  return { label: status || '未知', tone: 'neutral' }
}
// 行是否就绪可转：受阻或带阻塞原因的计划先处理后才能转。
function planConvertible(plan: BusinessConsoleMesProductionPlanRow) {
  return Boolean(plan.productionPlanId)
    && plan.readinessStatus !== 'Blocked'
    && (plan.blockingReasons?.length ?? 0) === 0
}
// 受阻行的一句话原因（取首条），用于禁用入口的说明。
function planBlockHint(plan: BusinessConsoleMesProductionPlanRow) {
  const first = plan.blockingReasons?.[0]
  if (first) return describeMesReadinessReason(first).label
  if (plan.readinessStatus === 'Warning') return '有预警，建议处理后再转'
  return '尚未就绪，需处理后再转'
}
function sortValue(plan: BusinessConsoleMesProductionPlanRow, key: string) {
  if (key === 'plannedQuantity') return plan.plannedQuantity ?? 0
  if (key === 'plannedStartUtc') return plan.plannedStartUtc ? new Date(plan.plannedStartUtc).getTime() : 0
  return (plan[key as keyof BusinessConsoleMesProductionPlanRow] as string | null) ?? ''
}
function toResourceOptions(items: BusinessConsoleResourceItem[]) {
  return items.filter((i) => i.active !== false && i.code).map((i) => ({ label: i.displayName ? `${i.displayName} (${i.code})` : i.code!, value: i.code! }))
}
function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed || undefined
}
function toIsoFromLocalInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toISOString()
}
function toLocalDateTimeInput(value?: string | null) {
  const date = value ? new Date(value) : new Date(Date.now() + 86_400_000)
  if (Number.isNaN(date.getTime())) return ''
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}
function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatPlanSource(value?: string | null) {
  const map: Record<string, string> = {
    forecast: '预测需求',
    sales: '正常订单',
    'sales-order': '正常订单',
    safety: '安全库存补充',
    'safety-stock': '安全库存补充',
    stock: '备货生产',
    'stock-build': '备货生产',
  }
  return value ? (map[value] ?? value) : '未指定'
}
function normalizeSourceQuery(value: unknown): string {
  const text = Array.isArray(value) ? value[0] : value
  const allowed = ['sales', 'stock', 'safety', 'forecast']
  return typeof text === 'string' && allowed.includes(text) ? text : 'all'
}
function newPlanIdempotencyKey(scope: string) {
  return `${scope}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="生产计划" :breadcrumbs="[{ label: '制造执行' }]" :count="`${productionPlansTotal} 个计划`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="productionPlansPending" @click="refreshProductionPlans">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <Toolbar v-model:search="keyword" search-placeholder="搜索计划号、来源、物料">
      <template #filters>
        <SelectPro v-model="sourceFilter">
          <SelectProTrigger class="h-9 w-36" aria-label="来源"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="o in sourceOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
        <SelectPro v-model="readinessFilter">
          <SelectProTrigger class="h-9 w-36" aria-label="就绪状态"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="o in readinessOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
      <template #actions>
        <ButtonPro type="button" variant="ghost" size="sm" @click="resetFilters">重置</ButtonPro>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
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
      :searchable="false"
      :column-settings="false"
      :empty-message="emptyMessage"
    >
      <template #cell-sourceSystem="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ formatPlanSource(row.sourceSystem) }}</span>
          <span v-if="row.sourceDocumentId" class="text-xs text-muted-foreground">{{ row.sourceDocumentId }}</span>
        </div>
      </template>
      <template #cell-skuId="{ row }">
        <span v-if="row.skuId">{{ row.skuId }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-plannedQuantity="{ row }">
        <span class="tabular-nums">{{ formatQuantity(row.plannedQuantity) }}</span>
        <span v-if="row.uomCode" class="ml-1 text-xs text-muted-foreground">{{ row.uomCode }}</span>
      </template>
      <template #cell-plannedStartUtc="{ row }">{{ formatDateTime(row.plannedStartUtc) }}</template>
      <template #cell-readinessStatus="{ row }">
        <StatusBadgePro :label="planReadiness(row.readinessStatus).label" :tone="planReadiness(row.readinessStatus).tone" />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <ButtonPro
            v-if="planConvertible(row)"
            size="sm"
            type="button"
            @click="openConvert(row)"
          >
            <FactoryIcon aria-hidden="true" />
            转工单
          </ButtonPro>
          <ButtonPro
            v-else
            size="sm"
            type="button"
            variant="outline"
            disabled
            :title="planBlockHint(row)"
            :aria-label="`暂不可转：${planBlockHint(row)}`"
          >
            {{ planBlockHint(row) }}
          </ButtonPro>
        </div>
      </template>
    </DataTablePro>


    <DialogPro v-model:open="convertOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>下达工单</DialogProTitle>
          <DialogProDescription>
            把计划 {{ selectedPlan?.productionPlanId ?? '' }}（来源：{{ formatPlanSource(selectedPlan?.sourceSystem) }}）下达为工单。下达后进入「工单与派工」安排生产。工作中心与交期可留空，按工艺路线默认。
          </DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitConvertPlan">
          <div v-if="selectedBlockingReasons.length" class="grid gap-1 rounded-md border border-warning/30 bg-warning/10 p-3 text-sm">
            <span class="font-medium text-warning">转工单前需处理：</span>
            <span v-for="(reason, i) in selectedBlockingReasons" :key="i" class="text-muted-foreground">· {{ reason.label }}（{{ reason.nextStep }}）</span>
          </div>
          <FieldProGroup class="grid gap-3 sm:grid-cols-2">
            <FieldPro>
              <FieldProLabel for="convert-wc">工作中心</FieldProLabel>
              <SelectPro v-model="convertForm.workCenterId">
                <SelectProTrigger id="convert-wc"><SelectProValue placeholder="按工艺路线默认" /></SelectProTrigger>
                <SelectProContent>
                  <SelectProItem v-for="o in workCenterOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                </SelectProContent>
              </SelectPro>
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="convert-due">交期</FieldProLabel>
              <InputPro id="convert-due" v-model="convertForm.dueUtc" type="datetime-local" />
            </FieldPro>
          </FieldProGroup>
          <DialogProFooter>
            <ButtonPro type="button" variant="outline" @click="convertOpen = false">取消</ButtonPro>
            <ButtonPro type="submit" :disabled="convertPlanToWorkOrderPending || !canConvert">
              <Spinner v-if="convertPlanToWorkOrderPending" aria-hidden="true" />
              <ArrowRightIcon v-else aria-hidden="true" />
              确认下达工单
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
