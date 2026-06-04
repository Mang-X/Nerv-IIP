<script setup lang="ts">
import type { BusinessConsoleMesCreateReceiptRequest } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesFinishedGoodsReceipts } from '@/composables/useBusinessMes'
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
import { EyeIcon, PackageCheckIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '完工入库' } })

const {
  createReceiptRequest,
  createReceiptRequestError,
  createReceiptRequestPending,
  filters,
  receiptRequests,
  receiptRequestsError,
  receiptRequestsPending,
  refreshReceiptRequests,
} = useMesFinishedGoodsReceipts()

const route = useRoute()
const router = useRouter()
const successMessage = shallowRef('')
const receiptSheetOpen = shallowRef(false)

const keyword = ref('')
const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => { filters.status = value === 'all' ? undefined : value },
})
const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '待处理', value: 'Pending' },
  { label: '已完成', value: 'Completed' },
  { label: '失败', value: 'Failed' },
]

const form = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  workOrderId: '',
  skuId: '',
  quantity: '1',
  uomCode: 'EA',
  requestedAtUtc: toLocalDateTimeInput(new Date()),
  idempotencyKey: '',
})

const filtered = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return receiptRequests.value
  return receiptRequests.value.filter((r) =>
    [r.receiptRequestId, r.workOrderId, r.skuId].some((v) => (v ?? '').toLowerCase().includes(kw)),
  )
})

const listErrorMessage = computed(() => formatError(receiptRequestsError.value))
const createErrorMessage = computed(() => formatError(createReceiptRequestError.value))
const pendingCount = computed(() => receiptRequests.value.filter((item) => item.receiptStatus !== 'Completed').length)
const hasReceiptContext = computed(() => isNonEmpty(form.workOrderId) && isNonEmpty(form.skuId))
const canCreate = computed(
  () =>
    isNonEmpty(form.organizationId) &&
    isNonEmpty(form.environmentId) &&
    isNonEmpty(form.workOrderId) &&
    isNonEmpty(form.skuId) &&
    toOptionalNumber(form.quantity) !== undefined &&
    isNonEmpty(form.uomCode) &&
    isNonEmpty(form.requestedAtUtc),
)

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => {
  const start = (page.value - 1) * pageSizeNumber.value
  return filtered.value.slice(start, start + pageSizeNumber.value)
})
watch([keyword, pageSize, () => receiptRequests.value.length], () => {
  page.value = 1
})

watch(
  () => route.query,
  (query) => {
    const workOrderId = firstQueryValue(query.workOrderId)
    const skuId = firstQueryValue(query.skuId)
    const quantity = firstQueryValue(query.quantity)
    if (workOrderId) form.workOrderId = workOrderId
    if (skuId) form.skuId = skuId
    if (quantity) form.quantity = quantity
    if (workOrderId && skuId) receiptSheetOpen.value = true
  },
  { immediate: true },
)

type ReceiptRow = (typeof receiptRequests)['value'][number]
const columns: DataTableColumn<ReceiptRow>[] = [
  { key: 'receiptRequestId', header: '请求号', cellClass: 'font-medium', accessor: (r) => r.receiptRequestId ?? '无' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderId ?? '无' },
  { key: 'skuId', header: '物料', accessor: (r) => r.skuId ?? '无' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-24' },
  { key: 'receiptStatus', header: '状态', width: 'w-24' },
  { key: 'requestedAtUtc', header: '请求时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function openWorkOrder(workOrderId?: string | null) {
  if (!workOrderId) return
  void router.push({ path: `/mes/work-orders/${encodeURIComponent(workOrderId)}` })
}
function openReceiptSheet() {
  if (!hasReceiptContext.value) return
  successMessage.value = ''
  receiptSheetOpen.value = true
}

async function submitReceiptRequest() {
  if (!canCreate.value) return
  const body: BusinessConsoleMesCreateReceiptRequest = {
    organizationId: form.organizationId.trim(),
    environmentId: form.environmentId.trim(),
    workOrderId: form.workOrderId.trim(),
    skuId: form.skuId.trim(),
    quantity: toOptionalNumber(form.quantity),
    uomCode: form.uomCode.trim(),
    requestedAtUtc: toIsoFromLocalInput(form.requestedAtUtc),
    idempotencyKey: optionalText(form.idempotencyKey) ?? `receipt-${form.workOrderId.trim()}`,
  }
  await createReceiptRequest(body)
  successMessage.value = `完工入库请求 ${body.workOrderId} 已提交。`
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function toIsoFromLocalInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toISOString()
}
function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}
function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function firstQueryValue(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="完工入库" :breadcrumbs="[{ label: '制造执行' }]" :count="`${filtered.length} 条入库请求`">
      <template #actions>
        <Button size="sm" type="button" :disabled="!hasReceiptContext" @click="openReceiptSheet">
          <PackageCheckIcon aria-hidden="true" />
          {{ hasReceiptContext ? '新增入库请求' : '从工单详情发起' }}
        </Button>
        <Button size="sm" type="button" variant="outline" :disabled="receiptRequestsPending" @click="refreshReceiptRequests">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="入库请求" :value="receiptRequests.length" hint="生产完成后的成品入库" />
      <SectionCard description="待处理" :value="pendingCount" hint="尚未完成入库" />
      <SectionCard description="已完成" :value="receiptRequests.length - pendingCount" hint="已入库成功" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索请求号、工单、物料">
      <template #filters>
        <Select v-model="statusFilter">
          <SelectTrigger class="h-9 w-32" aria-label="入库状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="pagedRows"
      row-key="receiptRequestId"
      :loading="receiptRequestsPending"
      empty-message="暂无完工入库请求。通常从报工完成、质量放行或工单详情发起。"
    >
      <template #cell-quantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template>
      <template #cell-receiptStatus="{ row }"><StatusBadge :value="row.receiptStatus" /></template>
      <template #cell-requestedAtUtc="{ row }">{{ formatDateTime(row.requestedAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <RowActions :label="`入库请求操作 ${row.receiptRequestId ?? ''}`">
          <DropdownMenuItem :disabled="!row.workOrderId" @click="openWorkOrder(row.workOrderId)">
            <EyeIcon aria-hidden="true" />
            查看工单
          </DropdownMenuItem>
        </RowActions>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="filtered.length" />

    <p v-if="!receiptRequestsPending && receiptRequests.length >= filters.take" class="text-xs text-muted-foreground">
      已加载前 {{ filters.take }} 条入库请求（后端返回上限），使用搜索或状态筛选定位更多请求。
    </p>

    <Dialog v-model:open="receiptSheetOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>新增入库请求</DialogTitle>
          <DialogDescription>用于生产完成后的成品入库申请，工单和物料由工单详情或报工完成结果带出。</DialogDescription>
        </DialogHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitReceiptRequest">
          <p v-if="createErrorMessage" class="text-sm text-destructive" role="alert">{{ createErrorMessage }}</p>
          <p v-if="successMessage" class="text-sm text-success" role="status">{{ successMessage }}</p>

          <FieldGroup class="grid gap-3">
            <Field>
              <FieldLabel for="receipt-work-order">工单号</FieldLabel>
              <Input id="receipt-work-order" v-model="form.workOrderId" readonly required />
            </Field>
            <Field>
              <FieldLabel for="receipt-sku">物料</FieldLabel>
              <Input id="receipt-sku" v-model="form.skuId" readonly required />
            </Field>
            <Field>
              <FieldLabel for="receipt-quantity">数量</FieldLabel>
              <Input id="receipt-quantity" v-model="form.quantity" inputmode="decimal" min="0" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="receipt-uom">单位</FieldLabel>
              <Input id="receipt-uom" v-model="form.uomCode" required />
            </Field>
            <Field>
              <FieldLabel for="receipt-requested-at">请求时间</FieldLabel>
              <Input id="receipt-requested-at" v-model="form.requestedAtUtc" required type="datetime-local" />
            </Field>
          </FieldGroup>

          <DialogFooter>
            <Button type="button" variant="outline" @click="receiptSheetOpen = false">取消</Button>
            <Button type="submit" :disabled="createReceiptRequestPending || !canCreate">
              <Spinner v-if="createReceiptRequestPending" aria-hidden="true" />
              <PackageCheckIcon v-else aria-hidden="true" />
              提交入库请求
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
