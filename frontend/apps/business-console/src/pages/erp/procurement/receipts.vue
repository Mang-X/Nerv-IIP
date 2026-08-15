<script setup lang="ts">
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpPurchaseReceipts } from '@/composables/useBusinessErp'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import { useSkuNames } from '@/composables/useSkuNames'
import { usePagedList } from '@/composables/usePagedList'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PackageCheckIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { UNAVAILABLE_TEXT, erpReadState, formatQuantity, readCount } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '采购收货',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

const receipts = useErpPurchaseReceipts()
// 供应商 / 物料列显名称：读面只回编码，中文名在主数据里，前端按编码 join。
const { resolvePartner } = useBusinessPartnerNames()
const { resolveSkuName } = useSkuNames()
const { page, pageSize } = usePagedList(receipts.filters, {
  resetOn: [() => receipts.filters.keyword],
})

const rows = computed(() =>
  receipts.items.value.flatMap((order) =>
    (order.lines ?? []).map((line) => ({
      purchaseOrderNo: order.purchaseOrderNo ?? '-',
      supplierCode: order.supplierCode ?? '-',
      supplierName: resolvePartner(order.supplierCode),
      status: order.status ?? '-',
      receiptReadiness: order.receiptReadiness ?? '-',
      lineNo: line.lineNo ?? '-',
      skuCode: line.skuCode ?? '-',
      skuName: resolveSkuName(line.skuCode),
      orderedQuantity: line.orderedQuantity ?? 0,
      receivedQuantity: line.receivedQuantity ?? 0,
      openQuantity: Math.max((line.orderedQuantity ?? 0) - (line.receivedQuantity ?? 0), 0),
    })),
  ),
)

const columns: NvDataTableColumn<(typeof rows.value)[number]>[] = [
  { key: 'purchaseOrderNo', header: '采购单', cellClass: 'font-medium' },
  { key: 'supplierCode', header: '供应商' },
  { key: 'lineNo', header: '行号', width: 'w-20' },
  { key: 'skuCode', header: '物料' },
  { key: 'orderedQuantity', header: '订单数量', align: 'end', width: 'w-28' },
  { key: 'receivedQuantity', header: '已收数量', align: 'end', width: 'w-28' },
  { key: 'openQuantity', header: '待收数量', align: 'end', width: 'w-28' },
  { key: 'receiptReadiness', header: '收货状态', width: 'w-28' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

const readState = computed(() =>
  erpReadState({
    noun: '可收货采购订单',
    unit: '张',
    ready: receipts.ready.value,
    pending: receipts.pending.value,
    error: receipts.error.value,
    total: receipts.total.value,
    filtered: Boolean(receipts.filters.keyword || receipts.filters.status),
    emptyHint: '还没有可收货的采购订单。采购订单释放后会在这里跟进入库。',
  }),
)

const receivableLines = computed(() => rows.value.filter((row) => row.openQuantity > 0).length)
const openQuantity = computed(() => rows.value.reduce((sum, row) => sum + row.openQuantity, 0))
const receiptCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'lines',
    label: '可收货行',
    value: readCount(readState.value, receivableLines.value),
    unit: readState.value.trustworthy ? '行' : '',
    meta: readState.value.trustworthy ? undefined : readState.value.emptyMessage,
  },
  {
    key: 'open-quantity',
    label: '待收数量',
    // 取不到采购订单时显 `—`：0 会被读成"没有在途待收"。
    value: readState.value.trustworthy ? formatQuantity(openQuantity.value) : UNAVAILABLE_TEXT,
    meta: readState.value.trustworthy ? '已下达采购但尚未入库的数量' : readState.value.emptyMessage,
  },
])

// 质检状态是收货时的真实业务决策点（#1345）：ERP 命令必填，决定是否触发来料检验与是否计提应付。
// 合法值与 ERP 域 ErpReceiptQualityStatuses 对齐：unrestricted / quality / blocked。
// 文案如实说明后果：只有 unrestricted 免检（在 WmsReceivingQualityStatuses 跳检表内），
// quality 与 blocked 都会转来料检验；应付只计 unrestricted 与 quality，blocked 不计。
const qualityStatusOptions = [
  { value: 'quality', label: '待检（转来料检验，计应付）' },
  { value: 'unrestricted', label: '合格（免检直接可用，计应付）' },
  { value: 'blocked', label: '冻结（暂扣不计应付，仍转检验）' },
] as const

// 「带出式录入」：收货对象只能由所选采购行带入，弹窗自身不提供采购单/行号的挑选或补填入口。
const open = shallowRef(false)
const receiptRow = shallowRef<(typeof rows.value)[number] | null>(null)
const form = reactive({ receivedQuantity: '1', purchaseReceiptNo: '', qualityStatus: 'quality' })
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  receivedQuantity: !(Number(form.receivedQuantity) > 0),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

const receiptContextItems = computed(() => {
  const row = receiptRow.value
  if (!row) return []
  return [
    { label: '采购单', value: row.purchaseOrderNo },
    { label: '采购行', value: row.lineNo },
    {
      label: '供应商',
      value: row.supplierName ? `${row.supplierName}（${row.supplierCode}）` : row.supplierCode,
    },
    { label: '物料', value: row.skuName ? `${row.skuName}（${row.skuCode}）` : row.skuCode },
    { label: '订单数量', value: formatQuantity(row.orderedQuantity) },
    { label: '已收数量', value: formatQuantity(row.receivedQuantity) },
    { label: '待收数量', value: formatQuantity(row.openQuantity) },
  ]
})

function openDialog(row: (typeof rows.value)[number]) {
  if (row.purchaseOrderNo === '-' || row.lineNo === '-') return
  receiptRow.value = row
  // 默认按待收数量整单收货，一线只在部分到货时改小。
  form.receivedQuantity = String(row.openQuantity > 0 ? row.openQuantity : 1)
  form.purchaseReceiptNo = ''
  // 默认「待检」：来料先入待检库位、由质检裁定放行，是收货环节业务上更稳妥的默认。
  form.qualityStatus = 'quality'
  showErrors.value = false
  open.value = true
}

async function submit() {
  const row = receiptRow.value
  if (!row) return
  showErrors.value = true
  if (!canSubmit.value) return
  try {
    await receipts.recordPurchaseReceipt({
      purchaseOrderNo: row.purchaseOrderNo,
      purchaseReceiptNo: form.purchaseReceiptNo.trim() || undefined,
      lines: [
        {
          purchaseOrderLineNo: row.lineNo,
          receivedQuantity: Number(form.receivedQuantity),
          qualityStatus: form.qualityStatus,
        },
      ],
    })
    open.value = false
    notifySuccess(`${row.purchaseOrderNo} 第 ${row.lineNo} 行已收货`)
  } catch (error) {
    notifyOperationFailure(
      '确认收货失败',
      receipts.recordPurchaseReceiptError.value ?? error,
      '确认收货失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="采购收货"
      :breadcrumbs="[{ label: '经营管理' }, { label: '采购' }]"
      :count="readState.count"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="receipts.pending.value"
          @click="receipts.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="receiptCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="receipts.filters.keyword"
          class="h-9 w-64"
          placeholder="采购单 / 供应商 / 物料"
          aria-label="采购收货关键字"
        />
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="receipts.total.value"
      :columns="columns"
      :rows="rows"
      :row-key="(r) => `${r.purchaseOrderNo}-${r.lineNo}`"
      :loading="receipts.pending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="readState.emptyMessage"
      :error="readState.error"
      :error-message="readState.errorMessage"
      :awaiting-scope="readState.awaitingScope"
      :awaiting-scope-message="readState.awaitingScopeMessage"
      @retry="receipts.refresh"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-supplierCode="{ row }">
        <CodeWithNameCell :code="row.supplierCode" :name="row.supplierName" />
      </template>
      <template #cell-skuCode="{ row }">
        <CodeWithNameCell :code="row.skuCode" :name="row.skuName" fallback="未指定物料" />
      </template>
      <template #cell-orderedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.orderedQuantity) }}</span></template
      >
      <template #cell-receivedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.receivedQuantity) }}</span></template
      >
      <template #cell-openQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.openQuantity) }}</span></template
      >
      <template #cell-receiptReadiness="{ row }"
        ><NvStatusBadge :value="row.receiptReadiness"
      /></template>
      <template #cell-actions="{ row }">
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="row.openQuantity <= 0"
          @click="openDialog(row)"
          >登记收货</NvButton
        >
      </template>
    </NvDataTable>

    <!-- 「带出式录入」：采购单 / 行号 / 物料 / 数量全部由所选行带出，只读呈现，不做输入位。 -->
    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>登记采购收货</NvDialogTitle>
          <NvDialogDescription class="sr-only">
            收货对象：采购单 {{ receiptRow?.purchaseOrderNo ?? '' }} 第
            {{ receiptRow?.lineNo ?? '' }} 行。
          </NvDialogDescription>
        </NvDialogHeader>
        <form v-if="receiptRow" class="grid gap-4" @submit.prevent="submit">
          <CarriedContextSummary label="收货对象" :items="receiptContextItems" />
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="erp-receipt-qty">
                收货数量 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-receipt-qty"
                v-model="form.receivedQuantity"
                type="number"
                min="1"
                step="1"
                autofocus
                :data-invalid="showErrors && invalid.receivedQuantity ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-receipt-quality-status">
                质检状态 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvSelect v-model="form.qualityStatus">
                <NvSelectTrigger id="erp-receipt-quality-status" aria-label="质检状态"
                  ><NvSelectValue placeholder="选择质检状态"
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in qualityStatusOptions"
                    :key="option.value"
                    :value="option.value"
                    >{{ option.label }}</NvSelectItem
                  >
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField
              ><NvFieldLabel for="erp-receipt-no">送货单号（可选）</NvFieldLabel
              ><NvInput id="erp-receipt-no" v-model="form.purchaseReceiptNo" autocomplete="off"
            /></NvField>
          </NvFieldGroup>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            收货数量需为正数。
          </p>
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="receipts.recordPurchaseReceiptPending.value">
              <Spinner v-if="receipts.recordPurchaseReceiptPending.value" aria-hidden="true" />
              <PackageCheckIcon v-else aria-hidden="true" />
              确认收货
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
