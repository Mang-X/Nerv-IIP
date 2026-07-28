<script setup lang="ts">
import type {
  BusinessConsoleCreateWmsCountExecutionRequest,
  BusinessConsoleWmsCountExecutionItem,
} from '@nerv-iip/api-client'
import { statusActionGate } from '@nerv-iip/business-core'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import { wmsStatusTone } from '@/data/businessLabels'
import { hasBusinessContext } from '@/composables/businessContextBinding'
import {
  isIndeterminateLifecycleWriteError,
  recoverLifecycleAction,
} from '@/composables/lifecycleAction'
import { createWmsIdempotencyKey, useWmsCountExecutions } from '@/composables/useBusinessWms'
import { useInventoryScopeCatalog } from '@/composables/useInventoryScope'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import {
  useWarehouseCodeCatalog,
  WAREHOUSE_CATALOG_SOURCE_TEXT,
  WAREHOUSE_LOCATION_EMPTY_TEXT,
} from '@/composables/useWarehouseCodeCatalog'
import {
  wmsWarehouseTaskStatusFilterOptions,
  wmsWarehouseTaskStatusLabel,
  WMS_STATUS_ANY,
} from '@/data/wmsReference'
import { usePagedList } from '@/composables/usePagedList'
import { useSkuNames } from '@/composables/useSkuNames'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
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
  NvDropdownMenuItem,
  NvField,
  NvFieldDescription,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvEntityPicker,
  NvInput,
  NvSearchSelect,
  NvMetricCard,
  NvMetricStrip,
  NvPageHeader,
  NvRowActions,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '盘点执行',
    requiredPermissions: ['business.wms.receipts.read'],
  },
})

const {
  countExecutions,
  countExecutionsError,
  countExecutionsPending,
  countExecutionsTotal,
  refreshCountExecutions,
  createCountExecution,
  createCountExecutionPending,
  completeCountExecution,
  completeCountExecutionPending,
  filters,
} = useWmsCountExecutions()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.locationCode],
})
// 物料 / 单位 / 工厂走主数据目录；库位后端无读面，从既有台账与作业记录派生。
const { skuOptions, skusPending, siteOptions, sitesPending, resolveUomCode } =
  useInventoryScopeCatalog()
const { locationOptions, warehouseCatalogPending } = useWarehouseCodeCatalog()
// 状态是后端枚举而不是目录，用哨兵值表达「全部」。
const statusFilter = computed({
  get: () => filters.status || WMS_STATUS_ANY,
  set: (value: string) => {
    filters.status = value === WMS_STATUS_ANY ? undefined : value
  },
})
/** 单位随物料的基本单位带出，不给手输：盘点单位写错就核不上账。 */
function onCountSkuChange(skuCode: string) {
  createForm.skuCode = skuCode
  createForm.uomCode = skuCode ? resolveUomCode(skuCode) : ''
}

function isOpen(row: BusinessConsoleWmsCountExecutionItem) {
  return statusActionGate({
    domain: 'wms-count',
    action: 'complete',
    facts: { status: row.status },
  }).executable
}
function hasVariance(row: BusinessConsoleWmsCountExecutionItem) {
  return typeof row.varianceQuantity === 'number' && row.varianceQuantity !== 0
}

// 待盘点 / 有差异 是可行动语义指标（驱动复盘与库存调整），非机械总数。
// 口径：这两个只能按**当前页**的行算，标签一律带「本页」，不与服务端总数混在一起。
const pendingCount = computed(() => countExecutions.value.filter(isOpen).length)
const varianceCount = computed(() => countExecutions.value.filter(hasVariance).length)

/**
 * 「账实不符」告警卡不许在读不到数时下结论：上下文未就绪 / 读取中 / 读失败时
 * `varianceCount` 恒为 0，直接渲染就会告诉仓管「账实一致」——把故障说成绿灯。
 * 非就绪一律值显 `—`、状态说取不到、脚注说清无法判断。
 */
const contextReady = computed(() => hasBusinessContext(filters))
const varianceCardReady = computed(
  () => contextReady.value && !countExecutionsError.value && !countExecutionsPending.value,
)
const varianceCardStatus = computed(() => {
  if (!varianceCardReady.value) return { label: '取不到数据', tone: 'neutral' as const }
  return varianceCount.value > 0
    ? { label: '待复盘', tone: 'danger' as const }
    : { label: '本页账实一致', tone: 'success' as const }
})
const varianceCardNote = computed(() => {
  if (!varianceCardReady.value) return '盘点单读不到，账实是否一致无法判断，请重试。'
  return varianceCount.value > 0
    ? '差异单需复盘确认后再做库存调整，否则账面数量会一直不准。'
    : '本页盘点单账面与实盘一致。'
})

const createOpen = shallowRef(false)
const createForm = reactive({
  countNo: '',
  skuCode: '',
  uomCode: 'EA',
  siteCode: '',
  locationCode: '',
  expectedQuantity: '',
})
const createError = shallowRef('')

const completeOpen = shallowRef(false)
const completeTarget = shallowRef<BusinessConsoleWmsCountExecutionItem>()
const completeForm = reactive({ countedQuantity: '' })
const completeError = shallowRef('')
const completeIntentKey = shallowRef('')
const completeIntentAttempted = shallowRef(false)
const completeIntentLocked = shallowRef(false)
const completeFrozenPayload = shallowRef<{ countedQuantity: number }>()
watch(
  () => completeForm.countedQuantity,
  () => {
    if (!completeIntentAttempted.value || completeIntentLocked.value) return
    completeIntentKey.value = createWmsIdempotencyKey()
    completeIntentAttempted.value = false
    completeFrozenPayload.value = undefined
    completeError.value = ''
  },
)

const listErrorMessage = computed(() => formatError(countExecutionsError.value))
// 弹窗内只留字段级校验汇总；提交失败一律 toast，不留常驻错误条。
const createErrorMessage = computed(() => createError.value)
const completeErrorMessage = computed(() => completeError.value)

/**
 * 盘点单号缺失时的说法。以前拿 countExecutionId（GUID）尾 8 位拼一个 `CNT-XXXXXXXX`
 * 冒充单号——那是编造出来的、系统里查不到的号，改为如实说明缺号。
 */
const MISSING_COUNT_NO = '无盘点单号'

// 盘点单读面只回编码（SKU-… / WH-…），名称在主数据里，按编码 join 出中文名。
const { resolveSkuName } = useSkuNames()
const { resolveLocation } = useMasterDataDisplayNames({ locations: true })

/** 「名称 编码」串，供排序与导出用；名录查不到就只有编码，不编名字。 */
function skuText(code?: string | null, fallback = '—') {
  const name = resolveSkuName(code)
  return name ? `${name} ${code}` : (code ?? fallback)
}
/** 「工厂 / 库位」串：库位优先显中文名，查不到就只显编码。 */
function locationLabel(row: { siteCode?: string | null; locationCode?: string | null }) {
  const location = row.locationCode ? (resolveLocation(row.locationCode) ?? row.locationCode) : ''
  return [row.siteCode, location].filter(Boolean).join(' / ') || '—'
}
function statusLabel(value?: string | null) {
  return wmsWarehouseTaskStatusLabel(value)
}

type CountRow = BusinessConsoleWmsCountExecutionItem
const columns: NvDataTableColumn<CountRow>[] = [
  {
    key: 'countNo',
    header: '盘点单号',
    cellClass: 'font-medium',
    accessor: (r) => r.countNo ?? MISSING_COUNT_NO,
  },
  {
    key: 'location',
    header: '库位',
    accessor: (r) => locationLabel(r),
  },
  {
    key: 'skuCode',
    header: '物料',
    accessor: (r) => skuText(r.skuCode),
  },
  { key: 'inventoryContext', header: '库存上下文', width: 'w-72' },
  {
    key: 'expectedQuantity',
    header: '账面',
    align: 'end',
    accessor: (r) => formatQuantity(r.expectedQuantity),
  },
  {
    key: 'countedQuantity',
    header: '实盘',
    align: 'end',
    accessor: (r) => formatQuantity(r.countedQuantity),
  },
  { key: 'varianceQuantity', header: '差异', align: 'end' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function rowKey(row: CountRow) {
  return row.countExecutionId ?? row.countNo ?? '盘点执行'
}
function formatQuantity(value?: number | null) {
  if (value === undefined || value === null) return '—'
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value)
}
function varianceLabel(value?: number | null) {
  if (value === undefined || value === null) return '—'
  const formatted = formatQuantity(Math.abs(value))
  if (value > 0) return `+${formatted}`
  if (value < 0) return `-${formatted}`
  return '0'
}

function openCreate() {
  createForm.countNo = ''
  createForm.skuCode = ''
  createForm.uomCode = 'EA'
  createForm.siteCode = ''
  createForm.locationCode = ''
  createForm.expectedQuantity = ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (
    !createForm.countNo.trim() ||
    !createForm.skuCode.trim() ||
    !createForm.siteCode.trim() ||
    !createForm.locationCode.trim()
  ) {
    createError.value = '请填写盘点单号、物料、工厂与库位。'
    return
  }
  const expected =
    createForm.expectedQuantity === '' ? undefined : Number(createForm.expectedQuantity)
  if (expected !== undefined && !(expected >= 0)) {
    createError.value = '账面数量需为非负数。'
    return
  }
  const body: BusinessConsoleCreateWmsCountExecutionRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    countNo: createForm.countNo.trim(),
    skuCode: createForm.skuCode.trim(),
    uomCode: createForm.uomCode.trim() || 'EA',
    siteCode: createForm.siteCode.trim(),
    locationCode: createForm.locationCode.trim(),
    expectedQuantity: expected,
  }
  try {
    await createCountExecution(body)
    createOpen.value = false
    notifySuccess('盘点单已创建')
  } catch (error) {
    notifyError(error, '创建盘点单失败，请稍后重试。')
  }
}

function openComplete(row: CountRow) {
  completeTarget.value = row
  completeIntentKey.value = createWmsIdempotencyKey()
  completeIntentAttempted.value = false
  completeIntentLocked.value = false
  completeFrozenPayload.value = undefined
  // 缺省值：已录实盘 → 沿用；否则用账面数量打底，仓管只需改差异行。
  const defaultQuantity = row.countedQuantity ?? row.expectedQuantity
  completeForm.countedQuantity = defaultQuantity != null ? String(defaultQuantity) : ''
  completeError.value = ''
  completeOpen.value = true
}
function onCompleteOpenChange(open: boolean) {
  if (!open && completeIntentLocked.value) return
  completeOpen.value = open
}

// 盘点对象由所选行带出，只读展示，不做成输入框。
const completeContextItems = computed(() => {
  const row = completeTarget.value
  if (!row) return []
  return [
    { label: '盘点单号', value: row.countNo ?? MISSING_COUNT_NO },
    { label: '库位', value: locationLabel(row) },
    {
      label: '物料',
      value: skuText(row.skuCode),
    },
    {
      label: '账面数量',
      value: row.expectedQuantity == null ? undefined : formatQuantity(row.expectedQuantity),
    },
  ]
})
async function submitComplete() {
  const target = completeTarget.value
  if (!target?.countExecutionId) return
  if (completeForm.countedQuantity === '') {
    completeError.value = '请填写实盘数量。'
    return
  }
  const counted = Number(completeForm.countedQuantity)
  if (!(counted >= 0)) {
    completeError.value = '实盘数量需为非负数。'
    return
  }
  try {
    const payload = completeFrozenPayload.value ?? { countedQuantity: counted }
    completeFrozenPayload.value = payload
    await completeCountExecution(
      target.countExecutionId,
      payload.countedQuantity,
      completeIntentKey.value,
      {
        attempt: completeIntentAttempted.value ? 'retry' : 'initial',
        onCommandAttempt: () => {
          completeIntentAttempted.value = true
        },
      },
    )
    completeOpen.value = false
    completeIntentKey.value = ''
    completeIntentAttempted.value = false
    completeIntentLocked.value = false
    completeFrozenPayload.value = undefined
    notifySuccess(`盘点单 ${target.countNo ?? MISSING_COUNT_NO} 已完成`)
  } catch (error) {
    if (
      await recoverLifecycleAction(error, {
        reset: () => {
          completeOpen.value = false
          completeTarget.value = undefined
          completeForm.countedQuantity = ''
          completeError.value = ''
          completeIntentKey.value = ''
          completeIntentAttempted.value = false
          completeIntentLocked.value = false
          completeFrozenPayload.value = undefined
        },
        refresh: refreshCountExecutions,
        notify: (message) => notifyError(message),
      })
    ) {
      return
    }
    completeIntentLocked.value =
      completeIntentAttempted.value && isIndeterminateLifecycleWriteError(error)
    completeError.value = completeIntentLocked.value
      ? '提交结果未知，当前内容已锁定；仅可按原内容重试。'
      : ''
    notifyError(error, '完成盘点失败，请稍后重试。')
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="盘点执行"
      :breadcrumbs="[{ label: '仓储作业' }]"
      :count="`${countExecutionsTotal} 张盘点单`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="countExecutionsPending"
          @click="refreshCountExecutions"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建盘点单
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
      <NvMetricStrip
        :cells="[
          { key: 'total', label: '盘点单', value: countExecutionsTotal, unit: '张' },
          { key: 'pending', label: '本页待实盘录入', value: pendingCount, unit: '张' },
          {
            key: 'variance',
            label: '本页账实不符',
            value: varianceCount,
            unit: '张',
            valueTone: varianceCount > 0 ? 'danger' : undefined,
          },
        ]"
      />
      <NvMetricCard
        variant="alert"
        label="本页账实不符"
        :value="varianceCardReady ? varianceCount : '—'"
        :unit="varianceCardReady ? '张' : undefined"
        :tone="varianceCardReady && varianceCount > 0 ? 'danger' : 'neutral'"
        :status="varianceCardStatus"
        :foot-start="varianceCardNote"
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvEntityPicker
          v-model="filters.locationCode"
          class="w-36"
          :options="locationOptions"
          title="选择库位"
          placeholder="库位"
          :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
          :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
          :loading="warehouseCatalogPending"
          clearable
          aria-label="库位"
        />
        <NvSearchSelect
          v-model="statusFilter"
          class="w-32"
          :options="wmsWarehouseTaskStatusFilterOptions"
          placeholder="全部状态"
          aria-label="盘点状态"
        />
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="countExecutionsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="countExecutions"
      :row-key="rowKey"
      :loading="countExecutionsPending"
      :searchable="false"
      :column-settings="false"
      :error="countExecutionsError"
      :error-message="listErrorMessage"
      :awaiting-scope="!contextReady"
      awaiting-scope-message="请先在顶部选择业务范围，再查看盘点单。"
      empty-message="暂无盘点单。"
      @retry="refreshCountExecutions"
    >
      <template #empty>
        <p class="text-sm font-medium">暂无盘点单</p>
        <p class="max-w-md text-sm text-muted-foreground">
          盘点单由仓管按库位 × 物料发起；日常收发货不会自动产生盘点单。
        </p>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建盘点单
        </NvButton>
      </template>
      <template #cell-varianceQuantity="{ row }">
        <span :class="hasVariance(row) ? 'font-medium text-warning' : 'text-muted-foreground'">{{
          varianceLabel(row.varianceQuantity)
        }}</span>
      </template>
      <template #cell-inventoryContext="{ row }">
        <WmsInventoryContextPanel
          compact
          :sku-code="row.skuCode"
          :uom-code="row.uomCode"
          :site-code="row.siteCode"
          :location-code="row.locationCode"
          source-workflow="inventory.count"
          source-label="扫码记录"
          :source-document-id="row.countNo ?? row.countExecutionId"
          gap-message="本页暂不显示冻结、预留与批次序列号明细，请到库存批次与预留页按盘点范围查看账面数据。"
        />
      </template>
      <template #cell-skuCode="{ row }">
        <CodeWithNameCell :code="row.skuCode" :name="resolveSkuName(row.skuCode)" fallback="—" />
      </template>
      <template #cell-status="{ row }"
        ><NvStatusBadge
          :value="row.status"
          :label="statusLabel(row.status)"
          :tone="wmsStatusTone(row.status)"
      /></template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`盘点操作 ${row.countNo ?? MISSING_COUNT_NO}`">
          <NvDropdownMenuItem :disabled="!isOpen(row)" @click="openComplete(row)">
            <CheckCircle2Icon aria-hidden="true" />
            完成盘点
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="createOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建盘点单</NvDialogTitle>
          <!-- 界面上不再写说明书；仅供读屏播报对象范围。 -->
          <NvDialogDescription class="sr-only">按库位与物料登记盘点单。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="cnt-no">盘点单号</NvFieldLabel>
              <NvInput
                id="cnt-no"
                v-model="createForm.countNo"
                autocomplete="off"
                placeholder="如 CNT-2026-0003"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="cnt-sku">物料</NvFieldLabel>
              <NvEntityPicker
                id="cnt-sku"
                :model-value="createForm.skuCode"
                :options="skuOptions"
                title="选择物料"
                placeholder="选择物料"
                source-text="数据来自基础数据物料主数据"
                empty-text="暂无物料主数据，请先在基础数据维护物料"
                :loading="skusPending"
                clearable
                aria-label="物料"
                @update:model-value="onCountSkuChange"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="cnt-site">工厂</NvFieldLabel>
              <NvEntityPicker
                id="cnt-site"
                v-model="createForm.siteCode"
                :options="siteOptions"
                title="选择工厂"
                placeholder="选择工厂"
                source-text="数据来自基础数据工厂主数据"
                empty-text="暂无工厂主数据，请先在基础数据维护工厂"
                :loading="sitesPending"
                clearable
                aria-label="工厂"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="cnt-location">库位</NvFieldLabel>
              <NvEntityPicker
                id="cnt-location"
                v-model="createForm.locationCode"
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
              <NvFieldLabel for="cnt-uom">单位</NvFieldLabel>
              <!-- 单位随物料的基本单位带出，不给手输：盘点单位写错就核不上账。 -->
              <span
                id="cnt-uom"
                class="inline-flex h-9 items-center rounded-md border border-input px-2.5 text-sm text-muted-foreground"
                >{{ createForm.uomCode || '选择物料后自动带出' }}</span
              >
            </NvField>
            <NvField>
              <NvFieldLabel for="cnt-expected">账面数量</NvFieldLabel>
              <NvInput
                id="cnt-expected"
                v-model="createForm.expectedQuantity"
                type="number"
                min="0"
                step="any"
                :disabled="completeIntentLocked"
              />
              <!-- 非显而易见的业务口径：留空的取值来源。 -->
              <NvFieldDescription>留空则按库存台账取账面数量。</NvFieldDescription>
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="createErrorMessage" :errors="[createErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createCountExecutionPending">
              <Spinner v-if="createCountExecutionPending" aria-hidden="true" />
              创建盘点单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog :open="completeOpen" @update:open="onCompleteOpenChange">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>完成盘点</NvDialogTitle>
          <!-- 盘点对象已在下方只读区完整呈现；此处仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            {{
              completeTarget
                ? `盘点单 ${completeTarget.countNo ?? MISSING_COUNT_NO} 的实盘录入。`
                : '实盘数量录入。'
            }}
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitComplete">
          <CarriedContextSummary label="盘点对象" :items="completeContextItems" />
          <NvFieldGroup class="grid gap-3">
            <NvField>
              <NvFieldLabel for="cnt-counted">实盘数量</NvFieldLabel>
              <NvInput
                id="cnt-counted"
                v-model="completeForm.countedQuantity"
                type="number"
                min="0"
                step="any"
                :disabled="completeIntentLocked"
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="completeErrorMessage" :errors="[completeErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline" :disabled="completeIntentLocked">
                取消
              </NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="completeCountExecutionPending">
              <Spinner v-if="completeCountExecutionPending" aria-hidden="true" />
              <CheckCircle2Icon v-else aria-hidden="true" />
              {{ completeIntentLocked ? '按原内容重试' : '完成盘点' }}
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
