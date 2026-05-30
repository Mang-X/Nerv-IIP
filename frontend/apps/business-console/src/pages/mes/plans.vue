<script setup lang="ts">
import BusinessActionSheet from '@/components/business/BusinessActionSheet.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import BusinessTablePagination from '@/components/business/BusinessTablePagination.vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { describeMesReadinessReason, useMesProductionPlans } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleMesProductionPlanRow,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import {
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import {
  ArrowDownIcon,
  ArrowUpDownIcon,
  ArrowUpIcon,
  CalendarCheckIcon,
  ClipboardListIcon,
  FactoryIcon,
  RefreshCwIcon,
} from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '生产计划' } })

type SortColumn = 'productionPlanId' | 'sourceSystem' | 'skuId' | 'plannedQuantity' | 'readinessStatus' | 'plannedStartUtc'

const {
  convertPlanToWorkOrder,
  convertPlanToWorkOrderError,
  convertPlanToWorkOrderPending,
  filters,
  productionPlans,
  productionPlansError,
  productionPlansPending,
  refreshProductionPlans,
} = useMesProductionPlans()
const router = useRouter()
const route = useRoute()
const initialSource = normalizeSourceQuery(route.query.source)
const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')
const convertSheetOpen = shallowRef(false)
const selectedPlan = shallowRef<BusinessConsoleMesProductionPlanRow>()
const convertSuccess = shallowRef('')
const selectedPlanBlockingReasons = computed(() =>
  (selectedPlan.value?.blockingReasons ?? []).map(describeMesReadinessReason),
)
const filterDraft = reactive({
  keyword: '',
  source: initialSource,
  readiness: 'all',
})
const appliedFilter = reactive({
  keyword: '',
  source: initialSource,
  readiness: 'all',
})
const tableState = reactive({
  page: 1,
  pageSize: '10',
  sortBy: 'plannedStartUtc' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
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
const sourceActions = [
  { title: '正常订单', description: '销售订单确认', source: 'sales', createSource: 'sales-order' },
  { title: '备货生产', description: '主生产计划', source: 'stock', createSource: 'stock-build' },
  { title: '安全库存', description: '库存水位补充', source: 'safety', createSource: 'safety-stock' },
  { title: '预测需求', description: '中长期需求', source: 'forecast', createSource: 'forecast' },
]

const errorMessage = computed(() => formatError(productionPlansError.value))
const convertErrorMessage = computed(() => formatError(convertPlanToWorkOrderError.value))
const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
const sourcePlans = computed(() => productionPlans.value)
const visiblePlans = computed(() => {
  const keyword = appliedFilter.keyword.trim().toLowerCase()

  return sourcePlans.value.filter((plan) => {
    const sourceText = `${plan.sourceSystem ?? ''} ${plan.sourceDocumentId ?? ''}`.toLowerCase()
    const keywordMatched =
      !keyword ||
      [
        plan.productionPlanId,
        plan.sourceSystem,
        plan.sourceDocumentId,
        plan.skuId,
        plan.readinessStatus,
      ].some((value) => (value ?? '').toLowerCase().includes(keyword))
    const sourceMatched = appliedFilter.source === 'all' || sourceText.includes(appliedFilter.source)
    const readinessMatched = appliedFilter.readiness === 'all' || plan.readinessStatus === appliedFilter.readiness

    return keywordMatched && sourceMatched && readinessMatched
  })
})
const readyCount = computed(() => visiblePlans.value.filter((x) => x.readinessStatus === 'Ready').length)
const blockedCount = computed(() => visiblePlans.value.filter((x) => x.readinessStatus !== 'Ready').length)
const sortedPlans = computed(() => {
  const direction = tableState.sortDirection === 'asc' ? 1 : -1

  return [...visiblePlans.value].sort((left, right) => {
    const leftValue = sortValue(left, tableState.sortBy)
    const rightValue = sortValue(right, tableState.sortBy)

    if (typeof leftValue === 'number' && typeof rightValue === 'number') {
      return (leftValue - rightValue) * direction
    }

    return String(leftValue).localeCompare(String(rightValue), 'zh-Hans-CN') * direction
  })
})
const pageSizeNumber = computed(() => Number(tableState.pageSize) || 10)
const pagedPlans = computed(() => {
  const start = (tableState.page - 1) * pageSizeNumber.value
  return sortedPlans.value.slice(start, start + pageSizeNumber.value)
})
const canConvert = computed(() => Boolean(selectedPlan.value?.productionPlanId))

watch(
  () => [
    appliedFilter.keyword,
    appliedFilter.source,
    appliedFilter.readiness,
    tableState.pageSize,
    sourcePlans.value.length,
  ],
  () => {
    tableState.page = 1
  },
)

function focusSource(source: string) {
  filterDraft.source = source
  applyFilters()
}

function applyFilters() {
  appliedFilter.keyword = filterDraft.keyword
  appliedFilter.source = filterDraft.source
  appliedFilter.readiness = filterDraft.readiness
}

function clearFilters() {
  filterDraft.keyword = ''
  filterDraft.source = 'all'
  filterDraft.readiness = 'all'
  applyFilters()
}

function openConvertSheet(plan: BusinessConsoleMesProductionPlanRow) {
  selectedPlan.value = plan
  convertSuccess.value = ''
  convertForm.workCenterId = ''
  convertForm.dueUtc = toLocalDateTimeInput(plan.plannedEndUtc ?? plan.plannedStartUtc)
  convertForm.idempotencyKey = newPlanIdempotencyKey(`convert-${plan.productionPlanId ?? 'plan'}`)
  convertSheetOpen.value = true
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
  convertForm.idempotencyKey = newPlanIdempotencyKey(`convert-${planId}`)
  convertSuccess.value = '已提交转工单。'
}

function setSort(column: SortColumn) {
  if (tableState.sortBy === column) {
    tableState.sortDirection = tableState.sortDirection === 'asc' ? 'desc' : 'asc'
    return
  }

  tableState.sortBy = column
  tableState.sortDirection = 'asc'
}

function sortIcon(column: SortColumn) {
  if (tableState.sortBy !== column) return ArrowUpDownIcon
  return tableState.sortDirection === 'asc' ? ArrowUpIcon : ArrowDownIcon
}

function sortValue(plan: BusinessConsoleMesProductionPlanRow, column: SortColumn) {
  if (column === 'plannedQuantity') return plan.plannedQuantity ?? 0
  if (column === 'plannedStartUtc') return plan.plannedStartUtc ? new Date(plan.plannedStartUtc).getTime() : 0
  return plan[column] ?? ''
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
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

function formatQuantity(value?: number) {
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

function newPlanIdempotencyKey(scope: string) {
  return `${scope}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}

function rowKey(row: BusinessConsoleMesProductionPlanRow, index: number) {
  return `${row.productionPlanId ?? 'plan'}:${index}`
}

function normalizeSourceQuery(value: unknown) {
  const source = Array.isArray(value) ? value[0] : value
  return ['sales', 'stock', 'safety', 'forecast'].includes(String(source)) ? String(source) : 'all'
}

function toResourceOptions(items: BusinessConsoleResourceItem[]) {
  return items
    .filter((item) => item.active !== false && item.code)
    .map((item) => ({
      label: item.displayName ? `${item.displayName} (${item.code})` : item.code!,
      value: item.code!,
    }))
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="生产执行"
        title="生产计划"
        kicker="计划员工作台"
        summary="正常订单、备货、安全库存和预测需求先在这里确认就绪，再转为车间可执行工单；急单不替代计划流程。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="productionPlansPending" @click="refreshProductionPlans">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
          <Button size="sm" type="button" @click="router.push('/mes/work-orders')">
            <FactoryIcon data-icon="inline-start" />
            工单与派工
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="flex flex-wrap items-center gap-2 rounded-lg border bg-background px-4 py-3">
        <span class="text-sm font-semibold text-foreground">计划来源</span>
        <Button
          v-for="action in sourceActions"
          :key="action.title"
          size="sm"
          type="button"
          variant="outline"
          @click="focusSource(action.source)"
        >
          {{ action.title }}
          <span class="text-xs text-muted-foreground">{{ action.description }}</span>
        </Button>
      </div>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 class="text-sm font-semibold text-foreground">计划筛选</h2>
        </div>
        <FieldGroup class="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px_220px_auto]">
          <Field>
            <FieldLabel for="plans-keyword">搜索</FieldLabel>
            <Input id="plans-keyword" v-model="filterDraft.keyword" placeholder="计划号、来源单据、物料" @keydown.enter="applyFilters" />
          </Field>
          <Field>
            <FieldLabel for="plans-source">需求来源</FieldLabel>
            <Select v-model="filterDraft.source">
              <SelectTrigger id="plans-source">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in sourceOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="plans-readiness">就绪状态</FieldLabel>
            <Select v-model="filterDraft.readiness">
              <SelectTrigger id="plans-readiness">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in readinessOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <div class="flex items-end gap-2">
            <Button type="button" @click="applyFilters">查询</Button>
            <Button type="button" variant="outline" @click="clearFilters">清空</Button>
          </div>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="计划数" :value="visiblePlans.length" detail="当前筛选结果" />
        <BusinessMetricCell label="可转工单" :value="readyCount" detail="就绪计划" />
        <BusinessMetricCell label="需处理" :value="blockedCount" detail="预警或阻塞" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">生产计划列表</h2>
          <span class="text-sm text-muted-foreground">订单 / 备货 / 安全库存 / 预测</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('productionPlanId')">
                    计划号
                    <component :is="sortIcon('productionPlanId')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('sourceSystem')">
                    来源
                    <component :is="sortIcon('sourceSystem')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('skuId')">
                    物料
                    <component :is="sortIcon('skuId')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead class="text-right">
                  <Button class="-mr-3" size="sm" type="button" variant="ghost" @click="setSort('plannedQuantity')">
                    数量
                    <component :is="sortIcon('plannedQuantity')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('readinessStatus')">
                    就绪
                    <component :is="sortIcon('readinessStatus')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('plannedStartUtc')">
                    计划开始
                    <component :is="sortIcon('plannedStartUtc')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead class="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(row, index) in pagedPlans" :key="rowKey(row, index)">
                <TableCell class="font-medium">{{ row.productionPlanId ?? '无编号' }}</TableCell>
                <TableCell>
                  <div class="grid gap-0.5">
                    <span>{{ formatPlanSource(row.sourceSystem) }}</span>
                    <span class="text-xs text-muted-foreground">{{ row.sourceDocumentId ?? '无来源单据' }}</span>
                  </div>
                </TableCell>
                <TableCell>{{ row.skuId ?? '未指定' }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.plannedQuantity) }}</TableCell>
                <TableCell>
                  <BusinessStatusBadge :value="row.readinessStatus" />
                </TableCell>
                <TableCell>{{ formatDateTime(row.plannedStartUtc) }}</TableCell>
                <TableCell class="text-right">
                  <Button size="sm" type="button" :disabled="row.readinessStatus !== 'Ready'" @click="openConvertSheet(row)">
                    <ClipboardListIcon data-icon="inline-start" />
                    转工单
                  </Button>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="productionPlansPending" :colspan="7">正在加载生产计划...</TableEmpty>
              <TableEmpty v-if="!visiblePlans.length && !productionPlansPending" :colspan="7">
                <BusinessEmptyState
                  title="当前筛选下没有生产计划"
                  description="可以切换需求来源、就绪状态或搜索条件。正常订单、备货和安全库存补充都会先进入计划池。"
                  action="没有计划时，应先到需求与物料计划查看已接受的生产建议。"
                />
              </TableEmpty>
            </TableBody>
          </Table>
        </div>
        <div class="border-t px-4 py-3">
          <BusinessTablePagination
            v-model:page="tableState.page"
            v-model:page-size="tableState.pageSize"
            :total-items="sortedPlans.length"
          />
        </div>
      </div>

      <BusinessActionSheet
        v-model:open="convertSheetOpen"
        title="生产计划转工单"
        description="确认工作中心和交期后，将计划下达到 MES 执行。"
      >
        <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitConvertPlan">
          <div>
            <p class="text-xs font-bold uppercase text-primary">转工单</p>
            <h2 class="text-base font-semibold text-foreground">{{ selectedPlan?.productionPlanId ?? '生产计划' }}</h2>
          </div>
          <BusinessFormStatus :error="convertErrorMessage" :success="convertSuccess" />
          <div class="grid gap-2 rounded-lg border p-3 text-sm text-muted-foreground">
            <p>来源：{{ formatPlanSource(selectedPlan?.sourceSystem) }} / {{ selectedPlan?.sourceDocumentId ?? '无来源单据' }}</p>
            <p>物料：{{ selectedPlan?.skuId ?? '未指定' }}，数量：{{ formatQuantity(selectedPlan?.plannedQuantity) }}</p>
            <div v-if="selectedPlanBlockingReasons.length" class="grid gap-2">
              <p class="font-medium text-foreground">阻塞处理</p>
              <div v-for="reason in selectedPlanBlockingReasons" :key="reason.code" class="grid gap-1 rounded-md border p-2">
                <div class="flex flex-wrap items-center gap-2">
                  <BusinessStatusBadge :value="reason.label" />
                  <span class="font-mono text-xs text-muted-foreground">{{ reason.code }}</span>
                </div>
                <p class="text-xs text-muted-foreground">{{ reason.nextStep }}</p>
              </div>
            </div>
          </div>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="convert-work-center">工作中心</FieldLabel>
              <Select v-if="workCenterOptions.length" v-model="convertForm.workCenterId">
                <SelectTrigger id="convert-work-center">
                  <SelectValue placeholder="选择工作中心" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="option in workCenterOptions" :key="option.value" :value="option.value">
                    {{ option.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <Input v-else id="convert-work-center" v-model="convertForm.workCenterId" placeholder="可选" />
            </Field>
            <Field>
              <FieldLabel for="convert-due">交期</FieldLabel>
              <Input id="convert-due" v-model="convertForm.dueUtc" type="datetime-local" />
            </Field>
          </FieldGroup>
          <div class="flex justify-end gap-2">
            <Button type="button" variant="outline" @click="convertSheetOpen = false">取消</Button>
            <Button type="submit" :disabled="convertPlanToWorkOrderPending || !canConvert">
              <Spinner v-if="convertPlanToWorkOrderPending" data-icon="inline-start" />
              <CalendarCheckIcon v-else data-icon="inline-start" />
              确认转工单
            </Button>
          </div>
        </form>
      </BusinessActionSheet>
    </section>
  </BusinessLayout>
</template>
