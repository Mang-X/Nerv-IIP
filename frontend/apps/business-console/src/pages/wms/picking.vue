<script setup lang="ts">
import type { BusinessConsoleWmsWarehouseTaskItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useWmsPickingTasks } from '@/composables/useBusinessWms'
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
  StatusBadge,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '拣货任务' } })

const {
  filters,
  pickingTasks,
  pickingTasksError,
  pickingTasksPending,
  pickingTasksTotal,
  refreshPickingTasks,
  createPicking,
  createPickingPending,
  createPickingError,
} = useWmsPickingTasks()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.locationCode, () => filters.keyword],
})

// 拣货任务挂在出库单下（领料齐套 → 出库拣货扣减）。创建需绑定出库单与单行任务。
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({
  outboundOrderId: '',
  taskNo: '',
  lineNo: '',
  fromLocationCode: '',
  toLocationCode: '',
  quantity: '',
})

function openCreate() {
  createForm.outboundOrderId = ''
  createForm.taskNo = ''
  createForm.lineNo = '1'
  createForm.fromLocationCode = ''
  createForm.toLocationCode = ''
  createForm.quantity = ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (!createForm.outboundOrderId.trim() || !createForm.taskNo.trim() || !createForm.lineNo.trim()
    || !createForm.fromLocationCode.trim() || !createForm.toLocationCode.trim()) {
    createError.value = '请填写出库单、任务号、行号与起讫库位。'
    return
  }
  if (createForm.quantity !== '' && !(Number(createForm.quantity) > 0)) {
    createError.value = '拣货数量需为正数。'
    return
  }
  try {
    await createPicking(createForm.outboundOrderId.trim(), {
      taskNo: createForm.taskNo.trim(),
      lineNo: createForm.lineNo.trim(),
      fromLocationCode: createForm.fromLocationCode.trim(),
      toLocationCode: createForm.toLocationCode.trim(),
      quantity: createForm.quantity === '' ? undefined : Number(createForm.quantity),
    })
    createOpen.value = false
    toast.success('拣货任务已创建')
  } catch {
    // 失败信息由页面错误区呈现。
  }
}

const errorMessage = computed(() => formatError(pickingTasksError.value ?? createPickingError.value))

type PickingRow = BusinessConsoleWmsWarehouseTaskItem
const columns: DataTableColumn<PickingRow>[] = [
  { key: 'taskNo', header: '任务号', cellClass: 'font-medium', accessor: (r) => r.taskNo ?? r.warehouseTaskId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'sourceOrderNo', header: '来源单据', accessor: (r) => r.sourceOrderNo ?? '—' },
  { key: 'skuCode', header: '物料', accessor: (r) => r.skuCode ?? '—' },
  { key: 'location', header: '起讫库位', accessor: (r) => `${r.fromLocationCode ?? '—'} → ${r.toLocationCode ?? '—'}` },
  { key: 'quantity', header: '数量', align: 'end', accessor: (r) => formatQuantity(r.executedQuantity ?? r.plannedQuantity) },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
]

function rowKey(row: PickingRow) {
  return row.warehouseTaskId ?? row.taskNo ?? '拣货任务'
}
function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
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
    <PageHeader title="拣货任务" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${pickingTasksTotal} 个拣货任务`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="pickingTasksPending" @click="refreshPickingTasks">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Button size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建拣货任务
        </Button>
      </template>
    </PageHeader>

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.keyword" class="h-9 w-40" placeholder="任务号/物料" aria-label="关键字" />
        <Input v-model="filters.locationCode" class="h-9 w-28" placeholder="库位" aria-label="库位" />
        <Input v-model="filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="拣货任务状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="pickingTasks"
      :row-key="rowKey"
      :loading="pickingTasksPending"
      empty-message="暂无拣货任务。领料齐套或出库拣货时由系统派生，或在此手工登记。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="pickingTasksTotal" />

    <Dialog v-model:open="createOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>新建拣货任务</DialogTitle>
          <DialogDescription>从拣货库位拣出出库单所需库存，完成出库拣货扣减。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field class="sm:col-span-2">
              <FieldLabel for="wms-picking-outbound">出库单</FieldLabel>
              <Input id="wms-picking-outbound" v-model="createForm.outboundOrderId" autocomplete="off" placeholder="出库单标识" />
            </Field>
            <Field>
              <FieldLabel for="wms-picking-no">任务号</FieldLabel>
              <Input id="wms-picking-no" v-model="createForm.taskNo" autocomplete="off" />
            </Field>
            <Field>
              <FieldLabel for="wms-picking-line">行号</FieldLabel>
              <Input id="wms-picking-line" v-model="createForm.lineNo" autocomplete="off" />
            </Field>
            <Field>
              <FieldLabel for="wms-picking-from">拣货库位</FieldLabel>
              <Input id="wms-picking-from" v-model="createForm.fromLocationCode" autocomplete="off" placeholder="货架库位" />
            </Field>
            <Field>
              <FieldLabel for="wms-picking-to">目标库位</FieldLabel>
              <Input id="wms-picking-to" v-model="createForm.toLocationCode" autocomplete="off" placeholder="集货/暂存库位" />
            </Field>
            <Field>
              <FieldLabel for="wms-picking-qty">拣货数量</FieldLabel>
              <Input id="wms-picking-qty" v-model="createForm.quantity" type="number" min="0" step="any" autocomplete="off" placeholder="可选" />
            </Field>
          </FieldGroup>

          <FieldError v-if="createError" :errors="[createError]" />

          <DialogFooter show-close-button>
            <Button type="submit" :disabled="createPickingPending">创建拣货任务</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
