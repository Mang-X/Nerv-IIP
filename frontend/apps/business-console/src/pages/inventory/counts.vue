<script setup lang="ts">
import type {
  BusinessConsoleConfirmStockCountAdjustmentRequest,
  BusinessConsoleCreateStockCountTaskRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useInventoryCounts } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  Spinner,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, ClipboardPlusIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '库存盘点',
    requiredPermissions: ['business.inventory.counts.manage'],
  },
})

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
const columns: NvDataTableColumn<QueueRow>[] = [
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
  const approvalPending = response?.data?.status === 'pending-approval'
  adjustmentSuccess.value = approvalPending
    ? `库存调整 ${response?.data?.approvalChainId ?? body.idempotencyKey} 已进入审批。`
    : `库存调整 ${response?.data?.movementId ?? body.idempotencyKey} 已确认。`
  countTaskQueue.value = countTaskQueue.value.map((row) =>
    row.countTaskId === adjustmentForm.countTaskId
      ? { ...row, countedQuantity: body.countedQuantity, status: approvalPending ? '待审批' : '已确认' }
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
    <NvPageHeader
      title="库存盘点"
      :breadcrumbs="[{ label: '库存' }]"
      :count="`${countTaskQueue.length} 个本次任务`"
    >
      <template #actions>
        <NvButton v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`"
            >返回工单 {{ contextWorkOrderId }}</RouterLink
          >
        </NvButton>
        <NvButton size="sm" type="button" @click="taskSheetOpen = true">
          <ClipboardPlusIcon aria-hidden="true" />
          创建盘点任务
        </NvButton>
      </template>
    </NvPageHeader>

    <NvDataTable
      :columns="columns"
      :rows="countTaskQueue"
      row-key="countTaskId"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无盘点任务。先创建盘点任务，再从任务行进入差异确认。"
    >
      <template #cell-actions="{ row }">
        <NvRowActions :label="`盘点操作 ${row.countTaskId}`">
          <NvDropdownMenuItem @click="openAdjustment(row)">
            <CheckCircle2Icon aria-hidden="true" />
            确认差异
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="taskSheetOpen">
      <NvDialogContent class="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>创建盘点任务</NvDialogTitle>
          <NvDialogDescription>指定物料、工厂、库位和批次后创建盘点任务。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitTask">
          <p v-if="taskErrorMessage" class="text-sm text-destructive" role="alert">
            {{ taskErrorMessage }}
          </p>
          <p v-if="taskSuccess" class="text-sm text-success" role="status">{{ taskSuccess }}</p>
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="count-task-sku">SKU</NvFieldLabel>
              <NvInput id="count-task-sku" v-model="taskForm.skuCode" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-uom">单位</NvFieldLabel>
              <NvInput id="count-task-uom" v-model="taskForm.uomCode" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-site">工厂</NvFieldLabel>
              <NvInput id="count-task-site" v-model="taskForm.siteCode" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-location">库位</NvFieldLabel>
              <NvInput id="count-task-location" v-model="taskForm.locationCode" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-quality">质量状态</NvFieldLabel>
              <NvInput id="count-task-quality" v-model="taskForm.qualityStatus" />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-owner-type">货主类型</NvFieldLabel>
              <NvInput id="count-task-owner-type" v-model="taskForm.ownerType" />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-owner-id">货主</NvFieldLabel>
              <NvInput
                id="count-task-owner-id"
                v-model="taskForm.ownerId"
                placeholder="可选货主名称或编码"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-lot">批次</NvFieldLabel>
              <NvInput id="count-task-lot" v-model="taskForm.lotNo" />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-serial">序列号</NvFieldLabel>
              <NvInput id="count-task-serial" v-model="taskForm.serialNo" />
            </NvField>
          </NvFieldGroup>
          <div class="flex justify-end">
            <NvButton type="submit" :disabled="createCountTaskPending || !canCreateTask">
              <Spinner v-if="createCountTaskPending" aria-hidden="true" />
              <ClipboardPlusIcon v-else aria-hidden="true" />
              创建任务
            </NvButton>
          </div>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="adjustmentSheetOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>确认盘点差异</NvDialogTitle>
          <NvDialogDescription
            >从已完成实盘的任务进入差异确认，重复提交保护由系统处理。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitAdjustment">
          <p v-if="adjustmentErrorMessage" class="text-sm text-destructive" role="alert">
            {{ adjustmentErrorMessage }}
          </p>
          <p v-if="adjustmentSuccess" class="text-sm text-success" role="status">
            {{ adjustmentSuccess }}
          </p>
          <NvFieldGroup class="grid gap-3">
            <NvField>
              <NvFieldLabel for="count-adjust-task-id">盘点任务</NvFieldLabel>
              <NvInput
                id="count-adjust-task-id"
                v-model="adjustmentForm.countTaskId"
                readonly
                required
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-adjust-quantity">实盘数量</NvFieldLabel>
              <NvInput
                id="count-adjust-quantity"
                v-model="adjustmentForm.countedQuantity"
                inputmode="decimal"
                required
                type="number"
              />
            </NvField>
          </NvFieldGroup>
          <div class="flex justify-end">
            <NvButton type="submit" :disabled="confirmAdjustmentPending || !canConfirmAdjustment">
              <Spinner v-if="confirmAdjustmentPending" aria-hidden="true" />
              <CheckCircle2Icon v-else aria-hidden="true" />
              确认调整
            </NvButton>
          </div>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
