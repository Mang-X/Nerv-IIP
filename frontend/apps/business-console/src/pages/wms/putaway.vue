<script setup lang="ts">
import type { BusinessConsoleWmsWarehouseTaskItem } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useWmsPutawayTasks } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePagination,
  DataTablePro,
  DialogPro,
  DialogProClose,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  InputPro,
  PageHeader,
  StatusBadgePro,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '上架任务' } })

const {
  filters,
  putawayTasks,
  putawayTasksError,
  putawayTasksPending,
  putawayTasksTotal,
  refreshPutawayTasks,
  createPutaway,
  createPutawayPending,
  createPutawayError,
} = useWmsPutawayTasks()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.locationCode, () => filters.keyword],
})

// 上架任务挂在收货入库单下（完工入库 → 上架增量）。创建需绑定入库单与单行任务。
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({
  inboundOrderId: '',
  taskNo: '',
  lineNo: '',
  fromLocationCode: '',
  toLocationCode: '',
  quantity: '',
})

function openCreate() {
  createForm.inboundOrderId = ''
  createForm.taskNo = ''
  createForm.lineNo = '1'
  createForm.fromLocationCode = ''
  createForm.toLocationCode = ''
  createForm.quantity = ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (!createForm.inboundOrderId.trim() || !createForm.taskNo.trim() || !createForm.lineNo.trim()
    || !createForm.fromLocationCode.trim() || !createForm.toLocationCode.trim()) {
    createError.value = '请填写入库单、任务号、行号与起讫库位。'
    return
  }
  if (createForm.quantity !== '' && !(Number(createForm.quantity) > 0)) {
    createError.value = '上架数量需为正数。'
    return
  }
  try {
    await createPutaway(createForm.inboundOrderId.trim(), {
      taskNo: createForm.taskNo.trim(),
      lineNo: createForm.lineNo.trim(),
      fromLocationCode: createForm.fromLocationCode.trim(),
      toLocationCode: createForm.toLocationCode.trim(),
      quantity: createForm.quantity === '' ? undefined : Number(createForm.quantity),
    })
    createOpen.value = false
    toast.success('上架任务已创建')
  } catch {
    // 失败信息由页面错误区呈现。
  }
}

const errorMessage = computed(() => formatError(putawayTasksError.value ?? createPutawayError.value))

type PutawayRow = BusinessConsoleWmsWarehouseTaskItem
const columns: DataTableProColumn<PutawayRow>[] = [
  { key: 'taskNo', header: '任务号', cellClass: 'font-medium', accessor: (r) => r.taskNo ?? r.warehouseTaskId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'sourceOrderNo', header: '来源单据', accessor: (r) => r.sourceOrderNo ?? '—' },
  { key: 'skuCode', header: '物料', accessor: (r) => r.skuCode ?? '—' },
  { key: 'location', header: '起讫库位', accessor: (r) => `${r.fromLocationCode ?? '—'} → ${r.toLocationCode ?? '—'}` },
  { key: 'quantity', header: '数量', align: 'end', accessor: (r) => formatQuantity(r.executedQuantity ?? r.plannedQuantity) },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
]

function rowKey(row: PutawayRow) {
  return row.warehouseTaskId ?? row.taskNo ?? '上架任务'
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
    <PageHeader title="上架任务" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${putawayTasksTotal} 个上架任务`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="putawayTasksPending" @click="refreshPutawayTasks">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <ButtonPro size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建上架任务
        </ButtonPro>
      </template>
    </PageHeader>

    <Toolbar :show-search="false">
      <template #filters>
        <InputPro v-model="filters.keyword" class="h-9 w-40" placeholder="任务号/物料" aria-label="关键字" />
        <InputPro v-model="filters.locationCode" class="h-9 w-28" placeholder="库位" aria-label="库位" />
        <InputPro v-model="filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="上架任务状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
      :columns="columns"
      :rows="putawayTasks"
      :row-key="rowKey"
      :loading="putawayTasksPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无上架任务。完工入库后由系统派生，或在此手工登记。"
    >
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status" /></template>
    </DataTablePro>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="putawayTasksTotal" />

    <DialogPro v-model:open="createOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>新建上架任务</DialogProTitle>
          <DialogProDescription>将收货入库单的暂存库存移入目标库位，完成上架增量。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field class="sm:col-span-2">
              <FieldLabel for="wms-putaway-inbound">入库单</FieldLabel>
              <InputPro id="wms-putaway-inbound" v-model="createForm.inboundOrderId" autocomplete="off" placeholder="入库单标识" />
            </Field>
            <Field>
              <FieldLabel for="wms-putaway-no">任务号</FieldLabel>
              <InputPro id="wms-putaway-no" v-model="createForm.taskNo" autocomplete="off" />
            </Field>
            <Field>
              <FieldLabel for="wms-putaway-line">行号</FieldLabel>
              <InputPro id="wms-putaway-line" v-model="createForm.lineNo" autocomplete="off" />
            </Field>
            <Field>
              <FieldLabel for="wms-putaway-from">来源库位</FieldLabel>
              <InputPro id="wms-putaway-from" v-model="createForm.fromLocationCode" autocomplete="off" placeholder="暂存库位" />
            </Field>
            <Field>
              <FieldLabel for="wms-putaway-to">目标库位</FieldLabel>
              <InputPro id="wms-putaway-to" v-model="createForm.toLocationCode" autocomplete="off" placeholder="货架库位" />
            </Field>
            <Field>
              <FieldLabel for="wms-putaway-qty">上架数量</FieldLabel>
              <InputPro id="wms-putaway-qty" v-model="createForm.quantity" type="number" min="0" step="any" autocomplete="off" placeholder="可选" />
            </Field>
          </FieldGroup>

          <FieldError v-if="createError" :errors="[createError]" />

          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="createPutawayPending">创建上架任务</ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
