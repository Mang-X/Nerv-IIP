<script setup lang="ts">
import type { BusinessConsoleMesCreateReceiptRequest } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import {
  isFailedReceiptStatus,
  mesReceiptStatusOptions,
  receiptStatusLabel,
  receiptStatusTone,
} from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { makeIdempotencyKey, useMesFinishedGoodsReceipts } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvDropdownMenuItem,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { EyeIcon, PackageCheckIcon, RefreshCwIcon, RotateCcwIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '完工入库',
    requiredPermissions: ['business.mes.receipts.read'],
  },
})

const {
  createReceiptRequest,
  createReceiptRequestError,
  createReceiptRequestPending,
  filters,
  receiptRequests,
  receiptRequestsError,
  receiptRequestsPending,
  receiptRequestsTotal,
  refreshReceiptRequests,
  retryInventoryPosting,
  retryingRequestNo,
} = useMesFinishedGoodsReceipts()
const { page, pageSize, resetPage } = usePagedList(filters, { resetOn: [() => filters.status] })
const { resolveSku } = useMesDisplayNames()

const route = useRoute()
const router = useRouter()
const receiptSheetOpen = shallowRef(false)
const quickViewWorkOrderId = ref<string | null>(null)

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const statusOptions = mesReceiptStatusOptions

// 重试端点要求 business.mes.receipts.manage（网关 MesReceiptsManage），页面路由只需 read。
// 无 manage 权限的只读用户不应看到重试按钮，否则点击必得 403（前后端操作级权限同步）。
const auth = useAuthStore()
const canManageReceipts = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.mesReceiptsManage),
)

// 完工入库状态标签/徽章色集中于 useMesReferenceLabels（与 mesReceiptStatusOptions 同域，避免漂移）。
function canRetry(row: ReceiptRow) {
  return (
    canManageReceipts.value &&
    isFailedReceiptStatus(row.receiptStatus) &&
    isNonEmpty(row.requestNo ?? '')
  )
}

const form = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  workOrderId: '',
  skuId: '',
  quantity: '1',
  unitCost: '',
  uomCode: 'EA',
  requestedAtUtc: toLocalDateTimeInput(new Date()),
  // 幂等键按「登记会话」生成：会话内瞬时失败后重投复用同键→后端回放不重复入库；
  // 成功后 resetCreateForm 轮换新键→同一工单连续登记产生两笔申请（真正支持高频连录）。
  idempotencyKey: makeIdempotencyKey('receipt'),
})

const listErrorMessage = computed(() => formatError(receiptRequestsError.value))
const hasReceiptContext = computed(() => isNonEmpty(form.workOrderId) && isNonEmpty(form.skuId))
const canCreate = computed(
  () =>
    // 创建端点同样要求 receipts.manage：无 manage 权限连提交都不放行（防只读用户提交后才 403）。
    canManageReceipts.value &&
    isNonEmpty(form.organizationId) &&
    isNonEmpty(form.environmentId) &&
    isNonEmpty(form.workOrderId) &&
    isNonEmpty(form.skuId) &&
    toPositiveNumber(form.quantity) !== undefined &&
    toPositiveNumber(form.unitCost) !== undefined &&
    isNonEmpty(form.uomCode) &&
    isNonEmpty(form.requestedAtUtc),
)

// 状态筛选进 URL query（A1 §5.3）：进入读 query 初始化，变更防抖后 replace 写回，默认值（all）删除键。
watch(
  () => route.query.status,
  (value) => {
    const next = firstQueryValue(value)
    if (next && next !== filters.status) filters.status = next
  },
  { immediate: true },
)
watch(
  () => filters.status,
  (value) => {
    const current = firstQueryValue(route.query.status)
    if ((value ?? '') === (current ?? '')) return
    void router.replace({
      query: { ...route.query, status: value ? value : undefined },
    })
  },
)

watch(
  () => route.query,
  (query) => {
    const workOrderId = firstQueryValue(query.workOrderId)
    const skuId = firstQueryValue(query.skuId)
    const quantity = firstQueryValue(query.quantity)
    if (workOrderId) form.workOrderId = workOrderId
    if (skuId) form.skuId = skuId
    if (quantity) form.quantity = quantity
    resetPage()
    // 仅有 manage 权限时才自动打开登记 Sheet（读用户从工单详情带 query 进来也不弹创建）。
    if (workOrderId && skuId && canManageReceipts.value) receiptSheetOpen.value = true
  },
  { immediate: true },
)

type ReceiptRow = (typeof receiptRequests)['value'][number]
const columns: NvDataTableColumn<ReceiptRow>[] = [
  {
    key: 'requestNo',
    header: '入库单',
    cellClass: 'font-medium',
    accessor: (r) => r.requestNo ?? r.receiptRequestId ?? '无',
  },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'skuId', header: '成品', accessor: (r) => resolveSku(r.skuCode ?? r.skuId) ?? '无' },
  { key: 'quantity', header: '入库数量', align: 'end', width: 'w-28' },
  { key: 'unitCost', header: '单位成本', align: 'end', width: 'w-28' },
  { key: 'receiptStatus', header: '入库状态', width: 'w-48' },
  { key: 'requestedAtUtc', header: '登记时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

function openWorkOrder(workOrderId?: string | null) {
  if (workOrderId) quickViewWorkOrderId.value = workOrderId
}
function openReceiptSheet() {
  if (!hasReceiptContext.value || !canManageReceipts.value) return
  receiptSheetOpen.value = true
}
function resetCreateForm() {
  form.quantity = '1'
  form.unitCost = ''
  form.uomCode = 'EA'
  form.requestedAtUtc = toLocalDateTimeInput(new Date())
  // 仅在登记成功后调用：轮换幂等键，使同一工单的下一笔登记成为一笔独立申请（连录不回放旧单）。
  form.idempotencyKey = makeIdempotencyKey('receipt')
}

async function submitReceiptRequest() {
  if (!canCreate.value) return
  const body: BusinessConsoleMesCreateReceiptRequest = {
    organizationId: form.organizationId.trim(),
    environmentId: form.environmentId.trim(),
    workOrderId: form.workOrderId.trim(),
    skuId: form.skuId.trim(),
    quantity: toPositiveNumber(form.quantity),
    unitCost: toPositiveNumber(form.unitCost),
    uomCode: form.uomCode.trim(),
    requestedAtUtc: toIsoFromLocalInput(form.requestedAtUtc),
    idempotencyKey: optionalText(form.idempotencyKey) ?? makeIdempotencyKey('receipt'),
  }
  // 操作结果一律走 toast（反馈规范）。登记成功与「列表刷新」是两件独立的事：
  // 刷新失败不得否定已成功的登记（否则用户同时看到成功+失败并可能重复提交），刷新错误由列表自身错误态负责提示。
  try {
    await createReceiptRequest(body)
  } catch {
    // 超量校验作为「实际错误消息」传入（notifyError 会优先透传 ≤60 字中文原文，故不能只放 fallback）；
    // 其余（网络/服务/权限）由 notifyError 统一映射。
    const err = createReceiptRequestError.value ?? undefined
    const overQuantity = overQuantityMessage(err)
    notifyError(overQuantity ? new Error(overQuantity) : err, '登记完工入库失败，请稍后重试。')
    return
  }
  notifySuccess(`已登记完工入库 · 工单 ${body.workOrderId ?? ''}，可在列表查看入库状态。`)
  resetCreateForm()
  void Promise.resolve(refreshReceiptRequests()).catch(() => {})
}

async function retryRow(row: ReceiptRow) {
  const requestNo = row.requestNo
  if (!requestNo || !canRetry(row)) return
  try {
    await retryInventoryPosting(requestNo)
  } catch (error) {
    notifyError(error, '重投入库过账失败，请稍后重试。')
    return
  }
  // 重投已成功：刷新失败同样不否定成功（列表错误态负责提示）。
  notifySuccess(`已重新提交入库过账（${requestNo}），过账完成后刷新即显示为「已入库」。`)
  void Promise.resolve(refreshReceiptRequests()).catch(() => {})
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
function formatUnitCost(value?: number | null) {
  return value === undefined || value === null
    ? '—'
    : new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 6 }).format(value)
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
function toPositiveNumber(value: string) {
  const parsed = toOptionalNumber(value)
  return parsed !== undefined && parsed > 0 ? parsed : undefined
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
// 累计申请量超过工单完工数量：后端返回带 WorkOrderId 后缀的技术消息，收敛成 issue 指定的一线业务文案。
// 命中时返回映射文案（由调用方作为「实际错误消息」传入 notifyError，绕过其 ≤60 字中文原文透传）；否则 undefined。
function overQuantityMessage(error: unknown): string | undefined {
  const raw = formatError(error)
  if (raw && (raw.includes('累计完工入库申请数量超过') || raw.includes('完工数量'))) {
    return '累计请求量超过完工数量，请先核对该工单的报工完成数量后再登记入库。'
  }
  return undefined
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="完工入库"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${receiptRequestsTotal} 条入库登记`"
    >
      <template #actions>
        <!-- 登记完工入库调用创建端点（需 receipts.manage）：无 manage 权限的只读用户不显示创建入口。 -->
        <NvButton
          v-if="canManageReceipts"
          size="sm"
          type="button"
          :disabled="!hasReceiptContext"
          @click="openReceiptSheet"
        >
          <PackageCheckIcon aria-hidden="true" />
          {{ hasReceiptContext ? '登记完工入库' : '从工单详情发起' }}
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="receiptRequestsPending"
          @click="refreshReceiptRequests"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="按入库状态筛选"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in statusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="receiptRequestsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="receiptRequests"
      row-key="receiptRequestId"
      :loading="receiptRequestsPending"
      empty-message="还没有完工入库登记。末道工序报完工后，在此把成品登记入库即会出现对应记录。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-requestNo="{ row }">
        <span v-if="row.requestNo">{{ row.requestNo }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-workOrderId="{ row }">
        <button
          v-if="row.workOrderId"
          type="button"
          class="text-brand underline-offset-4 hover:underline"
          @click="openWorkOrder(row.workOrderId)"
        >
          {{ row.workOrderNo ?? row.workOrderId }}
        </button>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-skuId="{ row }">
        <span v-if="row.skuId">{{ resolveSku(row.skuCode ?? row.skuId) }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template
      >
      <template #cell-unitCost="{ row }"
        ><span class="tabular-nums">{{ formatUnitCost(row.unitCost) }}</span></template
      >
      <template #cell-receiptStatus="{ row }">
        <div class="grid gap-1">
          <NvStatusBadge
            :tone="receiptStatusTone(row.receiptStatus)"
            :label="receiptStatusLabel(row.receiptStatus)"
          />
          <!-- 失败原因：库存过账失败时给出后端失败信息，一线据此判断是补库存还是改数量后再重投。 -->
          <p
            v-if="isFailedReceiptStatus(row.receiptStatus) && row.inventoryPostingFailureMessage"
            class="max-w-64 text-xs leading-snug text-destructive"
            :title="row.inventoryPostingFailureMessage"
          >
            {{ row.inventoryPostingFailureMessage }}
          </p>
        </div>
      </template>
      <template #cell-requestedAtUtc="{ row }">{{ formatDateTime(row.requestedAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <div class="flex items-center justify-end gap-1">
          <!-- 失败重试为该行高频主动作（A1 §2）：行内直达，重试中仅本行禁用 + spinner。 -->
          <NvButton
            v-if="canRetry(row)"
            size="sm"
            type="button"
            variant="outline"
            :disabled="retryingRequestNo === row.requestNo"
            @click="retryRow(row)"
          >
            <Spinner v-if="retryingRequestNo === row.requestNo" aria-hidden="true" />
            <RotateCcwIcon v-else aria-hidden="true" />
            重试
          </NvButton>
          <NvRowActions :label="`入库登记操作 ${row.requestNo ?? row.workOrderId ?? ''}`">
            <NvDropdownMenuItem
              :disabled="!row.workOrderId"
              @click="openWorkOrder(row.workOrderId)"
            >
              <EyeIcon aria-hidden="true" />
              查看工单
            </NvDropdownMenuItem>
          </NvRowActions>
        </div>
      </template>
    </NvDataTable>

    <NvSheet v-model:open="receiptSheetOpen">
      <NvSheetContent class="w-full overflow-y-auto sm:max-w-xl">
        <NvSheetHeader>
          <NvSheetTitle>登记完工入库</NvSheetTitle>
          <NvSheetDescription
            >把完工成品登记入库。工单与成品由报工完成或工单详情带出，只需确认入库数量、单位成本和单位。</NvSheetDescription
          >
        </NvSheetHeader>

        <!-- 结果一律走 toast（成功/失败/超量均 notifySuccess·notifyError）：Sheet 内不留常驻结果条。
             成功后重置表单留在原地支持高频连录，失败保持打开可修正重提。 -->
        <form class="grid content-start gap-4 p-4" @submit.prevent="submitReceiptRequest">
          <NvFieldGroup class="grid gap-3">
            <NvField>
              <NvFieldLabel for="receipt-work-order">工单号</NvFieldLabel>
              <NvInput id="receipt-work-order" v-model="form.workOrderId" readonly required />
            </NvField>
            <NvField>
              <NvFieldLabel for="receipt-sku">成品</NvFieldLabel>
              <NvInput id="receipt-sku" v-model="form.skuId" readonly required />
            </NvField>
            <NvField>
              <NvFieldLabel for="receipt-quantity">入库数量</NvFieldLabel>
              <NvInput
                id="receipt-quantity"
                v-model="form.quantity"
                inputmode="decimal"
                min="0.000001"
                step="0.000001"
                required
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="receipt-unit-cost">单位成本</NvFieldLabel>
              <NvInput
                id="receipt-unit-cost"
                v-model="form.unitCost"
                inputmode="decimal"
                min="0.000001"
                step="0.000001"
                required
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="receipt-uom">单位</NvFieldLabel>
              <NvInput id="receipt-uom" v-model="form.uomCode" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="receipt-requested-at">登记时间</NvFieldLabel>
              <NvInput
                id="receipt-requested-at"
                v-model="form.requestedAtUtc"
                required
                type="datetime-local"
              />
            </NvField>
          </NvFieldGroup>

          <NvSheetFooter>
            <NvButton type="button" variant="outline" @click="receiptSheetOpen = false"
              >取消</NvButton
            >
            <NvButton type="submit" :disabled="createReceiptRequestPending || !canCreate">
              <Spinner v-if="createReceiptRequestPending" aria-hidden="true" />
              <PackageCheckIcon v-else aria-hidden="true" />
              提交入库登记
            </NvButton>
          </NvSheetFooter>
        </form>
      </NvSheetContent>
    </NvSheet>

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
