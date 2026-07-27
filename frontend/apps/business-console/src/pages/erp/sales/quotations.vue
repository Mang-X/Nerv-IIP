<script setup lang="ts">
import type { BusinessConsoleErpQuotationItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpQuotations } from '@/composables/useBusinessErp'
import { useErpItemCatalog, useErpPartnerCatalog } from '@/composables/useErpPickerCatalog'
import { usePagedList } from '@/composables/usePagedList'
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
import { computed, reactive, shallowRef } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import { formatAmount, formatDate, pickerInvalidClass } from '../shared'

definePage({
  meta: { requiresAuth: true, title: '销售报价', requiredPermissions: ['business.erp.sales.read'] },
})

const quotations = useErpQuotations()
// 客户与物料从主数据目录里选，报价一开出就挂在真实客户与真实物料上。
const { customerOptions, partnersPending } = useErpPartnerCatalog()
const { skuOptions, skusPending } = useErpItemCatalog()
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
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '-' },
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
const quotationCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'pending',
    label: '待审报价',
    value: pendingApproval.value,
    unit: '单',
    meta: '草稿报价，审批后可转销售订单',
  },
  {
    key: 'amount',
    label: '报价金额',
    value: formatAmount(amount.value),
    meta: `当前列表 ${quotations.items.value.length} 张报价合计`,
  },
])

const open = shallowRef(false)
const form = reactive({
  customerCode: '',
  expiresOn: '',
  skuCode: '',
  quantity: '1',
  unitPrice: '0',
  requiredDate: '',
})
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  customerCode: !form.customerCode.trim(),
  expiresOn: !form.expiresOn,
  skuCode: !form.skuCode.trim(),
  requiredDate: !form.requiredDate,
  quantity: !(Number(form.quantity) > 0),
  unitPrice: !(Number(form.unitPrice) >= 0),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

function openDialog() {
  form.customerCode = ''
  form.expiresOn = ''
  form.skuCode = ''
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
          uomCode: 'EA',
          quantity,
          unitPrice,
          requiredDate: form.requiredDate,
        },
      ],
    })
    open.value = false
    notifySuccess('销售报价已创建')
  } catch (error) {
    notifyError(quotations.createQuotationError.value ?? error, '创建报价失败，请稍后重试。')
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
    notifyError(quotations.approveQuotationError.value ?? error, '审批报价失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="销售报价"
      :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]"
      :count="`${quotations.total.value} 张报价`"
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
      empty-message="暂无报价。可从销售机会或客户需求创建报价。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
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
            请选择客户与物料，填写有效期与需求日期，并给出正数数量与非负单价。
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
