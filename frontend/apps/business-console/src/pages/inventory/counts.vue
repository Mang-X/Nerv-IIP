<script setup lang="ts">
import type {
  BusinessConsoleConfirmStockCountAdjustmentRequest,
  BusinessConsoleCreateStockCountTaskRequest,
  BusinessConsoleInventoryCountTaskLineResponse,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import { useInventoryCounts } from '@/composables/useBusinessInventory'
import { useInventoryScopeDefaults } from '@/composables/useInventoryScope'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useSkuNames } from '@/composables/useSkuNames'
import {
  useWarehouseCodeCatalog,
  WAREHOUSE_CATALOG_SOURCE_TEXT,
  WAREHOUSE_LOCATION_EMPTY_TEXT,
  WAREHOUSE_LOT_EMPTY_TEXT,
  WAREHOUSE_SERIAL_EMPTY_TEXT,
} from '@/composables/useWarehouseCodeCatalog'
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
  countAdjustmentRows,
  countTaskRows,
  countTasksPending,
  countTasksTotal,
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
// 取值须落在 Inventory 服务认得的货主类型上（含别名），否则提交直接 400。
const OWNER_OPTIONS = [
  { label: '本公司', value: 'owned' },
  { label: '客户寄售', value: 'customer' },
  { label: '供应商寄售', value: 'supplier' },
  { label: '生产领用', value: 'production' },
  { label: '维修备件', value: 'maintenance' },
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

// 状态码值 → 中文标签：界面说人话，下发与过滤仍用后端码值。
const STATUS_LABELS: Record<string, string> = {
  open: '待实盘',
  'pending-approval': '待审批',
  confirmed: '已确认',
  'recount-required': '需复盘',
  cancelled: '已作废',
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
// 库位/批次/序列号后端无主数据读面，从既有台账与仓储作业记录派生可选项。
const { locationOptions, lotOptions, serialOptions, warehouseCatalogPending } =
  useWarehouseCodeCatalog()
// 物料 / 工厂 / 库位读面只回编码，名称在主数据里，按编码 join 出中文名。
const { resolveSkuName } = useSkuNames()
const { resolveLocation } = useMasterDataDisplayNames({ locations: true })

/** 「名称 编码」串，供排序与导出用；名录查不到就只有编码，不编名字。 */
function skuText(code?: string | null, fallback = '未记录') {
  const name = resolveSkuName(code)
  return name ? `${name} ${code}` : (code ?? fallback)
}
/** 「工厂 / 库位」串：库位优先显中文名，查不到就只显编码。 */
function locationLabel(siteCode?: string | null, locationCode?: string | null) {
  const location = locationCode ? (resolveLocation(locationCode) ?? locationCode) : ''
  return [siteCode, location].filter(Boolean).join(' / ') || '—'
}

const adjustmentTarget = shallowRef<BusinessConsoleInventoryCountTaskLineResponse>()
// 差异确认对象由所选任务行带出，只读展示，不做成（只读）输入框。
const adjustmentContextItems = computed(() => {
  const row = adjustmentTarget.value
  if (!row) return []
  return [
    { label: '盘点任务', value: row.countTaskCode || row.countTaskId },
    { label: '物料', value: skuText(row.skuCode) },
    { label: '库位', value: locationLabel(row.siteCode, row.locationCode) },
    { label: '批次', value: row.lotNo || '—' },
    { label: '状态', value: statusLabel(row.status) },
  ]
})
// 有待审批差异的任务上标注审批链，仓管才知道「在等谁」。
const approvalChainByCountTaskCode = computed(() =>
  Object.fromEntries(
    countAdjustmentRows.value
      .filter((row) => row.status === 'pending-approval' && row.approvalChainId)
      .map((row) => [row.countTaskCode, row.approvalChainId as string]),
  ),
)
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

type CountTaskRow = BusinessConsoleInventoryCountTaskLineResponse
const columns: NvDataTableColumn<CountTaskRow>[] = [
  { key: 'countTaskCode', header: '任务号', cellClass: 'font-medium' },
  // 物料 / 库位读面只回编码，名称在主数据里，按编码 join 出中文名。
  { key: 'skuCode', header: '物料', accessor: (r) => skuText(r.skuCode) },
  {
    key: 'location',
    header: '库位',
    accessor: (r) => locationLabel(r.siteCode, r.locationCode),
  },
  { key: 'lotNo', header: '批次', accessor: (r) => r.lotNo || '—' },
  {
    key: 'countedQuantity',
    header: '实盘',
    align: 'end',
    accessor: (r) => formatQuantity(r.countedQuantity),
  },
  {
    key: 'varianceQuantity',
    header: '差异',
    align: 'end',
    accessor: (r) => formatSignedQuantity(r.varianceQuantity),
  },
  {
    key: 'approvalChainId',
    header: '审批链',
    accessor: (r) => approvalChainByCountTaskCode.value[r.countTaskCode ?? ''] ?? '—',
  },
  { key: 'status', header: '状态', width: 'w-24', accessor: (r) => statusLabel(r.status) },
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
  // 列表来自服务端读面：mutation 成功后失效查询即可，新建的任务刷新之后仍然在。
  const taskId = response?.data?.countTaskId
  taskSheetOpen.value = false
  notifySuccess(`盘点任务 ${body.countTaskCode || taskId} 已创建`)
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
  adjustmentSheetOpen.value = false
  notifySuccess(approvalPending ? '库存调整已进入审批' : '库存调整已确认')
}

function openAdjustment(row: CountTaskRow) {
  const countTaskId = row.countTaskId ?? ''
  adjustmentTarget.value = row
  adjustmentForm.countTaskId = countTaskId
  adjustmentForm.countedQuantity = String(row.countedQuantity ?? 0)
  adjustmentForm.idempotencyKey = createAdjustmentIdempotencyKey(countTaskId)
  adjustmentSheetOpen.value = true
}
function statusLabel(status: string | undefined) {
  return status ? (STATUS_LABELS[status] ?? status) : '—'
}
function formatQuantity(value: number | null | undefined) {
  if (value === null || value === undefined) return '—'
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value)
}
function formatSignedQuantity(value: number | null | undefined) {
  if (value === null || value === undefined) return '—'
  return `${value > 0 ? '+' : ''}${formatQuantity(value)}`
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
      :count="`${countTasksTotal} 个盘点任务`"
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
      :rows="countTaskRows"
      :loading="countTasksPending"
      row-key="countTaskId"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无盘点任务。"
    >
      <template #empty>
        <p class="text-sm font-medium">暂无盘点任务</p>
        <p class="max-w-md text-sm text-muted-foreground">
          这里显示盘点任务与差异确认入口；已下发到仓库执行的盘点单在「仓储作业 · 盘点执行」跟进。
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
      <template #cell-skuCode="{ row }">
        <CodeWithNameCell :code="row.skuCode" :name="resolveSkuName(row.skuCode)" />
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`盘点操作 ${row.countTaskCode}`">
          <NvDropdownMenuItem :disabled="row.status !== 'open'" @click="openAdjustment(row)">
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
              <NvEntityPicker
                id="count-task-location"
                v-model="taskForm.locationCode"
                :options="locationOptions"
                title="选择库位"
                placeholder="选择库位"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="库位"
              />
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
              <NvEntityPicker
                id="count-task-lot"
                v-model="taskForm.lotNo"
                :options="lotOptions"
                title="选择批次"
                placeholder="选择批次"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOT_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="批次"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="count-task-serial">序列号</NvFieldLabel>
              <NvEntityPicker
                id="count-task-serial"
                v-model="taskForm.serialNo"
                :options="serialOptions"
                title="选择序列号"
                placeholder="选择序列号"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_SERIAL_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="序列号"
              />
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
