<script setup lang="ts">
import type { BusinessConsoleWmsOutboundOrderItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useWmsOutboundOrders } from '@/composables/useBusinessWms'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Checkbox,
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
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '出库发货' } })

const {
  outboundOrders,
  outboundError,
  outboundPending,
  refreshOutbound,
  completeOutbound,
  completeOutboundPending,
  completeOutboundError,
} = useWmsOutboundOrders()

const errorMessage = computed(() => formatError(outboundError.value ?? completeOutboundError.value))

const reviewOpen = shallowRef(false)
const pendingOrder = shallowRef<OutboundRow>()
const form = reactive({ packReviewNo: '', passed: true })
const formError = shallowRef('')

function isCompleted(row: OutboundRow) {
  return (row.status ?? '').toLowerCase() === 'completed'
}
function openReview(row: OutboundRow) {
  pendingOrder.value = row
  form.packReviewNo = ''
  form.passed = true
  formError.value = ''
  reviewOpen.value = true
}
async function submitReview() {
  const id = pendingOrder.value?.outboundOrderId
  if (!id) return
  if (!form.packReviewNo.trim()) {
    formError.value = '请输入复核单号。'
    return
  }
  try {
    await completeOutbound(id, { packReviewNo: form.packReviewNo.trim(), passed: form.passed })
    reviewOpen.value = false
    toast.success('出库复核已提交')
  } catch {
    // 失败信息由页面错误区呈现。
  }
}
const openCount = computed(
  () => outboundOrders.value.filter((r) => (r.status ?? '').toLowerCase() !== 'completed').length,
)

type OutboundRow = BusinessConsoleWmsOutboundOrderItem
const columns: DataTableColumn<OutboundRow>[] = [
  { key: 'outboundOrderNo', header: '出库单号', cellClass: 'font-medium', accessor: (r) => r.outboundOrderNo ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

function rowKey(row: OutboundRow) {
  return row.outboundOrderId ?? row.outboundOrderNo ?? '出库单'
}
function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="出库发货" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${outboundOrders.length} 张出库单`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="outboundPending" @click="refreshOutbound">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="出库单" :value="outboundOrders.length" hint="当前返回总数" />
      <SectionCard description="未完成" :value="openCount" hint="待拣货/复核/发运" />
    </SectionCards>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="outboundOrders"
      :row-key="rowKey"
      :loading="outboundPending"
      empty-message="暂无出库单。发货作业产生出库单后会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <Button
          size="sm"
          type="button"
          variant="outline"
          :aria-label="`完成复核 ${row.outboundOrderNo ?? ''}`"
          :disabled="isCompleted(row) || !row.outboundOrderId"
          @click="openReview(row)"
        >
          完成复核
        </Button>
      </template>
    </DataTable>

    <Dialog v-model:open="reviewOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>出库复核</DialogTitle>
          <DialogDescription>
            对出库单 {{ pendingOrder?.outboundOrderNo ?? '' }} 进行发货前复核。
          </DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitReview">
          <FieldGroup>
            <Field>
              <FieldLabel for="wms-pack-review-no">复核单号</FieldLabel>
              <Input id="wms-pack-review-no" v-model="form.packReviewNo" :aria-invalid="Boolean(formError)" autocomplete="off" />
              <FieldError v-if="formError" :errors="[formError]" />
            </Field>
            <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3">
              <FieldLabel for="wms-pack-passed">复核通过</FieldLabel>
              <Checkbox id="wms-pack-passed" v-model:checked="form.passed" />
            </Field>
          </FieldGroup>
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="completeOutboundPending">提交复核</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
