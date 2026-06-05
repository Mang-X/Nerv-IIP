<script setup lang="ts">
import type { BusinessConsoleMesProductionPlanRow, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableColumn, DataTableSort, StatusTone } from '@nerv-iip/ui'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { describeMesReadinessReason, useMesProductionPlans } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DropdownMenuItem,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import { FactoryIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '生产计划' } })

const {
  convertPlanToWorkOrder,
  convertPlanToWorkOrderError,
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
const convertSuccess = shallowRef('')
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
const readyCount = computed(() => visiblePlans.value.filter((x) => x.readinessStatus === 'Ready').length)
const blockedCount = computed(() => visiblePlans.value.filter((x) => x.readinessStatus !== 'Ready').length)

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
const convertErrorMessage = computed(() => formatError(convertPlanToWorkOrderError.value))

const columns: DataTableColumn<BusinessConsoleMesProductionPlanRow>[] = [
  { key: 'productionPlanId', header: '计划号', cellClass: 'font-medium' },
  { key: 'sourceSystem', header: '来源计划' },
  { key: 'skuId', header: 'SKU' },
  { key: 'plannedQuantity', header: '数量', align: 'end', width: 'w-24', accessor: (r) => r.plannedQuantity ?? 0 },
  { key: 'plannedStartUtc', header: '计划开始', width: 'w-44', accessor: (r) => (r.plannedStartUtc ? new Date(r.plannedStartUtc).getTime() : 0) },
  { key: 'readinessStatus', header: '就绪状态', width: 'w-28' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function openConvert(plan: BusinessConsoleMesProductionPlanRow) {
  selectedPlan.value = plan
  convertSuccess.value = ''
  convertForm.workCenterId = ''
  convertForm.dueUtc = toLocalDateTimeInput(plan.plannedEndUtc ?? plan.plannedStartUtc)
  convertForm.idempotencyKey = newPlanIdempotencyKey(`convert-${plan.productionPlanId ?? 'plan'}`)
  convertOpen.value = true
}
async function submitConvertPlan() {
  const planId = selectedPlan.value?.productionPlanId
  if (!planId || !canConvert.value) return
  await convertPlanToWorkOrder(planId, {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    workCenterId: optionalText(convertForm.workCenterId),
    dueUtc: convertForm.dueUtc ? toIsoFromLocalInput(convertForm.dueUtc) : undefined,
    idempotencyKey: convertForm.idempotencyKey,
  })
  convertSuccess.value = `计划 ${planId} 已提交转工单。`
  convertForm.idempotencyKey = newPlanIdempotencyKey(`convert-${planId}`)
  convertOpen.value = false
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
        <Button size="sm" type="button" variant="outline" :disabled="productionPlansPending" @click="refreshProductionPlans">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="本页可转工单" :value="readyCount" hint="人员/设备/物料就绪" />
      <SectionCard description="本页受阻或预警" :value="blockedCount" hint="需处理后再释放" />
      <SectionCard description="计划总数" :value="productionPlansTotal" hint="后端分页总数" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索计划号、来源、SKU">
      <template #filters>
        <Select v-model="sourceFilter">
          <SelectTrigger class="h-9 w-36" aria-label="来源"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="o in sourceOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
          </SelectContent>
        </Select>
        <Select v-model="readinessFilter">
          <SelectTrigger class="h-9 w-36" aria-label="就绪状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="o in readinessOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
      <template #actions>
        <Button type="button" variant="ghost" size="sm" @click="resetFilters">重置</Button>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedPlans"
      row-key="productionPlanId"
      :client-sort="false"
      :loading="productionPlansPending"
      empty-message="当前没有生产计划。来源计划由需求与计划（MRP/MPS）下达后出现在这里。"
    >
      <template #cell-sourceSystem="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ formatPlanSource(row.sourceSystem) }}</span>
          <span v-if="row.sourceDocumentId" class="text-xs text-muted-foreground">{{ row.sourceDocumentId }}</span>
        </div>
      </template>
      <template #cell-plannedQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.plannedQuantity) }}</span></template>
      <template #cell-plannedStartUtc="{ row }">{{ formatDateTime(row.plannedStartUtc) }}</template>
      <template #cell-readinessStatus="{ row }">
        <StatusBadge :label="planReadiness(row.readinessStatus).label" :tone="planReadiness(row.readinessStatus).tone" />
      </template>
      <template #cell-actions="{ row }">
        <RowActions :label="`生产计划操作 ${row.productionPlanId ?? ''}`">
          <DropdownMenuItem :disabled="!row.productionPlanId" @click="openConvert(row)">
            <FactoryIcon aria-hidden="true" />
            转工单
          </DropdownMenuItem>
        </RowActions>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="productionPlansTotal" />

    <Dialog v-model:open="convertOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>计划转工单</DialogTitle>
          <DialogDescription>
            来自来源计划 {{ selectedPlan?.productionPlanId ?? '' }}（{{ formatPlanSource(selectedPlan?.sourceSystem) }}）。释放后进入工单与派工。
          </DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitConvertPlan">
          <p v-if="convertErrorMessage" class="text-sm text-destructive" role="alert">{{ convertErrorMessage }}</p>
          <div v-if="selectedBlockingReasons.length" class="grid gap-1 rounded-md border border-warning/30 bg-warning/10 p-3 text-sm">
            <span class="font-medium text-warning">转工单前需处理：</span>
            <span v-for="(reason, i) in selectedBlockingReasons" :key="i" class="text-muted-foreground">· {{ reason.label }}（{{ reason.nextStep }}）</span>
          </div>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="convert-wc">工作中心</FieldLabel>
              <Select v-model="convertForm.workCenterId">
                <SelectTrigger id="convert-wc"><SelectValue placeholder="按工艺路线默认" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="o in workCenterOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="convert-due">交期</FieldLabel>
              <Input id="convert-due" v-model="convertForm.dueUtc" type="datetime-local" />
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" @click="convertOpen = false">取消</Button>
            <Button type="submit" :disabled="convertPlanToWorkOrderPending || !canConvert">
              <Spinner v-if="convertPlanToWorkOrderPending" aria-hidden="true" />
              转工单
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
