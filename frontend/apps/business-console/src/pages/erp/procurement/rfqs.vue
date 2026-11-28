<script setup lang="ts">
import { usePermission } from '@/composables/usePermission'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type { BusinessConsoleErpRequestForQuotationItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpRequestsForQuotation } from '@/composables/erp/useBusinessErp'
import {
  useErpItemCatalog,
  useErpPartnerCatalog,
  useErpSiteCatalog,
} from '@/composables/erp/useErpPickerCatalog'
import { useBusinessPartnerNames } from '@/composables/master-data/useBusinessPartnerNames'
import { usePagedList } from '@/composables/shared/usePagedList'
import EntityMultiPicker from '@/components/business/EntityMultiPicker.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { i18n } from '@/i18n'
import {
  NvButton,
  NvDataTable,
  NvDatePicker,
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
  NvSpinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  UNAVAILABLE_TEXT,
  erpReadState,
  formatDate,
  formatQuantity,
  pickerInvalidClass,
  readCount,
} from '../shared'

const { locale } = i18n.global

const { can } = usePermission()

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
const { siteOptions, sitesPending } = useErpSiteCatalog()
const { uomOptions, uomsPending, baseUomBySku } = useErpItemCatalog()
// 列表侧另需 code→name 反查（目录只给下拉选项，不做反查）；底层同一份查询，不会重复请求。
const { resolvePartnerLabel } = useBusinessPartnerNames()
const { page, pageSize } = usePagedList(rfqs.filters, { resetOn: [() => rfqs.filters.keyword] })

const columns: NvDataTableColumn<BusinessConsoleErpRequestForQuotationItem>[] = [
  { key: 'rfqNo', header: 'RFQ', cellClass: 'font-medium', accessor: (r) => r.rfqNo ?? '-' },
  {
    key: 'supplierCodes',
    header: '供应商',
    accessor: (r) =>
      (r.supplierCodes ?? []).map((code) => resolvePartnerLabel(code, '')).join(' / ') || '-',
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
const readState = computed(() =>
  erpReadState({
    noun: '询价单',
    unit: '张',
    ready: rfqs.ready.value,
    pending: rfqs.pending.value,
    error: rfqs.error.value,
    total: rfqs.total.value,
    filtered: Boolean(rfqs.filters.keyword || rfqs.filters.status),
    emptyHint: '还没有询价单。可从采购申请或供应商策略发起真实询价。',
  }),
)

const rfqCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'open',
    label: '询价中',
    value: readCount(readState.value, openCount.value),
    unit: readState.value.trustworthy ? '单' : '',
    meta: readState.value.trustworthy ? '等待供应商回价' : readState.value.emptyMessage,
  },
  {
    key: 'quantity',
    label: '询价数量',
    value: readState.value.trustworthy ? formatQuantity(requestedQuantity.value) : UNAVAILABLE_TEXT,
    meta: readState.value.trustworthy
      ? `当前列表 ${rfqs.items.value.length} 张询价单合计`
      : readState.value.emptyMessage,
  },
])

const open = shallowRef(false)
const form = reactive({ suppliers: '', siteCode: '' })
let nextLineId = 0
function newLine() {
  return {
    id: ++nextLineId,
    skuCode: '',
    uomCode: '',
    quantity: '1',
    requiredDate: '',
    siteCode: form.siteCode,
  }
}
const lines = ref([newLine()])
watch(
  () =>
    lines.value.map((line) => ({
      id: line.id,
      sku: line.skuCode,
      uom: baseUomBySku.value.get(line.skuCode.trim()),
    })),
  (defaults, previous) => {
    const previousById = new Map(previous.map((entry) => [entry.id, entry]))
    defaults.forEach(({ id, sku, uom }, index) => {
      const before = previousById.get(id)
      if (uom && (before?.sku !== sku || before?.uom !== uom)) lines.value[index]!.uomCode = uom
    })
  },
)
const showErrors = shallowRef(false)
const supplierCodeList = computed(() =>
  form.suppliers
    .split(/[,\s]+/)
    .map((s) => s.trim())
    .filter(Boolean),
)
function applySiteToLines() {
  for (const line of lines.value) line.siteCode = form.siteCode
}
const invalid = computed(() => ({
  suppliers: supplierCodeList.value.length === 0,
}))
const invalidLines = computed(() =>
  lines.value.map((line) => ({
    skuCode: !line.skuCode.trim(),
    uomCode: !line.uomCode.trim(),
    requiredDate: !line.requiredDate,
    quantity: !Number.isFinite(Number(line.quantity)) || Number(line.quantity) <= 0,
    siteCode: !line.siteCode.trim(),
  })),
)
const draftQuantityTotal = computed(() =>
  lines.value.reduce((sum, line) => {
    const qty = Number(line.quantity)
    return sum + (Number.isFinite(qty) && qty > 0 ? qty : 0)
  }, 0),
)
const canSubmit = computed(
  () =>
    !Object.values(invalid.value).some(Boolean) &&
    invalidLines.value.every((line) => !Object.values(line).some(Boolean)),
)

function openDialog() {
  form.suppliers = ''
  form.siteCode = ''
  lines.value = [newLine()]
  showErrors.value = false
  open.value = true
}

async function submit() {
  if (!can(P.erpProcurementManage)) return
  if (rfqs.createRequestForQuotationPending.value) return
  showErrors.value = true
  if (!canSubmit.value) return
  const supplierCodes = supplierCodeList.value
  try {
    await rfqs.createRequestForQuotation({
      supplierCodes,
      lines: lines.value.map((line, index) => ({
        lineNo: String((index + 1) * 10),
        skuCode: line.skuCode.trim(),
        uomCode: line.uomCode.trim(),
        quantity: Number(line.quantity),
        requiredDate: line.requiredDate,
        siteCode: line.siteCode.trim(),
      })),
    })
    open.value = false
    notifySuccess('RFQ 已创建')
  } catch (error) {
    notifyOperationFailure(
      '发起询价失败',
      rfqs.createRequestForQuotationError.value ?? error,
      '发起询价失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="询价 RFQ"
      :breadcrumbs="[{ label: '经营管理' }, { label: '采购' }]"
      :count="readState.count"
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
        <NvButton v-if="can(P.erpProcurementManage)" size="sm" type="button" @click="openDialog">
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
      :empty-message="readState.emptyMessage"
      :error="readState.error"
      :error-message="readState.errorMessage"
      :awaiting-scope="readState.awaitingScope"
      :awaiting-scope-message="readState.awaitingScopeMessage"
      @retry="rfqs.refresh"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
    </NvDataTable>

    <NvDialog v-if="can(P.erpProcurementManage)" v-model:open="open">
      <NvDialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>新建 RFQ</NvDialogTitle>
          <NvDialogDescription class="sr-only">向供应商发起单项物料询价。</NvDialogDescription>
        </NvDialogHeader>
        <form novalidate class="grid gap-4" @submit.prevent="submit">
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
                empty-text="暂无供应商，请先在「基础数据 · 业务伙伴」维护"
                :loading="partnersPending"
                aria-label="供应商"
                :invalid="showErrors && invalid.suppliers"
                selection-empty-text="至少选择一家供应商"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-rfq-batch-site"> 批量设置工厂 </NvFieldLabel>
              <NvEntityPicker
                id="erp-rfq-batch-site"
                v-model="form.siteCode"
                :options="siteOptions"
                title="选择工厂"
                placeholder="选择询价工厂"
                empty-text="暂无工厂，请先在「基础数据 · 工厂」维护"
                :loading="sitesPending"
                aria-label="工厂"
              />
              <NvButton
                type="button"
                variant="outline"
                :disabled="!form.siteCode"
                @click="applySiteToLines"
              >
                应用到全部明细
              </NvButton>
            </NvField>
          </NvFieldGroup>
          <fieldset
            v-for="(line, index) in lines"
            :key="line.id"
            class="grid gap-3 rounded-md border p-3"
          >
            <legend class="px-1 text-sm font-medium">物料明细 {{ index + 1 }}</legend>
            <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
              <NvField>
                <NvFieldLabel :for="`erp-rfq-site-${line.id}`">
                  工厂 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvEntityPicker
                  :id="`erp-rfq-site-${line.id}`"
                  v-model="line.siteCode"
                  :options="siteOptions"
                  title="选择工厂"
                  placeholder="选择询价工厂"
                  empty-text="暂无工厂，请先在「基础数据 · 工厂」维护"
                  :loading="sitesPending"
                  aria-label="工厂"
                  :invalid="showErrors && invalidLines[index]?.siteCode"
                  :aria-describedby="
                    showErrors && invalidLines[index]?.siteCode ? 'erp-rfq-form-error' : undefined
                  "
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`erp-rfq-sku-${line.id}`">
                  物料 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <DirectoryPicker
                  :id="`erp-rfq-sku-${line.id}`"
                  v-model="line.skuCode"
                  directory-type="material"
                  creatable
                  :class="pickerInvalidClass(showErrors && invalidLines[index]?.skuCode)"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`erp-rfq-uom-${line.id}`">
                  单位 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvEntityPicker
                  :id="`erp-rfq-uom-${line.id}`"
                  v-model="line.uomCode"
                  :options="uomOptions"
                  title="选择单位"
                  placeholder="选择询价单位"
                  empty-text="暂无计量单位，请先在「基础数据 · 计量单位」维护"
                  :loading="uomsPending"
                  aria-label="单位"
                  :class="pickerInvalidClass(showErrors && invalidLines[index]?.uomCode)"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`erp-rfq-qty-${line.id}`">
                  数量 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvInput
                  :id="`erp-rfq-qty-${line.id}`"
                  v-model="line.quantity"
                  type="number"
                  step="any"
                  :invalid="showErrors && invalidLines[index]?.quantity"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`erp-rfq-date-${line.id}`">
                  需求日期 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvDatePicker
                  :id="`erp-rfq-date-${line.id}`"
                  v-model="line.requiredDate"
                  :locale="locale"
                  clearable
                  :aria-invalid="showErrors && invalidLines[index]?.requiredDate"
                  :class="pickerInvalidClass(showErrors && invalidLines[index]?.requiredDate)"
                />
              </NvField>
            </NvFieldGroup>
            <NvButton
              type="button"
              variant="outline"
              class="justify-self-start"
              :disabled="lines.length === 1"
              :aria-label="`删除第 ${index + 1} 行`"
              @click="lines.splice(index, 1)"
              >删除行</NvButton
            >
            <p
              v-if="showErrors && Object.values(invalidLines[index] ?? {}).some(Boolean)"
              role="alert"
              class="text-sm text-destructive"
            >
              请核对第 {{ index + 1 }} 行标出的字段。
            </p>
          </fieldset>
          <div class="flex flex-wrap items-center justify-between gap-2">
            <NvButton
              type="button"
              variant="outline"
              class="justify-self-start"
              @click="lines.push(newLine())"
              >添加明细</NvButton
            >
            <p class="text-sm text-muted-foreground tabular-nums">
              草稿合计：{{ lines.length }} 行 · 询价数量 {{ draftQuantityTotal }}
            </p>
          </div>
          <p
            v-if="showErrors && !canSubmit"
            id="erp-rfq-form-error"
            class="text-sm text-destructive"
            role="alert"
          >
            请选择工厂、供应商、物料和单位，填写需求日期及正数数量。
          </p>
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="rfqs.createRequestForQuotationPending.value">
              <NvSpinner v-if="rfqs.createRequestForQuotationPending.value" aria-hidden="true" />
              发起询价
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
