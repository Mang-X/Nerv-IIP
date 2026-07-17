<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import ReceiptCreateSheet from '@/components/mes/ReceiptCreateSheet.vue'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import {
  isFailedReceiptStatus,
  mesReceiptStatusOptions,
  receiptStatusLabel,
  receiptStatusTone,
} from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { useMesFinishedGoodsReceipts } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvPageHeader,
  NvRowActions,
  NvDropdownMenuItem,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
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

// 路由页只负责编排：列表 + 状态筛选（URL 同步）+ 逐行重试 + 权限门控 + 工单上下文；
// 登记表单/产出批次/提交封装在 ReceiptCreateSheet + useReceiptCreateForm（Vue best-practices §2）。
const {
  filters,
  receiptRequests,
  receiptRequestsError,
  receiptRequestsPending,
  receiptRequestsTotal,
  refreshReceiptRequests,
  retryInventoryPosting,
  isRetrying,
} = useMesFinishedGoodsReceipts()
const { page, pageSize, resetPage } = usePagedList(filters, { resetOn: [() => filters.status] })
const { resolveSku } = useMesDisplayNames()

const route = useRoute()
const router = useRouter()
const receiptSheetOpen = shallowRef(false)
const quickViewWorkOrderId = ref<string | null>(null)

// 登记完工入库的工单上下文（由工单详情/报工带 query 进入）；传给 ReceiptCreateSheet。
const receiptContext = reactive({ workOrderId: '', skuId: '', quantity: '' })

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const statusOptions = mesReceiptStatusOptions

// 重试端点要求 business.mes.receipts.manage（网关 MesReceiptsManage），页面路由只需 read。
// 无 manage 权限的只读用户不应看到重试/创建入口，否则点击必得 403（前后端操作级权限同步）。
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

const listErrorMessage = computed(() => formatError(receiptRequestsError.value))
const hasReceiptContext = computed(
  () => isNonEmpty(receiptContext.workOrderId) && isNonEmpty(receiptContext.skuId),
)

// 状态筛选进 URL query（A1 §5.3）：进入读 query 初始化，变更防抖后 replace 写回，默认值（all）删除键。
watch(
  () => route.query.status,
  (value) => {
    // 空值也要生效：从失败状态页后退到无 status 的 URL 时清回默认（否则列表卡在旧筛选）。
    const next = firstQueryValue(value) || undefined
    if (next !== filters.status) filters.status = next
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
    // 无条件覆盖：query 缺失即清空，避免离开/切换后残留旧工单或混合上下文。
    receiptContext.workOrderId = firstQueryValue(query.workOrderId)
    receiptContext.skuId = firstQueryValue(query.skuId)
    receiptContext.quantity = firstQueryValue(query.quantity)
    resetPage()
    if (receiptContext.workOrderId && receiptContext.skuId && canManageReceipts.value) {
      // 仅有 manage 权限时才自动打开登记 Sheet（读用户从工单详情带 query 进来也不弹创建）。
      receiptSheetOpen.value = true
    } else if (!receiptContext.workOrderId || !receiptContext.skuId) {
      // 上下文不完整则关闭登记 Sheet，避免用陈旧/混合上下文提交。
      receiptSheetOpen.value = false
    }
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

async function retryRow(row: ReceiptRow) {
  const requestNo = row.requestNo
  if (!requestNo || !canRetry(row)) return
  try {
    await retryInventoryPosting(requestNo)
  } catch (error) {
    notifyError(error, '重投入库过账失败，请稍后重试。')
    return
  }
  // 重投已成功：刷新失败不否定成功（列表错误态负责提示）。
  notifySuccess(`已重新提交入库过账（${requestNo}），过账完成后刷新即显示为「已入库」。`)
  void Promise.resolve(refreshReceiptRequests()).catch(() => {})
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatUnitCost(value?: number | null) {
  return value === undefined || value === null
    ? '—'
    : new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 6 }).format(value)
}
function firstQueryValue(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
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
            :disabled="isRetrying(row.requestNo ?? '')"
            @click="retryRow(row)"
          >
            <Spinner v-if="isRetrying(row.requestNo ?? '')" aria-hidden="true" />
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

    <ReceiptCreateSheet
      v-model:open="receiptSheetOpen"
      :organization-id="filters.organizationId"
      :environment-id="filters.environmentId"
      :work-order-id="receiptContext.workOrderId"
      :sku-id="receiptContext.skuId"
      :initial-quantity="receiptContext.quantity"
    />

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
