<script setup lang="ts">
import type { BusinessConsoleErpQuotationItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpQuotations } from '@/composables/useBusinessErp'
import { useErpItemCatalog, useErpPartnerCatalog } from '@/composables/useErpPickerCatalog'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import { usePagedList } from '@/composables/usePagedList'
import PartnerNameCell from '@/components/erp/PartnerNameCell.vue'
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
  NvDropdownMenuItem,
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  UNAVAILABLE_TEXT,
  erpReadState,
  formatAmount,
  formatDate,
  pickerInvalidClass,
  readCount,
} from '../shared'

definePage({
  meta: { requiresAuth: true, title: '销售报价', requiredPermissions: ['business.erp.sales.read'] },
})

const quotations = useErpQuotations()
// 客户与物料从主数据目录里选，报价一开出就挂在真实客户与真实物料上。
const { customerOptions, partnersPending } = useErpPartnerCatalog()
const { skuOptions, skusPending, uomOptions, uomsPending, baseUomBySku } = useErpItemCatalog()
// 列表侧另需 code→name 反查（目录只给下拉选项，不做反查）；底层同一份查询，不会重复请求。
const { resolvePartner } = useBusinessPartnerNames()
const { page, pageSize } = usePagedList(quotations.filters, {
  resetOn: [() => quotations.filters.status, () => quotations.filters.keyword],
})
const statusFilter = computed({
  get: () => quotations.filters.status || 'all',
  set: (value: string) => {
    quotations.filters.status = value === 'all' ? undefined : value
  },
})

const columns: NvDataTableColumn<BusinessConsoleErpQuotationItem>[] = [
  {
    key: 'quotationNo',
    header: '报价单号',
    cellClass: 'font-medium',
    accessor: (r) => r.quotationNo ?? '-',
  },
  {
    key: 'customerCode',
    header: '客户',
    accessor: (r) => resolvePartner(r.customerCode) ?? r.customerCode ?? '-',
  },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'expiresOn', header: '有效期至', width: 'w-32', accessor: (r) => formatDate(r.expiresOn) },
  {
    key: 'totalAmount',
    header: '金额',
    align: 'end',
    width: 'w-32',
    accessor: (r) => r.totalAmount ?? 0,
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

const pendingApproval = computed(
  () => quotations.items.value.filter((q) => (q.status ?? '').toLowerCase() === 'draft').length,
)
const amount = computed(() =>
  quotations.items.value.reduce((sum, q) => sum + (q.totalAmount ?? 0), 0),
)
const readState = computed(() =>
  erpReadState({
    noun: '报价单',
    unit: '张',
    ready: quotations.ready.value,
    pending: quotations.pending.value,
    error: quotations.error.value,
    total: quotations.total.value,
    filtered: Boolean(quotations.filters.keyword || quotations.filters.status),
    emptyHint: '还没有报价单。可从销售机会或客户需求创建报价。',
  }),
)

const quotationCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'pending',
    label: '待审报价',
    value: readCount(readState.value, pendingApproval.value),
    unit: readState.value.trustworthy ? '单' : '',
    meta: readState.value.trustworthy
      ? '草稿报价，审批后可转销售订单'
      : readState.value.emptyMessage,
  },
  {
    key: 'amount',
    label: '报价金额',
    value: readState.value.trustworthy ? formatAmount(amount.value) : UNAVAILABLE_TEXT,
    meta: readState.value.trustworthy
      ? `当前列表 ${quotations.items.value.length} 张报价合计`
      : readState.value.emptyMessage,
  },
])

const open = shallowRef(false)
const form = reactive({
  customerCode: '',
  expiresOn: '',
  skuCode: '',
  uomCode: '',
  quantity: '1',
  unitPrice: '0',
  requiredDate: '',
})
// 报价单位默认跟随物料的基本单位；用户仍可改成销售包装单位。
// 曾踩坑：这里写死一个通用单位，遇到按 kg / l 计量的物料，后端单位换算找不到换算关系直接 500。
watch(
  () => form.skuCode,
  (skuCode) => {
    const baseUom = baseUomBySku.value.get(skuCode.trim())
    if (baseUom) form.uomCode = baseUom
  },
)
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  customerCode: !form.customerCode.trim(),
  expiresOn: !form.expiresOn,
  skuCode: !form.skuCode.trim(),
  uomCode: !form.uomCode.trim(),
  requiredDate: !form.requiredDate,
  quantity: !(Number(form.quantity) > 0),
  unitPrice: !(Number(form.unitPrice) >= 0),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

function openDialog() {
  form.customerCode = ''
  form.expiresOn = ''
  form.skuCode = ''
  form.uomCode = ''
  form.quantity = '1'
  form.unitPrice = '0'
  form.requiredDate = ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  showErrors.value = true
  if (!canSubmit.value) return
  const quantity = Number(form.quantity)
  const unitPrice = Number(form.unitPrice)
  try {
    await quotations.createQuotation({
      customerCode: form.customerCode.trim(),
      expiresOn: form.expiresOn,
      lines: [
        {
          lineNo: '10',
          skuCode: form.skuCode.trim(),
          uomCode: form.uomCode.trim(),
          quantity,
          unitPrice,
          requiredDate: form.requiredDate,
        },
      ],
    })
    open.value = false
    notifySuccess('销售报价已创建')
  } catch (error) {
    notifyOperationFailure(
      '创建报价失败',
      quotations.createQuotationError.value ?? error,
      '创建报价失败，请稍后重试。',
    )
  }
}

function isApprovable(row: BusinessConsoleErpQuotationItem) {
  return (row.status ?? '').toLowerCase() === 'draft'
}

async function approve(row: BusinessConsoleErpQuotationItem) {
  if (!row.quotationNo || !isApprovable(row)) return
  try {
    await quotations.approveQuotation(row.quotationNo)
    notifySuccess(`报价单 ${row.quotationNo} 已审批`)
  } catch (error) {
    notifyOperationFailure(
      '审批报价失败',
      quotations.approveQuotationError.value ?? error,
      '审批报价失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="销售报价"
      :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]"
      :count="readState.count"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="quotations.pending.value"
          @click="quotations.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openDialog">
          <PlusIcon aria-hidden="true" />
          新建报价
        </NvButton>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="quotationCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="quotations.filters.keyword"
          class="h-9 w-56"
          placeholder="报价单号 / 客户"
          aria-label="报价单关键字"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="报价单状态"
            ><NvSelectValue placeholder="全部状态"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部状态</NvSelectItem>
            <NvSelectItem value="Draft">待审</NvSelectItem>
            <NvSelectItem value="Approved">已批准</NvSelectItem>
            <NvSelectItem value="Rejected">已拒绝</NvSelectItem>
            <NvSelectItem value="Expired">已过期</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="quotations.total.value"
      :columns="columns"
      :rows="quotations.items.value"
      :row-key="(r: BusinessConsoleErpQuotationItem) => r.quotationNo ?? '销售报价'"
      :loading="quotations.pending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="readState.emptyMessage"
      :error="readState.error"
      :error-message="readState.errorMessage"
      :awaiting-scope="readState.awaitingScope"
      :awaiting-scope-message="readState.awaitingScopeMessage"
      @retry="quotations.refresh"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-customerCode="{ row }">
        <PartnerNameCell :code="row.customerCode" />
      </template>
      <template #cell-status="{ row }">
        <div class="flex items-center gap-1.5">
          <NvStatusBadge :value="row.status ?? '-'" />
          <!-- 已转出报价：标注既有订单号；再次转订单后端会幂等返回这张单，不会新建。 -->
          <span
            v-if="row.convertedSalesOrderNo"
            class="text-xs text-muted-foreground"
            :title="`该报价已转出为销售订单 ${row.convertedSalesOrderNo}，重复转订单将返回同一张单`"
          >
            已转 {{ row.convertedSalesOrderNo }}
          </span>
        </div>
      </template>
      <template #cell-totalAmount="{ row }"
        ><span class="tabular-nums">{{ formatAmount(row.totalAmount) }}</span></template
      >
      <template #cell-actions="{ row }">
        <NvRowActions v-if="isApprovable(row)" :label="`报价单操作 ${row.quotationNo ?? ''}`">
          <NvDropdownMenuItem
            :disabled="quotations.approveQuotationPending.value"
            @click="approve(row)"
          >
            <CheckCircle2Icon aria-hidden="true" />
            审批通过
          </NvDropdownMenuItem>
        </NvRowActions>
        <span v-else class="text-muted-foreground">-</span>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建销售报价</NvDialogTitle>
          <NvDialogDescription class="sr-only">向客户报出单项物料价格。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="erp-quo-customer">
                客户 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-quo-customer"
                v-model="form.customerCode"
                :options="customerOptions"
                title="选择客户"
                placeholder="选择客户"
                source-text="数据来自基础数据业务伙伴（客户角色）"
                empty-text="暂无客户，请先在「基础数据 · 业务伙伴」维护"
                :loading="partnersPending"
                aria-label="客户"
                :class="pickerInvalidClass(showErrors && invalid.customerCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-quo-expires">
                有效期至 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-quo-expires"
                v-model="form.expiresOn"
                type="date"
                :data-invalid="showErrors && invalid.expiresOn ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-quo-sku">
                物料 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-quo-sku"
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
              <NvFieldLabel for="erp-quo-uom">
                单位 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-quo-uom"
                v-model="form.uomCode"
                :options="uomOptions"
                title="选择单位"
                placeholder="选择报价单位"
                source-text="数据来自基础数据计量单位；选定物料后默认带出基本单位"
                empty-text="暂无计量单位，请先在「基础数据 · 计量单位」维护"
                :loading="uomsPending"
                aria-label="单位"
                :class="pickerInvalidClass(showErrors && invalid.uomCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-quo-required">
                需求日期 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-quo-required"
                v-model="form.requiredDate"
                type="date"
                :data-invalid="showErrors && invalid.requiredDate ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-quo-qty">
                数量 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-quo-qty"
                v-model="form.quantity"
                type="number"
                min="1"
                step="1"
                :data-invalid="showErrors && invalid.quantity ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-quo-price">
                单价（元） <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-quo-price"
                v-model="form.unitPrice"
                type="number"
                min="0"
                step="0.01"
                :data-invalid="showErrors && invalid.unitPrice ? '' : undefined"
              />
            </NvField>
          </NvFieldGroup>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请选择客户、物料与单位，填写有效期与需求日期，并给出正数数量与非负单价。
          </p>
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="quotations.createQuotationPending.value">
              <Spinner v-if="quotations.createQuotationPending.value" aria-hidden="true" />
              创建报价
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
