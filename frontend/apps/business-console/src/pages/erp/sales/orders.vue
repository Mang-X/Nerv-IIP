<script setup lang="ts">
import type { BusinessConsoleErpSalesOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useErpSalesOrders } from '@/composables/useBusinessErp'
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
import { formatAmount, formatError } from '../shared'

definePage({
  meta: { requiresAuth: true, title: '销售订单', requiredPermissions: ['business.erp.sales.read'] },
})

const orders = useErpSalesOrders()
const { page, pageSize } = usePagedList(orders.filters, { resetOn: [() => orders.filters.keyword] })

const columns: NvDataTableColumn<BusinessConsoleErpSalesOrderItem>[] = [
  {
    key: 'salesOrderNo',
    header: '销售单号',
    cellClass: 'font-medium',
    accessor: (r) => r.salesOrderNo ?? '-',
  },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '-' },
  { key: 'status', header: '状态', width: 'w-28' },
  {
    key: 'totalAmount',
    header: '金额',
    align: 'end',
    width: 'w-32',
    accessor: (r) => r.totalAmount ?? 0,
  },
]

const releasedCount = computed(
  () =>
    orders.salesOrders.value.filter((o) => (o.status ?? '').toLowerCase() === 'released').length,
)
const amount = computed(() =>
  orders.salesOrders.value.reduce((sum, order) => sum + (order.totalAmount ?? 0), 0),
)

const open = shallowRef(false)
const form = reactive({ quotationNo: '', salesOrderNo: '' })
const formError = shallowRef('')

function openDialog() {
  form.quotationNo = ''
  form.salesOrderNo = ''
  formError.value = ''
  open.value = true
}

async function submit() {
  if (!form.quotationNo.trim()) {
    formError.value = '请输入已批准报价单号。'
    return
  }
  try {
    await orders.createSalesOrder({
      quotationNo: form.quotationNo.trim(),
      salesOrderNo: form.salesOrderNo.trim() || undefined,
    })
    open.value = false
    toast.success('销售订单已创建')
  } catch {
    formError.value = formatError(orders.createSalesOrderError.value) || '创建失败，请稍后重试。'
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="销售订单"
      :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]"
      :count="`${orders.salesOrdersTotal.value} 张订单`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="orders.salesOrdersPending.value"
          @click="orders.refreshSalesOrders"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openDialog">
          <PlusIcon aria-hidden="true" />
          新建订单
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="2">
      <NvSectionCard description="已释放订单" :value="releasedCount" hint="可进入发货履约" />
      <NvSectionCard description="本页订单金额" :value="formatAmount(amount)" hint="按当前页汇总" />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="orders.filters.keyword"
          class="h-9 w-64"
          placeholder="销售单号 / 客户"
          aria-label="销售订单关键字"
        />
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="orders.salesOrdersTotal.value"
      :columns="columns"
      :rows="orders.salesOrders.value"
      :row-key="(r: BusinessConsoleErpSalesOrderItem) => r.salesOrderNo ?? '销售订单'"
      :loading="orders.salesOrdersPending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无销售订单。批准报价后可在这里生成订单。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
      <template #cell-totalAmount="{ row }"
        ><span class="tabular-nums">{{ formatAmount(row.totalAmount) }}</span></template
      >
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建销售订单</NvDialogTitle>
          <NvDialogDescription>由已批准的销售报价转换生成订单。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup>
            <NvField
              ><NvFieldLabel for="erp-so-quotation">报价单号</NvFieldLabel
              ><NvInput id="erp-so-quotation" v-model="form.quotationNo" autocomplete="off"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-so-no">销售单号（可选）</NvFieldLabel
              ><NvInput id="erp-so-no" v-model="form.salesOrderNo" autocomplete="off"
            /></NvField>
          </NvFieldGroup>
          <NvFieldError v-if="formError" :errors="[formError]" />
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="orders.createSalesOrderPending.value">
              <Spinner v-if="orders.createSalesOrderPending.value" aria-hidden="true" />
              创建
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
