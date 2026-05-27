<script setup lang="ts">
import BusinessActionSheet from '@/components/business/BusinessActionSheet.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { useMesProductionPlans } from '@/composables/useBusinessMes'
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
import { useRouter } from 'vue-router'

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
const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')
const convertSheetOpen = shallowRef(false)
const selectedPlan = shallowRef<BusinessConsoleMesProductionPlanRow>()
const convertSuccess = shallowRef('')
const tableState = reactive({
  keyword: '',
  page: 1,
  pageSize: '10',
  source: 'all',
  readiness: 'all',
  sortBy: 'plannedStartUtc' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
const convertForm = reactive({
  workOrderId: '',
  workCenterId: '',
  dueUtc: '',
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
const sourceCards = [
  { title: '正常订单', description: '销售订单或客户订单确认后进入计划池。', source: 'sales' },
  { title: '备货生产', description: '按预测和主生产计划提前安排生产。', source: 'stock' },
  { title: '安全库存补充', description: '库存水位触发补货建议，计划员确认后转工单。', source: 'safety' },
  { title: '预测需求', description: '来自需求计划或预测模型的中长期生产建议。', source: 'forecast' },
]

const errorMessage = computed(() => formatError(productionPlansError.value))
const convertErrorMessage = computed(() => formatError(convertPlanToWorkOrderError.value))
const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
const visiblePlans = computed(() => {
  const keyword = tableState.keyword.trim().toLowerCase()

  return productionPlans.value.filter((plan) => {
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
    const sourceMatched = tableState.source === 'all' || sourceText.includes(tableState.source)
    const readinessMatched = tableState.readiness === 'all' || plan.readinessStatus === tableState.readiness

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
const totalPages = computed(() => Math.max(1, Math.ceil(sortedPlans.value.length / pageSizeNumber.value)))
const pagedPlans = computed(() => {
  const start = (tableState.page - 1) * pageSizeNumber.value
  return sortedPlans.value.slice(start, start + pageSizeNumber.value)
})
const paginationSummary = computed(() => {
  if (!sortedPlans.value.length) return '0 条'
  const start = (tableState.page - 1) * pageSizeNumber.value + 1
  const end = Math.min(tableState.page * pageSizeNumber.value, sortedPlans.value.length)
  return `${start}-${end} / ${sortedPlans.value.length} 条`
})
const canConvert = computed(() => Boolean(selectedPlan.value?.productionPlanId))

watch(
  () => [
    tableState.keyword,
    tableState.source,
    tableState.readiness,
    tableState.pageSize,
    productionPlans.value.length,
  ],
  () => {
    tableState.page = 1
  },
)

function focusSource(source: string) {
  tableState.source = source
}

function openConvertSheet(plan: BusinessConsoleMesProductionPlanRow) {
  selectedPlan.value = plan
  convertSuccess.value = ''
  convertForm.workOrderId = plan.productionPlanId ? `WO-${plan.productionPlanId}` : ''
  convertForm.workCenterId = ''
  convertForm.dueUtc = toLocalDateTimeInput(plan.plannedEndUtc ?? plan.plannedStartUtc)
  convertSheetOpen.value = true
}

async function submitConvertPlan() {
  const planId = selectedPlan.value?.productionPlanId
  if (!planId || !canConvert.value) return

  await convertPlanToWorkOrder(planId, {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    workOrderId: optionalText(convertForm.workOrderId),
    workCenterId: optionalText(convertForm.workCenterId),
    dueUtc: convertForm.dueUtc ? toIsoFromLocalInput(convertForm.dueUtc) : undefined,
    idempotencyKey: `convert-${planId}-${Date.now()}`,
  })
  const workOrderId = convertForm.workOrderId
  convertSuccess.value = workOrderId ? `已生成工单 ${workOrderId}。` : '已提交转工单。'
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

function previousPage() {
  tableState.page = Math.max(1, tableState.page - 1)
}

function nextPage() {
  tableState.page = Math.min(totalPages.value, tableState.page + 1)
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

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}

function rowKey(row: BusinessConsoleMesProductionPlanRow, index: number) {
  return `${row.productionPlanId ?? 'plan'}:${index}`
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
        domain="MES"
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

      <div class="grid gap-3 lg:grid-cols-4">
        <button
          v-for="card in sourceCards"
          :key="card.title"
          class="grid gap-2 rounded-lg border bg-background p-4 text-left transition-colors hover:border-primary/50 hover:bg-muted/40"
          type="button"
          @click="focusSource(card.source)"
        >
          <span class="text-sm font-semibold text-foreground">{{ card.title }}</span>
          <span class="min-h-12 text-sm leading-6 text-muted-foreground">{{ card.description }}</span>
          <span class="text-sm font-medium text-primary">筛选这类计划</span>
        </button>
      </div>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 class="text-sm font-semibold text-foreground">计划筛选</h2>
          <Button size="sm" type="button" variant="ghost" @click="tableState.keyword = ''; tableState.source = 'all'; tableState.readiness = 'all'">
            清空筛选
          </Button>
        </div>
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="plans-keyword">搜索</FieldLabel>
            <Input id="plans-keyword" v-model="tableState.keyword" placeholder="计划号、来源单据、SKU" />
          </Field>
          <Field>
            <FieldLabel for="plans-source">需求来源</FieldLabel>
            <Select v-model="tableState.source">
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
            <Select v-model="tableState.readiness">
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
          <Field>
            <FieldLabel for="plans-page-size">每页显示</FieldLabel>
            <Select v-model="tableState.pageSize">
              <SelectTrigger id="plans-page-size">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="10">10 条</SelectItem>
                <SelectItem value="20">20 条</SelectItem>
                <SelectItem value="50">50 条</SelectItem>
              </SelectContent>
            </Select>
          </Field>
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
          <span class="text-sm text-muted-foreground">显示 {{ paginationSummary }}</span>
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
                    <span>{{ row.sourceSystem ?? '未指定' }}</span>
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
                  action="没有计划时，应先从需求计划、销售订单或库存策略生成计划建议。"
                />
              </TableEmpty>
            </TableBody>
          </Table>
        </div>
        <div class="flex flex-wrap items-center justify-between gap-3 border-t px-4 py-3">
          <p class="text-sm text-muted-foreground">
            第 {{ tableState.page }} / {{ totalPages }} 页
          </p>
          <div class="flex items-center gap-2">
            <Button size="sm" type="button" variant="outline" :disabled="tableState.page <= 1" @click="previousPage">
              上一页
            </Button>
            <Button size="sm" type="button" variant="outline" :disabled="tableState.page >= totalPages" @click="nextPage">
              下一页
            </Button>
          </div>
        </div>
      </div>

      <BusinessActionSheet
        v-model:open="convertSheetOpen"
        title="生产计划转工单"
        description="确认工单号、工作中心和交期后，将计划下达到 MES 执行。"
      >
        <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitConvertPlan">
          <div>
            <p class="text-xs font-bold uppercase text-primary">转工单</p>
            <h2 class="text-base font-semibold text-foreground">{{ selectedPlan?.productionPlanId ?? '生产计划' }}</h2>
          </div>
          <BusinessFormStatus :error="convertErrorMessage" :success="convertSuccess" />
          <div class="grid gap-2 rounded-lg border p-3 text-sm text-muted-foreground">
            <p>来源：{{ selectedPlan?.sourceSystem ?? '未指定' }} / {{ selectedPlan?.sourceDocumentId ?? '无来源单据' }}</p>
            <p>物料：{{ selectedPlan?.skuId ?? '未指定' }}，数量：{{ formatQuantity(selectedPlan?.plannedQuantity) }}</p>
            <p v-if="selectedPlan?.blockingReasons?.length">阻塞：{{ selectedPlan.blockingReasons.join('；') }}</p>
          </div>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="convert-work-order">工单号</FieldLabel>
              <Input id="convert-work-order" v-model="convertForm.workOrderId" placeholder="可由系统生成" />
            </Field>
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
