<script setup lang="ts">
import type { BusinessConsoleErpPayableItem, BusinessConsoleErpReceivableItem } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useErpPayables, useErpReceivables } from '@/composables/useBusinessErp'
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
  FieldPro,
  FieldProError,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
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
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { formatAmount, formatError } from '../shared'

definePage({ meta: { requiresAuth: true, title: 'AR/AP', requiredPermissions: ['business.erp.finance.read'] } })

const receivables = useErpReceivables()
const payables = useErpPayables()
const receivablesPaged = usePagedList(receivables.filters, { resetOn: [() => receivables.filters.status, () => receivables.filters.keyword] })
const payablesPaged = usePagedList(payables.filters, { resetOn: [() => payables.filters.status, () => payables.filters.keyword] })

const receivableStatus = computed({
  get: () => receivables.filters.status || 'all',
  set: (value: string) => { receivables.filters.status = value === 'all' ? undefined : value },
})
const payableStatus = computed({
  get: () => payables.filters.status || 'all',
  set: (value: string) => { payables.filters.status = value === 'all' ? undefined : value },
})

const receivableColumns: DataTableProColumn<BusinessConsoleErpReceivableItem>[] = [
  { key: 'receivableNo', header: '应收单号', cellClass: 'font-medium', accessor: (r) => r.receivableNo ?? '-' },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '-' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '-' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  { key: 'openAmount', header: '未结', align: 'end', width: 'w-32', accessor: (r) => r.openAmount ?? 0 },
  { key: 'status', header: '状态', width: 'w-24' },
]
const payableColumns: DataTableProColumn<BusinessConsoleErpPayableItem>[] = [
  { key: 'payableNo', header: '应付单号', cellClass: 'font-medium', accessor: (r) => r.payableNo ?? '-' },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '-' },
  { key: 'supplierCode', header: '供应商', accessor: (r) => r.supplierCode ?? '-' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  { key: 'openAmount', header: '未结', align: 'end', width: 'w-32', accessor: (r) => r.openAmount ?? 0 },
  { key: 'status', header: '状态', width: 'w-24' },
]

const receivableAmount = computed(() => receivables.items.value.reduce((sum, r) => sum + (r.openAmount ?? 0), 0))
const payableAmount = computed(() => payables.items.value.reduce((sum, r) => sum + (r.openAmount ?? 0), 0))

const receivableOpen = shallowRef(false)
const payableOpen = shallowRef(false)
const receivableForm = reactive({ sourceDocumentNo: '', customerCode: '', amount: '0' })
const payableForm = reactive({ sourceDocumentNo: '', supplierCode: '', amount: '0' })
const formError = shallowRef('')

function openReceivableDialog() {
  receivableForm.sourceDocumentNo = ''
  receivableForm.customerCode = ''
  receivableForm.amount = '0'
  formError.value = ''
  receivableOpen.value = true
}
function openPayableDialog() {
  payableForm.sourceDocumentNo = ''
  payableForm.supplierCode = ''
  payableForm.amount = '0'
  formError.value = ''
  payableOpen.value = true
}

async function submitReceivable() {
  const amount = Number(receivableForm.amount)
  if (!receivableForm.sourceDocumentNo.trim() || !receivableForm.customerCode.trim() || !(amount > 0)) {
    formError.value = '请填写来源单据、客户和正数金额。'
    return
  }
  try {
    await receivables.createReceivable({ sourceDocumentNo: receivableForm.sourceDocumentNo.trim(), customerCode: receivableForm.customerCode.trim(), amount, currencyCode: 'CNY' })
    receivableOpen.value = false
    toast.success('应收已登记')
  } catch {
    formError.value = formatError(receivables.createReceivableError.value) || '登记失败，请稍后重试。'
  }
}

async function submitPayable() {
  const amount = Number(payableForm.amount)
  if (!payableForm.sourceDocumentNo.trim() || !payableForm.supplierCode.trim() || !(amount > 0)) {
    formError.value = '请填写来源单据、供应商和正数金额。'
    return
  }
  try {
    await payables.createPayable({ sourceDocumentNo: payableForm.sourceDocumentNo.trim(), supplierCode: payableForm.supplierCode.trim(), amount, currencyCode: 'CNY' })
    payableOpen.value = false
    toast.success('应付已登记')
  } catch {
    formError.value = formatError(payables.createPayableError.value) || '登记失败，请稍后重试。'
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="AR/AP" :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]" :count="`${receivables.total.value} 笔应收 / ${payables.total.value} 笔应付`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" @click="() => { receivables.refresh(); payables.refresh() }">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <ButtonPro size="sm" type="button" @click="openReceivableDialog"><PlusIcon aria-hidden="true" />登记应收</ButtonPro>
        <ButtonPro size="sm" type="button" @click="openPayableDialog"><PlusIcon aria-hidden="true" />登记应付</ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="应收未结" :value="formatAmount(receivableAmount)" hint="当前筛选页未收金额" />
      <SectionCard description="应付未结" :value="formatAmount(payableAmount)" hint="当前筛选页未付金额" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <InputPro v-model="receivables.filters.keyword" class="h-9 w-48" placeholder="应收单号 / 客户" aria-label="应收关键字" />
        <SelectPro v-model="receivableStatus">
          <SelectProTrigger class="h-9 w-32" aria-label="应收状态"><SelectProValue placeholder="全部状态" /></SelectProTrigger>
          <SelectProContent><SelectProItem value="all">全部应收</SelectProItem><SelectProItem value="open">未结</SelectProItem><SelectProItem value="settled">已结清</SelectProItem></SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>
    <DataTablePro
      manual
      :page="receivablesPaged.page.value"
      :page-size="receivablesPaged.pageSize.value"
      :total-items="receivables.total.value"
      :columns="receivableColumns"
      :rows="receivables.items.value"
      :row-key="(r: BusinessConsoleErpReceivableItem) => r.receivableNo ?? r.sourceDocumentNo ?? '应收'"
      :loading="receivables.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无应收账款。"
      @update:page="receivablesPaged.page.value = $event"
      @update:page-size="(v) => (receivablesPaged.pageSize.value = String(v))"
    >
      <template #cell-amount="{ row }"><span class="tabular-nums">{{ formatAmount(row.amount, row.currencyCode ?? 'CNY') }}</span></template>
      <template #cell-openAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.openAmount, row.currencyCode ?? 'CNY') }}</span></template>
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status ?? '-'" /></template>
    </DataTablePro>

    <Toolbar :show-search="false">
      <template #filters>
        <InputPro v-model="payables.filters.keyword" class="h-9 w-48" placeholder="应付单号 / 供应商" aria-label="应付关键字" />
        <SelectPro v-model="payableStatus">
          <SelectProTrigger class="h-9 w-32" aria-label="应付状态"><SelectProValue placeholder="全部状态" /></SelectProTrigger>
          <SelectProContent><SelectProItem value="all">全部应付</SelectProItem><SelectProItem value="open">未结</SelectProItem><SelectProItem value="settled">已结清</SelectProItem></SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>
    <DataTablePro
      manual
      :page="payablesPaged.page.value"
      :page-size="payablesPaged.pageSize.value"
      :total-items="payables.total.value"
      :columns="payableColumns"
      :rows="payables.items.value"
      :row-key="(r: BusinessConsoleErpPayableItem) => r.payableNo ?? r.sourceDocumentNo ?? '应付'"
      :loading="payables.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无应付账款。"
      @update:page="payablesPaged.page.value = $event"
      @update:page-size="(v) => (payablesPaged.pageSize.value = String(v))"
    >
      <template #cell-amount="{ row }"><span class="tabular-nums">{{ formatAmount(row.amount, row.currencyCode ?? 'CNY') }}</span></template>
      <template #cell-openAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.openAmount, row.currencyCode ?? 'CNY') }}</span></template>
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status ?? '-'" /></template>
    </DataTablePro>

    <DialogPro v-model:open="receivableOpen">
      <DialogProContent>
        <DialogProHeader><DialogProTitle>登记应收</DialogProTitle><DialogProDescription>对销售发货或其他收入来源登记客户应收。</DialogProDescription></DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitReceivable">
          <FieldProGroup><FieldPro><FieldProLabel for="erp-ar-source">来源单据</FieldProLabel><InputPro id="erp-ar-source" v-model="receivableForm.sourceDocumentNo" /></FieldPro><FieldPro><FieldProLabel for="erp-ar-customer">客户</FieldProLabel><InputPro id="erp-ar-customer" v-model="receivableForm.customerCode" /></FieldPro><FieldPro><FieldProLabel for="erp-ar-amount">金额（元）</FieldProLabel><InputPro id="erp-ar-amount" v-model="receivableForm.amount" type="number" min="0" step="0.01" /></FieldPro></FieldProGroup>
          <FieldProError v-if="formError" :errors="[formError]" />
          <DialogProFooter><DialogProClose as-child><ButtonPro type="button" variant="outline">取消</ButtonPro></DialogProClose><ButtonPro type="submit" :disabled="receivables.createReceivablePending.value"><Spinner v-if="receivables.createReceivablePending.value" />登记</ButtonPro></DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>

    <DialogPro v-model:open="payableOpen">
      <DialogProContent>
        <DialogProHeader><DialogProTitle>登记应付</DialogProTitle><DialogProDescription>对采购收货或其他供应商来源登记应付。</DialogProDescription></DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitPayable">
          <FieldProGroup><FieldPro><FieldProLabel for="erp-ap-source">来源单据</FieldProLabel><InputPro id="erp-ap-source" v-model="payableForm.sourceDocumentNo" /></FieldPro><FieldPro><FieldProLabel for="erp-ap-supplier">供应商</FieldProLabel><InputPro id="erp-ap-supplier" v-model="payableForm.supplierCode" /></FieldPro><FieldPro><FieldProLabel for="erp-ap-amount">金额（元）</FieldProLabel><InputPro id="erp-ap-amount" v-model="payableForm.amount" type="number" min="0" step="0.01" /></FieldPro></FieldProGroup>
          <FieldProError v-if="formError" :errors="[formError]" />
          <DialogProFooter><DialogProClose as-child><ButtonPro type="button" variant="outline">取消</ButtonPro></DialogProClose><ButtonPro type="submit" :disabled="payables.createPayablePending.value"><Spinner v-if="payables.createPayablePending.value" />登记</ButtonPro></DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
