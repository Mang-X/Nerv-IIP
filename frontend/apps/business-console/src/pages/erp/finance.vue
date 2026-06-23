<script setup lang="ts">
import type {
  BusinessConsoleErpCostCandidateItem,
  BusinessConsoleErpJournalVoucherItem,
  BusinessConsoleErpPayableItem,
  BusinessConsoleErpReceivableItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import {
  useErpCostCandidates,
  useErpFinanceSummary,
  useErpJournalVouchers,
  useErpPayables,
  useErpReceivables,
} from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '财务' } })

const { summary, summaryError, summaryPending, refreshSummary } = useErpFinanceSummary()

const receivables = useErpReceivables()
const receivablesPaged = usePagedList(receivables.filters, {
  resetOn: [() => receivables.filters.status, () => receivables.filters.keyword],
})

const payables = useErpPayables()
const payablesPaged = usePagedList(payables.filters, {
  resetOn: [() => payables.filters.status, () => payables.filters.keyword],
})

const vouchers = useErpJournalVouchers()
const vouchersPaged = usePagedList(vouchers.filters, {
  resetOn: [() => vouchers.filters.status, () => vouchers.filters.keyword],
})

const costCandidates = useErpCostCandidates()
const costCandidatesPaged = usePagedList(costCandidates.filters, {
  resetOn: [() => costCandidates.filters.status, () => costCandidates.filters.keyword],
})

const activeTab = shallowRef<'receivables' | 'payables' | 'vouchers' | 'cost-candidates'>('receivables')

// reka-ui SelectItem 不接受空字符串 value，用 'all' 作「全部」哨兵并映射回 undefined。
function statusProxy(getStatus: () => string | undefined, setStatus: (value: string | undefined) => void) {
  return computed({
    get: () => getStatus() || 'all',
    set: (value: string) => setStatus(value === 'all' ? undefined : value),
  })
}
const receivableStatus = statusProxy(() => receivables.filters.status, (v) => { receivables.filters.status = v })
const payableStatus = statusProxy(() => payables.filters.status, (v) => { payables.filters.status = v })
const voucherStatus = statusProxy(() => vouchers.filters.status, (v) => { vouchers.filters.status = v })
const costStatus = statusProxy(() => costCandidates.filters.status, (v) => { costCandidates.filters.status = v })

const summaryErrorMessage = computed(() => formatError(summaryError.value))
function refreshActive() {
  void refreshSummary()
  if (activeTab.value === 'receivables') void receivables.refresh()
  else if (activeTab.value === 'payables') void payables.refresh()
  else if (activeTab.value === 'vouchers') void vouchers.refresh()
  else void costCandidates.refresh()
}

const receivableColumns: DataTableColumn<BusinessConsoleErpReceivableItem>[] = [
  { key: 'receivableNo', header: '应收单号', cellClass: 'font-medium', accessor: (r) => r.receivableNo ?? '—' },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '—' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '—' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  { key: 'openAmount', header: '未结', align: 'end', width: 'w-32', accessor: (r) => r.openAmount ?? 0 },
  { key: 'status', header: '状态', width: 'w-24' },
]
const payableColumns: DataTableColumn<BusinessConsoleErpPayableItem>[] = [
  { key: 'payableNo', header: '应付单号', cellClass: 'font-medium', accessor: (r) => r.payableNo ?? '—' },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '—' },
  { key: 'supplierCode', header: '供应商', accessor: (r) => r.supplierCode ?? '—' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  { key: 'openAmount', header: '未结', align: 'end', width: 'w-32', accessor: (r) => r.openAmount ?? 0 },
  { key: 'status', header: '状态', width: 'w-24' },
]
const voucherColumns: DataTableColumn<BusinessConsoleErpJournalVoucherItem>[] = [
  { key: 'voucherNo', header: '凭证号', cellClass: 'font-medium', accessor: (r) => r.voucherNo ?? '—' },
  { key: 'postingDate', header: '过账日期', width: 'w-32', accessor: (r) => formatDate(r.postingDate) },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'totalDebitAmount', header: '借方', align: 'end', width: 'w-32', accessor: (r) => r.totalDebitAmount ?? 0 },
  { key: 'totalCreditAmount', header: '贷方', align: 'end', width: 'w-32', accessor: (r) => r.totalCreditAmount ?? 0 },
]
const costCandidateColumns: DataTableColumn<BusinessConsoleErpCostCandidateItem>[] = [
  { key: 'candidateNo', header: '候选编号', cellClass: 'font-medium', accessor: (r) => r.candidateNo ?? '—' },
  { key: 'sourceType', header: '来源类型', accessor: (r) => sourceTypeLabel(r.sourceType) },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '—' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  { key: 'status', header: '状态', width: 'w-24' },
]

const SOURCE_TYPE_LABELS: Record<string, string> = {
  'production': '生产成本',
  'procurement': '采购成本',
  'maintenance': '维护成本',
  'logistics': '物流成本',
}
function sourceTypeLabel(value?: string | null) {
  return SOURCE_TYPE_LABELS[(value ?? '').toLowerCase()] ?? value ?? '—'
}

// ---- 登记应收 ----
const receivableOpen = shallowRef(false)
const receivableForm = reactive({ sourceDocumentNo: '', customerCode: '', amount: '0' })
const receivableFormError = shallowRef('')
function openReceivableDialog() {
  receivableForm.sourceDocumentNo = ''
  receivableForm.customerCode = ''
  receivableForm.amount = '0'
  receivableFormError.value = ''
  receivableOpen.value = true
}
async function submitReceivable() {
  const amount = Number(receivableForm.amount)
  if (!receivableForm.sourceDocumentNo.trim() || !receivableForm.customerCode.trim()) {
    receivableFormError.value = '请填写来源单据与客户。'
    return
  }
  if (!(amount > 0)) {
    receivableFormError.value = '金额需为正数。'
    return
  }
  try {
    await receivables.createReceivable({
      sourceDocumentNo: receivableForm.sourceDocumentNo.trim(),
      customerCode: receivableForm.customerCode.trim(),
      amount,
      currencyCode: 'CNY',
    })
    receivableOpen.value = false
    toast.success('应收账款已登记')
  } catch {
    receivableFormError.value = formatError(receivables.createReceivableError.value) || '登记失败，请稍后重试。'
  }
}

// ---- 登记应付 ----
const payableOpen = shallowRef(false)
const payableForm = reactive({ sourceDocumentNo: '', supplierCode: '', amount: '0' })
const payableFormError = shallowRef('')
function openPayableDialog() {
  payableForm.sourceDocumentNo = ''
  payableForm.supplierCode = ''
  payableForm.amount = '0'
  payableFormError.value = ''
  payableOpen.value = true
}
async function submitPayable() {
  const amount = Number(payableForm.amount)
  if (!payableForm.sourceDocumentNo.trim() || !payableForm.supplierCode.trim()) {
    payableFormError.value = '请填写来源单据与供应商。'
    return
  }
  if (!(amount > 0)) {
    payableFormError.value = '金额需为正数。'
    return
  }
  try {
    await payables.createPayable({
      sourceDocumentNo: payableForm.sourceDocumentNo.trim(),
      supplierCode: payableForm.supplierCode.trim(),
      amount,
      currencyCode: 'CNY',
    })
    payableOpen.value = false
    toast.success('应付账款已登记')
  } catch {
    payableFormError.value = formatError(payables.createPayableError.value) || '登记失败，请稍后重试。'
  }
}

// ---- 登记成本候选 ----
const costOpen = shallowRef(false)
const costForm = reactive({ sourceType: 'production', sourceDocumentNo: '', amount: '0' })
const costFormError = shallowRef('')
function openCostDialog() {
  costForm.sourceType = 'production'
  costForm.sourceDocumentNo = ''
  costForm.amount = '0'
  costFormError.value = ''
  costOpen.value = true
}
async function submitCost() {
  const amount = Number(costForm.amount)
  if (!costForm.sourceDocumentNo.trim()) {
    costFormError.value = '请填写来源单据。'
    return
  }
  if (!(amount > 0)) {
    costFormError.value = '金额需为正数。'
    return
  }
  try {
    await costCandidates.createCostCandidate({
      sourceType: costForm.sourceType,
      sourceDocumentNo: costForm.sourceDocumentNo.trim(),
      amount,
      currencyCode: 'CNY',
    })
    costOpen.value = false
    toast.success('成本候选已登记')
  } catch {
    costFormError.value = formatError(costCandidates.createCostCandidateError.value) || '登记失败，请稍后重试。'
  }
}

// ---- 过账凭证（两条借贷分录）----
const voucherOpen = shallowRef(false)
const voucherForm = reactive({
  postingDate: '',
  debitAccount: '',
  creditAccount: '',
  amount: '0',
  memo: '',
})
const voucherFormError = shallowRef('')
function openVoucherDialog() {
  voucherForm.postingDate = ''
  voucherForm.debitAccount = ''
  voucherForm.creditAccount = ''
  voucherForm.amount = '0'
  voucherForm.memo = ''
  voucherFormError.value = ''
  voucherOpen.value = true
}
async function submitVoucher() {
  const amount = Number(voucherForm.amount)
  if (!voucherForm.postingDate || !voucherForm.debitAccount.trim() || !voucherForm.creditAccount.trim() || !voucherForm.memo.trim()) {
    voucherFormError.value = '请填写过账日期、借/贷科目与摘要。'
    return
  }
  if (!(amount > 0)) {
    voucherFormError.value = '金额需为正数。'
    return
  }
  try {
    await vouchers.postVoucher({
      postingDate: voucherForm.postingDate,
      lines: [
        { accountCode: voucherForm.debitAccount.trim(), debitAmount: amount, creditAmount: 0, memo: voucherForm.memo.trim() },
        { accountCode: voucherForm.creditAccount.trim(), debitAmount: 0, creditAmount: amount, memo: voucherForm.memo.trim() },
      ],
    })
    voucherOpen.value = false
    toast.success('会计凭证已过账')
  } catch {
    voucherFormError.value = formatError(vouchers.postVoucherError.value) || '过账失败，请稍后重试。'
  }
}

function formatAmount(value?: number | null, currency = 'CNY') {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value ?? 0)
}
function formatDate(value?: string | null) {
  if (!value) return '—'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleDateString('zh-CN')
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="财务" :breadcrumbs="[{ label: '经营管理' }]" :count="`${receivables.total.value} 笔应收`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="summaryPending" @click="refreshActive">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <p v-if="summaryErrorMessage" class="text-sm text-destructive" role="alert">{{ summaryErrorMessage }}</p>

    <SectionCards :columns="4">
      <SectionCard description="应收未结" :value="formatAmount(summary?.openReceivableAmount)" hint="待收客户款项" />
      <SectionCard description="应付未结" :value="formatAmount(summary?.openPayableAmount)" hint="待付供应商款项" />
      <SectionCard description="待入账成本" :value="formatAmount(summary?.costCandidateAmount)" hint="成本候选待结转" />
      <SectionCard description="已过账凭证" :value="summary?.postedVoucherCount ?? 0" hint="累计过账凭证数" />
    </SectionCards>

    <Tabs v-model="activeTab">
      <TabsList>
        <TabsTrigger value="receivables">应收账款</TabsTrigger>
        <TabsTrigger value="payables">应付账款</TabsTrigger>
        <TabsTrigger value="vouchers">会计凭证</TabsTrigger>
        <TabsTrigger value="cost-candidates">成本候选</TabsTrigger>
      </TabsList>

      <TabsContent value="receivables" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="receivables.filters.keyword" class="h-9 w-48" placeholder="应收单号 / 客户" aria-label="应收关键字" />
            <Select v-model="receivableStatus">
              <SelectTrigger class="h-9 w-32" aria-label="应收状态"><SelectValue placeholder="全部状态" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部状态</SelectItem>
                <SelectItem value="open">未结</SelectItem>
                <SelectItem value="settled">已结清</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button size="sm" type="button" @click="openReceivableDialog">
              <PlusIcon aria-hidden="true" />
              登记应收
            </Button>
          </template>
        </Toolbar>
        <p v-if="formatError(receivables.error.value)" class="text-sm text-destructive" role="alert">{{ formatError(receivables.error.value) }}</p>
        <DataTable
          :columns="receivableColumns"
          :rows="receivables.items.value"
          :row-key="(r: BusinessConsoleErpReceivableItem) => r.receivableNo ?? r.sourceDocumentNo ?? '应收'"
          :loading="receivables.pending.value"
          empty-message="暂无应收账款。销售发货过账后会在这里生成应收。"
        >
          <template #cell-amount="{ row }"><span class="tabular-nums">{{ formatAmount(row.amount, row.currencyCode ?? 'CNY') }}</span></template>
          <template #cell-openAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.openAmount, row.currencyCode ?? 'CNY') }}</span></template>
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="receivablesPaged.page.value" v-model:page-size="receivablesPaged.pageSize.value" :total-items="receivables.total.value" />
      </TabsContent>

      <TabsContent value="payables" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="payables.filters.keyword" class="h-9 w-48" placeholder="应付单号 / 供应商" aria-label="应付关键字" />
            <Select v-model="payableStatus">
              <SelectTrigger class="h-9 w-32" aria-label="应付状态"><SelectValue placeholder="全部状态" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部状态</SelectItem>
                <SelectItem value="open">未结</SelectItem>
                <SelectItem value="settled">已结清</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button size="sm" type="button" @click="openPayableDialog">
              <PlusIcon aria-hidden="true" />
              登记应付
            </Button>
          </template>
        </Toolbar>
        <p v-if="formatError(payables.error.value)" class="text-sm text-destructive" role="alert">{{ formatError(payables.error.value) }}</p>
        <DataTable
          :columns="payableColumns"
          :rows="payables.items.value"
          :row-key="(r: BusinessConsoleErpPayableItem) => r.payableNo ?? r.sourceDocumentNo ?? '应付'"
          :loading="payables.pending.value"
          empty-message="暂无应付账款。采购收货后会在这里生成应付。"
        >
          <template #cell-amount="{ row }"><span class="tabular-nums">{{ formatAmount(row.amount, row.currencyCode ?? 'CNY') }}</span></template>
          <template #cell-openAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.openAmount, row.currencyCode ?? 'CNY') }}</span></template>
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="payablesPaged.page.value" v-model:page-size="payablesPaged.pageSize.value" :total-items="payables.total.value" />
      </TabsContent>

      <TabsContent value="vouchers" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="vouchers.filters.keyword" class="h-9 w-48" placeholder="凭证号" aria-label="凭证关键字" />
            <Select v-model="voucherStatus">
              <SelectTrigger class="h-9 w-32" aria-label="凭证状态"><SelectValue placeholder="全部状态" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部状态</SelectItem>
                <SelectItem value="posted">已过账</SelectItem>
                <SelectItem value="reversed">已冲销</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button size="sm" type="button" @click="openVoucherDialog">
              <PlusIcon aria-hidden="true" />
              过账凭证
            </Button>
          </template>
        </Toolbar>
        <p v-if="formatError(vouchers.error.value)" class="text-sm text-destructive" role="alert">{{ formatError(vouchers.error.value) }}</p>
        <DataTable
          :columns="voucherColumns"
          :rows="vouchers.items.value"
          :row-key="(r: BusinessConsoleErpJournalVoucherItem) => r.voucherNo ?? '凭证'"
          :loading="vouchers.pending.value"
          empty-message="暂无会计凭证。成本/收入过账后会在这里生成凭证。"
        >
          <template #cell-totalDebitAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalDebitAmount) }}</span></template>
          <template #cell-totalCreditAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalCreditAmount) }}</span></template>
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="vouchersPaged.page.value" v-model:page-size="vouchersPaged.pageSize.value" :total-items="vouchers.total.value" />
      </TabsContent>

      <TabsContent value="cost-candidates" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="costCandidates.filters.keyword" class="h-9 w-48" placeholder="候选编号 / 来源单据" aria-label="成本候选关键字" />
            <Select v-model="costStatus">
              <SelectTrigger class="h-9 w-32" aria-label="成本候选状态"><SelectValue placeholder="全部状态" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部状态</SelectItem>
                <SelectItem value="pending">待结转</SelectItem>
                <SelectItem value="posted">已结转</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button size="sm" type="button" @click="openCostDialog">
              <PlusIcon aria-hidden="true" />
              登记成本候选
            </Button>
          </template>
        </Toolbar>
        <p v-if="formatError(costCandidates.error.value)" class="text-sm text-destructive" role="alert">{{ formatError(costCandidates.error.value) }}</p>
        <DataTable
          :columns="costCandidateColumns"
          :rows="costCandidates.items.value"
          :row-key="(r: BusinessConsoleErpCostCandidateItem) => r.candidateNo ?? r.sourceDocumentNo ?? '成本候选'"
          :loading="costCandidates.pending.value"
          empty-message="暂无成本候选。生产/采购成本归集后会在这里生成待结转候选。"
        >
          <template #cell-amount="{ row }"><span class="tabular-nums">{{ formatAmount(row.amount, row.currencyCode ?? 'CNY') }}</span></template>
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="costCandidatesPaged.page.value" v-model:page-size="costCandidatesPaged.pageSize.value" :total-items="costCandidates.total.value" />
      </TabsContent>
    </Tabs>

    <Dialog v-model:open="receivableOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>登记应收账款</DialogTitle>
          <DialogDescription>对已发货销售单据登记客户应收。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitReceivable">
          <FieldGroup>
            <Field>
              <FieldLabel for="erp-ar-source">来源单据</FieldLabel>
              <Input id="erp-ar-source" v-model="receivableForm.sourceDocumentNo" autocomplete="off" placeholder="如 销售订单号 / 发货单号" />
            </Field>
            <Field>
              <FieldLabel for="erp-ar-customer">客户</FieldLabel>
              <Input id="erp-ar-customer" v-model="receivableForm.customerCode" autocomplete="off" placeholder="如 CUST-HENGJING" />
            </Field>
            <Field>
              <FieldLabel for="erp-ar-amount">金额（元）</FieldLabel>
              <Input id="erp-ar-amount" v-model="receivableForm.amount" type="number" min="0" step="0.01" />
            </Field>
          </FieldGroup>
          <FieldError v-if="receivableFormError" :errors="[receivableFormError]" />
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="receivables.createReceivablePending.value">
              <Spinner v-if="receivables.createReceivablePending.value" aria-hidden="true" />
              登记应收
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="payableOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>登记应付账款</DialogTitle>
          <DialogDescription>对已收货采购单据登记供应商应付。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitPayable">
          <FieldGroup>
            <Field>
              <FieldLabel for="erp-ap-source">来源单据</FieldLabel>
              <Input id="erp-ap-source" v-model="payableForm.sourceDocumentNo" autocomplete="off" placeholder="如 采购订单号 / 入库单号" />
            </Field>
            <Field>
              <FieldLabel for="erp-ap-supplier">供应商</FieldLabel>
              <Input id="erp-ap-supplier" v-model="payableForm.supplierCode" autocomplete="off" placeholder="如 SUP-XINWEI" />
            </Field>
            <Field>
              <FieldLabel for="erp-ap-amount">金额（元）</FieldLabel>
              <Input id="erp-ap-amount" v-model="payableForm.amount" type="number" min="0" step="0.01" />
            </Field>
          </FieldGroup>
          <FieldError v-if="payableFormError" :errors="[payableFormError]" />
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="payables.createPayablePending.value">
              <Spinner v-if="payables.createPayablePending.value" aria-hidden="true" />
              登记应付
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="costOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>登记成本候选</DialogTitle>
          <DialogDescription>归集待结转成本，过账后形成会计凭证。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCost">
          <FieldGroup>
            <Field>
              <FieldLabel for="erp-cc-type">来源类型</FieldLabel>
              <Select v-model="costForm.sourceType">
                <SelectTrigger id="erp-cc-type" aria-label="来源类型"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="production">生产成本</SelectItem>
                  <SelectItem value="procurement">采购成本</SelectItem>
                  <SelectItem value="maintenance">维护成本</SelectItem>
                  <SelectItem value="logistics">物流成本</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="erp-cc-source">来源单据</FieldLabel>
              <Input id="erp-cc-source" v-model="costForm.sourceDocumentNo" autocomplete="off" placeholder="如 工单号 / 采购单号" />
            </Field>
            <Field>
              <FieldLabel for="erp-cc-amount">金额（元）</FieldLabel>
              <Input id="erp-cc-amount" v-model="costForm.amount" type="number" min="0" step="0.01" />
            </Field>
          </FieldGroup>
          <FieldError v-if="costFormError" :errors="[costFormError]" />
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="costCandidates.createCostCandidatePending.value">
              <Spinner v-if="costCandidates.createCostCandidatePending.value" aria-hidden="true" />
              登记成本候选
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="voucherOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>过账会计凭证</DialogTitle>
          <DialogDescription>登记一借一贷分录并过账，借贷自动平衡。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitVoucher">
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="erp-jv-date">过账日期</FieldLabel>
              <Input id="erp-jv-date" v-model="voucherForm.postingDate" type="date" />
            </Field>
            <Field>
              <FieldLabel for="erp-jv-amount">金额（元）</FieldLabel>
              <Input id="erp-jv-amount" v-model="voucherForm.amount" type="number" min="0" step="0.01" />
            </Field>
            <Field>
              <FieldLabel for="erp-jv-debit">借方科目</FieldLabel>
              <Input id="erp-jv-debit" v-model="voucherForm.debitAccount" autocomplete="off" placeholder="如 5001 生产成本" />
            </Field>
            <Field>
              <FieldLabel for="erp-jv-credit">贷方科目</FieldLabel>
              <Input id="erp-jv-credit" v-model="voucherForm.creditAccount" autocomplete="off" placeholder="如 1403 原材料" />
            </Field>
            <Field class="sm:col-span-2">
              <FieldLabel for="erp-jv-memo">摘要</FieldLabel>
              <Input id="erp-jv-memo" v-model="voucherForm.memo" autocomplete="off" placeholder="如 结转本月生产领料成本" />
            </Field>
          </FieldGroup>
          <FieldError v-if="voucherFormError" :errors="[voucherFormError]" />
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="vouchers.postVoucherPending.value">
              <Spinner v-if="vouchers.postVoucherPending.value" aria-hidden="true" />
              过账凭证
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
