<script setup lang="ts">
import type { BusinessConsoleErpQuotationItem } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useErpQuotations } from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePro,
  DialogPro,
  DialogProClose,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DropdownMenuProItem,
  FieldPro,
  FieldProError,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { formatAmount, formatDate, formatError } from '../shared'

definePage({ meta: { requiresAuth: true, title: '销售报价', requiredPermissions: ['business.erp.sales.read'] } })

const quotations = useErpQuotations()
const { page, pageSize } = usePagedList(quotations.filters, { resetOn: [() => quotations.filters.status, () => quotations.filters.keyword] })
const statusFilter = computed({
  get: () => quotations.filters.status || 'all',
  set: (value: string) => { quotations.filters.status = value === 'all' ? undefined : value },
})

const columns: DataTableProColumn<BusinessConsoleErpQuotationItem>[] = [
  { key: 'quotationNo', header: '报价单号', cellClass: 'font-medium', accessor: (r) => r.quotationNo ?? '-' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '-' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'expiresOn', header: '有效期至', width: 'w-32', accessor: (r) => formatDate(r.expiresOn) },
  { key: 'totalAmount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.totalAmount ?? 0 },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

const pendingApproval = computed(() => quotations.items.value.filter((q) => (q.status ?? '').toLowerCase() === 'draft').length)
const amount = computed(() => quotations.items.value.reduce((sum, q) => sum + (q.totalAmount ?? 0), 0))

const open = shallowRef(false)
const form = reactive({ customerCode: '', expiresOn: '', skuCode: '', quantity: '1', unitPrice: '0', requiredDate: '' })
const formError = shallowRef('')

function openDialog() {
  form.customerCode = ''
  form.expiresOn = ''
  form.skuCode = ''
  form.quantity = '1'
  form.unitPrice = '0'
  form.requiredDate = ''
  formError.value = ''
  open.value = true
}

async function submit() {
  const quantity = Number(form.quantity)
  const unitPrice = Number(form.unitPrice)
  if (!form.customerCode.trim() || !form.expiresOn || !form.skuCode.trim() || !form.requiredDate) {
    formError.value = '请填写客户、有效期、物料与需求日期。'
    return
  }
  if (!(quantity > 0) || !(unitPrice >= 0)) {
    formError.value = '数量需为正数、单价不可为负。'
    return
  }
  try {
    await quotations.createQuotation({
      customerCode: form.customerCode.trim(),
      expiresOn: form.expiresOn,
      lines: [{ lineNo: '10', skuCode: form.skuCode.trim(), uomCode: 'EA', quantity, unitPrice, requiredDate: form.requiredDate }],
    })
    open.value = false
    toast.success('销售报价已创建')
  } catch {
    formError.value = formatError(quotations.createQuotationError.value) || '创建失败，请稍后重试。'
  }
}

function isApprovable(row: BusinessConsoleErpQuotationItem) {
  return (row.status ?? '').toLowerCase() === 'draft'
}

async function approve(row: BusinessConsoleErpQuotationItem) {
  if (!row.quotationNo || !isApprovable(row)) return
  try {
    await quotations.approveQuotation(row.quotationNo)
    toast.success(`报价单 ${row.quotationNo} 已审批`)
  } catch {
    toast.error(formatError(quotations.approveQuotationError.value) || '审批失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="销售报价" :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]" :count="`${quotations.total.value} 张报价`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="quotations.pending.value" @click="quotations.refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <ButtonPro size="sm" type="button" @click="openDialog">
          <PlusIcon aria-hidden="true" />
          新建报价
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="待审报价" :value="pendingApproval" hint="Draft 状态，可审批后转订单" />
      <SectionCard description="本页报价金额" :value="formatAmount(amount)" hint="按当前页汇总" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <InputPro v-model="quotations.filters.keyword" class="h-9 w-56" placeholder="报价单号 / 客户" aria-label="报价单关键字" />
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="报价单状态"><SelectProValue placeholder="全部状态" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部状态</SelectProItem>
            <SelectProItem value="Draft">待审</SelectProItem>
            <SelectProItem value="Approved">已批准</SelectProItem>
            <SelectProItem value="Rejected">已拒绝</SelectProItem>
            <SelectProItem value="Expired">已过期</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <DataTablePro
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
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status ?? '-'" /></template>
      <template #cell-totalAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalAmount) }}</span></template>
      <template #cell-actions="{ row }">
        <RowActions v-if="isApprovable(row)" :label="`报价单操作 ${row.quotationNo ?? ''}`">
          <DropdownMenuProItem :disabled="quotations.approveQuotationPending.value" @click="approve(row)">
            <CheckCircle2Icon aria-hidden="true" />
            审批通过
          </DropdownMenuProItem>
        </RowActions>
        <span v-else class="text-muted-foreground">-</span>
      </template>
    </DataTablePro>

    <DialogPro v-model:open="open">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>新建销售报价</DialogProTitle>
          <DialogProDescription>报价审批通过后，可在销售订单页转为订单。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <FieldProGroup class="grid gap-3 sm:grid-cols-2">
            <FieldPro><FieldProLabel for="erp-quo-customer">客户</FieldProLabel><InputPro id="erp-quo-customer" v-model="form.customerCode" autocomplete="off" /></FieldPro>
            <FieldPro><FieldProLabel for="erp-quo-expires">有效期至</FieldProLabel><InputPro id="erp-quo-expires" v-model="form.expiresOn" type="date" /></FieldPro>
            <FieldPro><FieldProLabel for="erp-quo-sku">物料</FieldProLabel><InputPro id="erp-quo-sku" v-model="form.skuCode" autocomplete="off" /></FieldPro>
            <FieldPro><FieldProLabel for="erp-quo-required">需求日期</FieldProLabel><InputPro id="erp-quo-required" v-model="form.requiredDate" type="date" /></FieldPro>
            <FieldPro><FieldProLabel for="erp-quo-qty">数量</FieldProLabel><InputPro id="erp-quo-qty" v-model="form.quantity" type="number" min="1" step="1" /></FieldPro>
            <FieldPro><FieldProLabel for="erp-quo-price">单价（元）</FieldProLabel><InputPro id="erp-quo-price" v-model="form.unitPrice" type="number" min="0" step="0.01" /></FieldPro>
          </FieldProGroup>
          <FieldProError v-if="formError" :errors="[formError]" />
          <DialogProFooter>
            <DialogProClose as-child><ButtonPro type="button" variant="outline">取消</ButtonPro></DialogProClose>
            <ButtonPro type="submit" :disabled="quotations.createQuotationPending.value">
              <Spinner v-if="quotations.createQuotationPending.value" aria-hidden="true" />
              创建
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
