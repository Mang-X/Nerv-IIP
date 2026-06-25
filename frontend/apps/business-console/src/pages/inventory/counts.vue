<script setup lang="ts">
import type {
  BusinessConsoleConfirmStockCountAdjustmentRequest,
  BusinessConsoleCreateStockCountTaskRequest,
} from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useInventoryCounts } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProHeader,
  DialogProTitle,
  DropdownMenuItem,
  Field,
  FieldGroup,
  FieldLabel,
  InputPro,
  PageHeader,
  RowActions,
  Spinner,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, ClipboardPlusIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '库存盘点' } })

const route = useRoute()
const {
  confirmAdjustment,
  confirmAdjustmentError,
  confirmAdjustmentPending,
  createCountTask,
  createCountTaskError,
  createCountTaskPending,
  filters,
} = useInventoryCounts()

const taskSuccess = shallowRef('')
const adjustmentSuccess = shallowRef('')
const taskSheetOpen = shallowRef(false)
const adjustmentSheetOpen = shallowRef(false)
let adjustmentKeySequence = 0

const taskForm = reactive({
  countTaskCode: '',
  skuCode: '',
  uomCode: 'EA',
  siteCode: '',
  locationCode: '',
  lotNo: '',
  serialNo: '',
  qualityStatus: 'available',
  ownerType: 'owned',
  ownerId: '',
})
const adjustmentForm = reactive({
  countTaskId: '',
  countedQuantity: '0',
  idempotencyKey: '',
})

interface CountTaskQueueRow {
  countTaskId: string
  countTaskCode: string
  skuCode: string
  siteCode: string
  locationCode: string
  status: string
  countedQuantity?: number
}

const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
watch(
  () => route.query,
  (query) => {
    const sku = firstQuery(query.skuCode) || firstQuery(query.skuId)
    if (sku) taskForm.skuCode = sku
    const site = firstQuery(query.siteCode)
    if (site) taskForm.siteCode = site
    const location = firstQuery(query.locationCode)
    if (location) taskForm.locationCode = location
    const lot = firstQuery(query.lotNo) || firstQuery(query.materialLotId)
    if (lot) taskForm.lotNo = lot
    const serial = firstQuery(query.serialNo)
    if (serial) taskForm.serialNo = serial
  },
  { immediate: true },
)

const taskErrorMessage = computed(() => formatError(createCountTaskError.value))
const adjustmentErrorMessage = computed(() => formatError(confirmAdjustmentError.value))
const countTaskQueue = shallowRef<CountTaskQueueRow[]>([])
const canCreateTask = computed(
  () =>
    isNonEmpty(filters.organizationId) &&
    isNonEmpty(filters.environmentId) &&
    isNonEmpty(taskForm.skuCode) &&
    isNonEmpty(taskForm.uomCode) &&
    isNonEmpty(taskForm.siteCode) &&
    isNonEmpty(taskForm.locationCode),
)
const canConfirmAdjustment = computed(
  () =>
    isNonEmpty(filters.organizationId) &&
    isNonEmpty(filters.environmentId) &&
    isNonEmpty(adjustmentForm.countTaskId) &&
    isNonEmpty(adjustmentForm.idempotencyKey) &&
    toOptionalNumber(adjustmentForm.countedQuantity) !== undefined,
)

type QueueRow = CountTaskQueueRow
const columns: DataTableProColumn<QueueRow>[] = [
  { key: 'countTaskId', header: '任务号', cellClass: 'font-medium' },
  { key: 'skuCode', header: '物料' },
  { key: 'location', header: '库位', accessor: (r) => `${r.siteCode} / ${r.locationCode}` },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

async function submitTask() {
  if (!canCreateTask.value) return
  const body: BusinessConsoleCreateStockCountTaskRequest = {
    organizationId: filters.organizationId.trim(),
    environmentId: filters.environmentId.trim(),
    countTaskCode: taskForm.countTaskCode.trim() || `COUNT-${Date.now()}`,
    skuCode: taskForm.skuCode.trim(),
    uomCode: taskForm.uomCode.trim(),
    siteCode: taskForm.siteCode.trim(),
    locationCode: taskForm.locationCode.trim(),
    lotNo: optionalText(taskForm.lotNo),
    serialNo: optionalText(taskForm.serialNo),
    qualityStatus: optionalText(taskForm.qualityStatus),
    ownerType: optionalText(taskForm.ownerType),
    ownerId: optionalText(taskForm.ownerId),
  }
  const response = await createCountTask(body)
  const taskId = response?.data?.countTaskId
  taskSuccess.value = `盘点任务 ${taskId ?? body.countTaskCode} 已提交。`
  countTaskQueue.value = [
    {
      countTaskId: taskId ?? body.countTaskCode ?? '待返回',
      countTaskCode: body.countTaskCode ?? '',
      skuCode: body.skuCode ?? '',
      siteCode: body.siteCode ?? '',
      locationCode: body.locationCode ?? '',
      status: '待实盘',
    },
    ...countTaskQueue.value,
  ]
  taskSheetOpen.value = false
}

async function submitAdjustment() {
  if (!canConfirmAdjustment.value) return
  const body: BusinessConsoleConfirmStockCountAdjustmentRequest = {
    countedQuantity: toOptionalNumber(adjustmentForm.countedQuantity),
    idempotencyKey: adjustmentForm.idempotencyKey.trim(),
  }
  const response = await confirmAdjustment(adjustmentForm.countTaskId.trim(), body)
  adjustmentSuccess.value = `库存调整 ${response?.data?.movementId ?? body.idempotencyKey} 已提交。`
  countTaskQueue.value = countTaskQueue.value.map((row) =>
    row.countTaskId === adjustmentForm.countTaskId
      ? { ...row, countedQuantity: body.countedQuantity, status: '已确认' }
      : row,
  )
}

function openAdjustment(row: CountTaskQueueRow) {
  adjustmentSuccess.value = ''
  adjustmentForm.countTaskId = row.countTaskId
  adjustmentForm.countedQuantity = String(row.countedQuantity ?? 0)
  adjustmentForm.idempotencyKey = createAdjustmentIdempotencyKey(row.countTaskId)
  adjustmentSheetOpen.value = true
}
function createAdjustmentIdempotencyKey(countTaskId: string) {
  adjustmentKeySequence += 1
  return `count-${countTaskId}-${Date.now()}-${adjustmentKeySequence}`
}
function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="库存盘点" :breadcrumbs="[{ label: '库存' }]" :count="`${countTaskQueue.length} 个本次任务`">
      <template #actions>
        <ButtonPro v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`">返回工单 {{ contextWorkOrderId }}</RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" @click="taskSheetOpen = true">
          <ClipboardPlusIcon aria-hidden="true" />
          创建盘点任务
        </ButtonPro>
      </template>
    </PageHeader>

    <DataTablePro
      :columns="columns"
      :rows="countTaskQueue"
      row-key="countTaskId"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无盘点任务。先创建盘点任务，再从任务行进入差异确认。"
    >
      <template #cell-actions="{ row }">
        <RowActions :label="`盘点操作 ${row.countTaskId}`">
          <DropdownMenuItem @click="openAdjustment(row)">
            <CheckCircle2Icon aria-hidden="true" />
            确认差异
          </DropdownMenuItem>
        </RowActions>
      </template>
    </DataTablePro>

    <DialogPro v-model:open="taskSheetOpen">
      <DialogProContent class="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
        <DialogProHeader>
          <DialogProTitle>创建盘点任务</DialogProTitle>
          <DialogProDescription>指定物料、工厂、库位和批次后创建盘点任务。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitTask">
          <p v-if="taskErrorMessage" class="text-sm text-destructive" role="alert">{{ taskErrorMessage }}</p>
          <p v-if="taskSuccess" class="text-sm text-success" role="status">{{ taskSuccess }}</p>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="count-task-sku">SKU</FieldLabel>
              <InputPro id="count-task-sku" v-model="taskForm.skuCode" required />
            </Field>
            <Field>
              <FieldLabel for="count-task-uom">单位</FieldLabel>
              <InputPro id="count-task-uom" v-model="taskForm.uomCode" required />
            </Field>
            <Field>
              <FieldLabel for="count-task-site">工厂</FieldLabel>
              <InputPro id="count-task-site" v-model="taskForm.siteCode" required />
            </Field>
            <Field>
              <FieldLabel for="count-task-location">库位</FieldLabel>
              <InputPro id="count-task-location" v-model="taskForm.locationCode" required />
            </Field>
            <Field>
              <FieldLabel for="count-task-quality">质量状态</FieldLabel>
              <InputPro id="count-task-quality" v-model="taskForm.qualityStatus" />
            </Field>
            <Field>
              <FieldLabel for="count-task-owner-type">货主类型</FieldLabel>
              <InputPro id="count-task-owner-type" v-model="taskForm.ownerType" />
            </Field>
            <Field>
              <FieldLabel for="count-task-owner-id">货主</FieldLabel>
              <InputPro id="count-task-owner-id" v-model="taskForm.ownerId" placeholder="可选货主名称或编码" />
            </Field>
            <Field>
              <FieldLabel for="count-task-lot">批次</FieldLabel>
              <InputPro id="count-task-lot" v-model="taskForm.lotNo" />
            </Field>
            <Field>
              <FieldLabel for="count-task-serial">序列号</FieldLabel>
              <InputPro id="count-task-serial" v-model="taskForm.serialNo" />
            </Field>
          </FieldGroup>
          <div class="flex justify-end">
            <ButtonPro type="submit" :disabled="createCountTaskPending || !canCreateTask">
              <Spinner v-if="createCountTaskPending" aria-hidden="true" />
              <ClipboardPlusIcon v-else aria-hidden="true" />
              创建任务
            </ButtonPro>
          </div>
        </form>
      </DialogProContent>
    </DialogPro>

    <DialogPro v-model:open="adjustmentSheetOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>确认盘点差异</DialogProTitle>
          <DialogProDescription>从已完成实盘的任务进入差异确认，重复提交保护由系统处理。</DialogProDescription>
        </DialogProHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitAdjustment">
          <p v-if="adjustmentErrorMessage" class="text-sm text-destructive" role="alert">{{ adjustmentErrorMessage }}</p>
          <p v-if="adjustmentSuccess" class="text-sm text-success" role="status">{{ adjustmentSuccess }}</p>
          <FieldGroup class="grid gap-3">
            <Field>
              <FieldLabel for="count-adjust-task-id">盘点任务</FieldLabel>
              <InputPro id="count-adjust-task-id" v-model="adjustmentForm.countTaskId" readonly required />
            </Field>
            <Field>
              <FieldLabel for="count-adjust-quantity">实盘数量</FieldLabel>
              <InputPro id="count-adjust-quantity" v-model="adjustmentForm.countedQuantity" inputmode="decimal" required type="number" />
            </Field>
          </FieldGroup>
          <div class="flex justify-end">
            <ButtonPro type="submit" :disabled="confirmAdjustmentPending || !canConfirmAdjustment">
              <Spinner v-if="confirmAdjustmentPending" aria-hidden="true" />
              <CheckCircle2Icon v-else aria-hidden="true" />
              确认调整
            </ButtonPro>
          </div>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
