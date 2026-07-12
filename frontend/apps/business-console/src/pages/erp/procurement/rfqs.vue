<script setup lang="ts">
import type { BusinessConsoleErpRequestForQuotationItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useErpRequestsForQuotation } from '@/composables/useBusinessErp'
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
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  Spinner,
  NvStatusBadge,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { formatDate, formatError, formatQuantity } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '询价 RFQ',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

const rfqs = useErpRequestsForQuotation()
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

const open = shallowRef(false)
const form = reactive({
  suppliers: '',
  skuCode: '',
  uomCode: 'EA',
  quantity: '1',
  requiredDate: '',
})
const formError = shallowRef('')

function openDialog() {
  form.suppliers = ''
  form.skuCode = ''
  form.uomCode = 'EA'
  form.quantity = '1'
  form.requiredDate = ''
  formError.value = ''
  open.value = true
}

async function submit() {
  const quantity = Number(form.quantity)
  const supplierCodes = form.suppliers
    .split(/[,\s]+/)
    .map((s) => s.trim())
    .filter(Boolean)
  if (!supplierCodes.length || !form.skuCode.trim() || !form.uomCode.trim() || !form.requiredDate) {
    formError.value = '请填写供应商、物料、单位和需求日期。'
    return
  }
  if (!(quantity > 0)) {
    formError.value = '数量需为正数。'
    return
  }
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
    toast.success('RFQ 已创建')
  } catch {
    formError.value =
      formatError(rfqs.createRequestForQuotationError.value) || '创建失败，请稍后重试。'
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

    <NvSectionCards :columns="2">
      <NvSectionCard description="询价中" :value="openCount" hint="等待供应商报价" />
      <NvSectionCard
        description="本页询价数量"
        :value="formatQuantity(requestedQuantity)"
        hint="按当前页明细汇总"
      />
    </NvSectionCards>

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
          <NvDialogDescription
            >向一个或多个供应商发起询价，后续在供应商报价页录入回价。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField class="sm:col-span-2">
              <NvFieldLabel for="erp-rfq-suppliers">供应商</NvFieldLabel>
              <NvInput
                id="erp-rfq-suppliers"
                v-model="form.suppliers"
                autocomplete="off"
                placeholder="多个供应商用空格或逗号分隔"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-rfq-sku">物料</NvFieldLabel>
              <NvInput id="erp-rfq-sku" v-model="form.skuCode" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-rfq-uom">单位</NvFieldLabel>
              <NvInput id="erp-rfq-uom" v-model="form.uomCode" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-rfq-qty">数量</NvFieldLabel>
              <NvInput id="erp-rfq-qty" v-model="form.quantity" type="number" min="1" step="1" />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-rfq-date">需求日期</NvFieldLabel>
              <NvInput id="erp-rfq-date" v-model="form.requiredDate" type="date" />
            </NvField>
          </NvFieldGroup>
          <NvFieldError v-if="formError" :errors="[formError]" />
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="rfqs.createRequestForQuotationPending.value">
              <Spinner v-if="rfqs.createRequestForQuotationPending.value" aria-hidden="true" />
              创建
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
