<script setup lang="ts">
import type {
  BusinessConsoleConfirmStockCountAdjustmentRequest,
  BusinessConsoleCreateStockCountTaskRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import { useInventoryCounts } from '@/composables/useBusinessInventory'
import { useInventoryScopeDefaults } from '@/composables/useInventoryScope'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
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
  confirmAdjustmentPending,
  createCountTask,
  createCountTaskPending,
  filters,
} = useInventoryCounts()

// 受控值：UI 说人话，下发仍是后端码值。
const QUALITY_OPTIONS = [
  { label: '可用', value: 'available' },
  { label: '待检', value: 'inspection' },
  { label: '冻结', value: 'blocked' },
  { label: '不合格', value: 'rejected' },
]
const OWNER_OPTIONS = [
  { label: '自有', value: 'owned' },
  { label: '客户', value: 'customer' },
  { label: '供应商', value: 'supplier' },
  { label: '寄售', value: 'consignment' },
]

const taskSheetOpen = shallowRef(false)
const adjustmentSheetOpen = shallowRef(false)
let adjustmentKeySequence = 0

const taskForm = reactive({
  countTaskCode: '',
  skuCode: '',
  // 单位不手填：盘点任务必须落在与库存台账完全一致的维度上，单位跟随所选物料的基本单位。
  uomCode: '',
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

// 工厂给默认值、单位跟随物料，仓管只需要选物料与库位。
const { siteOptions, sitesPending, skuOptions, skusPending } = useInventoryScopeDefaults(taskForm)

const countTaskQueue = shallowRef<CountTaskQueueRow[]>([])
const adjustmentTarget = shallowRef<CountTaskQueueRow>()
// 差异确认对象由所选任务行带出，只读展示，不做成（只读）输入框。
const adjustmentContextItems = computed(() => {
  const row = adjustmentTarget.value
  if (!row) return []
  return [
    { label: '盘点任务', value: row.countTaskCode || row.countTaskId },
    { label: '物料', value: row.skuCode },
    { label: '库位', value: [row.siteCode, row.locationCode].filter(Boolean).join(' / ') },
    { label: '状态', value: row.status },
  ]
})
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
  let response
  try {
    response = await createCountTask(body)
  } catch (error) {
    notifyError(error, '创建盘点任务失败，请稍后重试。')
    return
  }
  const taskId = response?.data?.countTaskId
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
  notifySuccess(`盘点任务 ${taskId ?? body.countTaskCode} 已创建`)
}

async function submitAdjustment() {
  if (!canConfirmAdjustment.value) return
  const body: BusinessConsoleConfirmStockCountAdjustmentRequest = {
    countedQuantity: toOptionalNumber(adjustmentForm.countedQuantity),
    idempotencyKey: adjustmentForm.idempotencyKey.trim(),
  }
  let response
  try {
    response = await confirmAdjustment(adjustmentForm.countTaskId.trim(), body)
  } catch (error) {
    notifyError(error, '确认库存调整失败，请稍后重试。')
    return
  }
  const approvalPending = response?.data?.status === 'pending-approval'
  countTaskQueue.value = countTaskQueue.value.map((row) =>
    row.countTaskId === adjustmentForm.countTaskId
      ? {
          ...row,
          countedQuantity: body.countedQuantity,
          status: approvalPending ? '待审批' : '已确认',
        }
      : row,
  )
  adjustmentSheetOpen.value = false
  notifySuccess(approvalPending ? '库存调整已进入审批' : '库存调整已确认')
}

function openAdjustment(row: CountTaskQueueRow) {
  adjustmentTarget.value = row
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
      empty-message="暂无盘点任务。"
    >
      <template #empty>
        <p class="text-sm font-medium">暂无盘点任务</p>
        <p class="max-w-md text-sm text-muted-foreground">
          这里显示本次创建的盘点任务与差异确认入口；已下发到仓库执行的盘点单在「仓储作业 ·
          盘点执行」跟进。
        </p>
        <div class="flex gap-2">
          <NvButton size="sm" type="button" @click="taskSheetOpen = true">
            <ClipboardPlusIcon aria-hidden="true" />
            创建盘点任务
          </NvButton>
          <NvButton size="sm" type="button" variant="outline" as-child>
            <RouterLink to="/wms/counts">盘点执行</RouterLink>
          </NvButton>
        </div>
      </template>
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
          <!-- 界面上不再写说明书；仅供读屏播报对象范围。 -->
          <NvDialogDescription class="sr-only"
            >按物料、工厂与库位创建盘点任务。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitTask">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="count-task-sku">物料</NvFieldLabel>
              <NvEntityPicker
                id="count-task-sku"
                v-model="taskForm.skuCode"
                :options="skuOptions"
                title="选择物料"
                placeholder="选择物料"
                source-text="数据来自基础数据物料主数据"
                empty-text="暂无物料主数据，请先在基础数据维护物料"
                :loading="skusPending"
                aria-label="物料"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-uom">单位</NvFieldLabel>
              <NvInput id="count-task-uom" v-model="taskForm.uomCode" disabled />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-site">工厂</NvFieldLabel>
              <NvEntityPicker
                id="count-task-site"
                v-model="taskForm.siteCode"
                :options="siteOptions"
                title="选择工厂"
                placeholder="选择工厂"
                source-text="数据来自基础数据工厂主数据"
                empty-text="暂无工厂主数据，请先在基础数据维护工厂"
                :loading="sitesPending"
                aria-label="工厂"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-location">库位</NvFieldLabel>
              <NvInput id="count-task-location" v-model="taskForm.locationCode" required />
            </NvField>
            <NvField>
              <NvFieldLabel>质量状态</NvFieldLabel>
              <NvSelect v-model="taskForm.qualityStatus">
                <NvSelectTrigger aria-label="质量状态"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in QUALITY_OPTIONS" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel>货主类型</NvFieldLabel>
              <NvSelect v-model="taskForm.ownerType">
                <NvSelectTrigger aria-label="货主类型"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in OWNER_OPTIONS" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
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
          <!-- 盘点对象已在下方只读区完整呈现；此处仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            盘点任务
            {{ adjustmentTarget?.countTaskCode || adjustmentForm.countTaskId }} 的差异确认。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitAdjustment">
          <CarriedContextSummary label="盘点对象" :items="adjustmentContextItems" />
          <NvFieldGroup class="grid gap-3">
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
