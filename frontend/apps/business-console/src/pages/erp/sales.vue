<script setup lang="ts">
import type { BusinessConsoleErpSalesOrderItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useErpSalesOrders } from '@/composables/useBusinessErp'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
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
  StatusBadge,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '销售订单' } })

const {
  salesOrders,
  salesOrdersError,
  salesOrdersPending,
  refreshSalesOrders,
  createSalesOrder,
  createSalesOrderPending,
  createSalesOrderError,
} = useErpSalesOrders()

const errorMessage = computed(() => formatError(salesOrdersError.value ?? createSalesOrderError.value))
const totalAmount = computed(() => salesOrders.value.reduce((sum, o) => sum + (o.totalAmount ?? 0), 0))
const openCount = computed(
  () => salesOrders.value.filter((o) => !['completed', 'closed', 'cancelled'].includes((o.status ?? '').toLowerCase())).length,
)

type SalesRow = BusinessConsoleErpSalesOrderItem
const columns: DataTableColumn<SalesRow>[] = [
  { key: 'salesOrderNo', header: '销售单号', cellClass: 'font-medium', accessor: (r) => r.salesOrderNo ?? '无' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'totalAmount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.totalAmount ?? 0 },
]

const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({ quotationNo: '', salesOrderNo: '' })

function openCreate() {
  createForm.quotationNo = ''
  createForm.salesOrderNo = ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (!createForm.quotationNo.trim()) {
    createError.value = '请输入报价单号（销售订单由已批准报价转换生成）。'
    return
  }
  try {
    await createSalesOrder({
      quotationNo: createForm.quotationNo.trim(),
      salesOrderNo: createForm.salesOrderNo.trim() || undefined,
    })
    createOpen.value = false
    toast.success('销售订单已创建')
  } catch {
    // 失败信息由页面错误区呈现。
  }
}

function rowKey(row: SalesRow) {
  return row.salesOrderNo ?? '销售订单'
}
function formatAmount(value: number) {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY', maximumFractionDigits: 2 }).format(value)
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="销售订单" :breadcrumbs="[{ label: '经营管理' }]" :count="`${salesOrders.length} 张销售订单`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="salesOrdersPending" @click="refreshSalesOrders">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Button size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建销售订单
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="销售订单" :value="salesOrders.length" hint="当前返回总数" />
      <SectionCard description="未完成" :value="openCount" hint="待发货/收款" />
      <SectionCard description="订单总额" :value="formatAmount(totalAmount)" hint="本次返回合计" />
    </SectionCards>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="salesOrders"
      :row-key="rowKey"
      :loading="salesOrdersPending"
      empty-message="暂无销售订单。报价批准并转换后会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-totalAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalAmount ?? 0) }}</span></template>
    </DataTable>

    <Dialog v-model:open="createOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>新建销售订单</DialogTitle>
          <DialogDescription>由已批准的报价转换生成销售订单，订单明细沿用报价。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <FieldGroup>
            <Field>
              <FieldLabel for="erp-so-quotation">报价单号</FieldLabel>
              <Input id="erp-so-quotation" v-model="createForm.quotationNo" autocomplete="off" />
              <FieldError v-if="createError" :errors="[createError]" />
            </Field>
            <Field>
              <FieldLabel for="erp-so-no">销售单号（可选）</FieldLabel>
              <Input id="erp-so-no" v-model="createForm.salesOrderNo" autocomplete="off" placeholder="留空由系统编号" />
            </Field>
          </FieldGroup>
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="createSalesOrderPending">创建销售订单</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
