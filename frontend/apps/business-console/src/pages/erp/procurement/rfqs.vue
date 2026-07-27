<script setup lang="ts">
import type { BusinessConsoleErpRequestForQuotationItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpRequestsForQuotation } from '@/composables/useBusinessErp'
import { useErpItemCatalog, useErpPartnerCatalog } from '@/composables/useErpPickerCatalog'
import { usePagedList } from '@/composables/usePagedList'
import EntityMultiPicker from '@/components/business/EntityMultiPicker.vue'
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
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import { formatDate, formatQuantity, pickerInvalidClass } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '询价 RFQ',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

const rfqs = useErpRequestsForQuotation()
// 询价对象与物料从主数据目录里选；供应商是多选，用应用侧的多选组合件。
const { supplierOptions, partnersPending } = useErpPartnerCatalog()
const { skuOptions, skusPending, uomOptions, uomsPending, baseUomBySku } = useErpItemCatalog()
const { page, pageSize } = usePagedList(rfqs.filters, { resetOn: [() => rfqs.filters.keyword] })

const columns: NvDataTableColumn<BusinessConsoleErpRequestForQuotationItem>[] = [
  { key: 'rfqNo', header: 'RFQ', cellClass: 'font-medium', accessor: (r) => r.rfqNo ?? '-' },
  {
    key: 'supplierCodes',
    header: '供应商',
    accessor: (r) => (r.supplierCodes ?? []).join(' / ') || '-',
  },
  {
    key: 'lineCount',
    header: '明细',
    align: 'end',
    width: 'w-20',
    accessor: (r) => r.lines?.length ?? 0,
  },
  { key: 'status', header: '状态', width: 'w-28' },
  {
    key: 'createdAtUtc',
    header: '创建时间',
    width: 'w-40',
    accessor: (r) => formatDate(r.createdAtUtc),
  },
]

const openCount = computed(
  () => rfqs.items.value.filter((r) => (r.status ?? '').toLowerCase() === 'open').length,
)
const requestedQuantity = computed(() =>
  rfqs.items.value
    .flatMap((r) => r.lines ?? [])
    .reduce((sum, line) => sum + (line.quantity ?? 0), 0),
)
const rfqCells = computed<NvMetricStripCell[]>(() => [
  { key: 'open', label: '询价中', value: openCount.value, unit: '单', meta: '等待供应商回价' },
  {
    key: 'quantity',
    label: '询价数量',
    value: formatQuantity(requestedQuantity.value),
    meta: `当前列表 ${rfqs.items.value.length} 张询价单合计`,
  },
])

const open = shallowRef(false)
const form = reactive({
  suppliers: '',
  skuCode: '',
  uomCode: '',
  quantity: '1',
  requiredDate: '',
})
// 询价单位默认跟随物料的基本单位；用户仍可改成采购包装单位。
watch(
  () => form.skuCode,
  (skuCode) => {
    const baseUom = baseUomBySku.value.get(skuCode.trim())
    if (baseUom) form.uomCode = baseUom
  },
)
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)
const supplierCodeList = computed(() =>
  form.suppliers
    .split(/[,\s]+/)
    .map((s) => s.trim())
    .filter(Boolean),
)
const invalid = computed(() => ({
  suppliers: supplierCodeList.value.length === 0,
  skuCode: !form.skuCode.trim(),
  uomCode: !form.uomCode.trim(),
  requiredDate: !form.requiredDate,
  quantity: !(Number(form.quantity) > 0),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

function openDialog() {
  form.suppliers = ''
  form.skuCode = ''
  form.uomCode = ''
  form.quantity = '1'
  form.requiredDate = ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  showErrors.value = true
  if (!canSubmit.value) return
  const quantity = Number(form.quantity)
  const supplierCodes = supplierCodeList.value
  try {
    await rfqs.createRequestForQuotation({
      supplierCodes,
      lines: [
        {
          lineNo: '10',
          skuCode: form.skuCode.trim(),
          uomCode: form.uomCode.trim(),
          quantity,
          requiredDate: form.requiredDate,
        },
      ],
    })
    open.value = false
    notifySuccess('RFQ 已创建')
  } catch (error) {
    notifyError(rfqs.createRequestForQuotationError.value ?? error, '发起询价失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="询价 RFQ"
      :breadcrumbs="[{ label: '经营管理' }, { label: '采购' }]"
      :count="`${rfqs.total.value} 张 RFQ`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="rfqs.pending.value"
          @click="rfqs.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openDialog">
          <PlusIcon aria-hidden="true" />
          新建 RFQ
        </NvButton>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="rfqCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="rfqs.filters.keyword"
          class="h-9 w-64"
          placeholder="RFQ / 供应商 / 物料"
          aria-label="RFQ 关键字"
        />
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="rfqs.total.value"
      :columns="columns"
      :rows="rfqs.items.value"
      :row-key="(r: BusinessConsoleErpRequestForQuotationItem) => r.rfqNo ?? 'RFQ'"
      :loading="rfqs.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无 RFQ。可从采购申请或供应商策略发起真实询价。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建 RFQ</NvDialogTitle>
          <NvDialogDescription class="sr-only">向供应商发起单项物料询价。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField class="sm:col-span-2">
              <NvFieldLabel for="erp-rfq-suppliers">
                供应商 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <EntityMultiPicker
                id="erp-rfq-suppliers"
                v-model="form.suppliers"
                :options="supplierOptions"
                title="选择供应商"
                placeholder="添加询价供应商"
                source-text="数据来自基础数据业务伙伴（供应商角色）"
                empty-text="暂无供应商，请先在「基础数据 · 业务伙伴」维护"
                :loading="partnersPending"
                aria-label="供应商"
                :invalid="showErrors && invalid.suppliers"
                selection-empty-text="至少选择一家供应商"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-rfq-sku">
                物料 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-rfq-sku"
                v-model="form.skuCode"
                :options="skuOptions"
                title="选择物料"
                placeholder="选择物料"
                source-text="数据来自基础数据物料主数据"
                empty-text="暂无物料，请先在「基础数据 · 物料」维护"
                :loading="skusPending"
                aria-label="物料"
                :class="pickerInvalidClass(showErrors && invalid.skuCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-rfq-uom">
                单位 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-rfq-uom"
                v-model="form.uomCode"
                :options="uomOptions"
                title="选择单位"
                placeholder="选择询价单位"
                source-text="数据来自基础数据计量单位；选定物料后默认带出基本单位"
                empty-text="暂无计量单位，请先在「基础数据 · 计量单位」维护"
                :loading="uomsPending"
                aria-label="单位"
                :class="pickerInvalidClass(showErrors && invalid.uomCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-rfq-qty">
                数量 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-rfq-qty"
                v-model="form.quantity"
                type="number"
                min="1"
                step="1"
                :data-invalid="showErrors && invalid.quantity ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-rfq-date">
                需求日期 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-rfq-date"
                v-model="form.requiredDate"
                type="date"
                :data-invalid="showErrors && invalid.requiredDate ? '' : undefined"
              />
            </NvField>
          </NvFieldGroup>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请选择供应商、物料、单位，填写需求日期，并给出正数数量。
          </p>
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="rfqs.createRequestForQuotationPending.value">
              <Spinner v-if="rfqs.createRequestForQuotationPending.value" aria-hidden="true" />
              发起询价
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
