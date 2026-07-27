<script setup lang="ts">
import type { BusinessConsoleInventoryAvailabilityLineResponse } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricSegment, NvMetricStripCell } from '@nerv-iip/ui'
import InventoryExpiryStatusBadge from '@/components/inventory/InventoryExpiryStatusBadge.vue'
import InventoryExpirySummaryCards from '@/components/inventory/InventoryExpirySummaryCards.vue'
import { useInventoryAvailability } from '@/composables/useBusinessInventory'
import { useInventoryExpiryView } from '@/composables/useInventoryExpiryView'
import { useInventoryScopeDefaults } from '@/composables/useInventoryScope'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError } from '@/utils/notify'
import {
  formatInventoryExpiryDate,
  formatInventoryExpirySource,
  formatInventoryShelfLife,
  inventoryExpiryRowKey,
  type InventoryExpiryDisplayLine,
} from '@/utils/inventoryExpiryPresentation'
import {
  NvButton,
  NvDataTable,
  NvEntityPicker,
  NvInput,
  NvMetricRing,
  NvMetricStrip,
  NvPageHeader,
  NvPagination,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import {
  BarcodeIcon,
  ClipboardCheckIcon,
  PackageSearchIcon,
  RefreshCwIcon,
  RouteIcon,
  WarehouseIcon,
} from '@lucide/vue'
import { computed, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '批次与预留',
    requiredPermissions: ['business.inventory.ledger.read'],
  },
})

const route = useRoute()
const {
  availability,
  availabilityError,
  availabilityLines,
  availabilityPending,
  filters,
  refreshAvailability,
} = useInventoryAvailability()
const {
  expiryAlertsError,
  expiryAlertsPending,
  expiryAlertsPage,
  expiryAlertsPageSize,
  expiryAlertsSuccessful,
  expiryAlertsTotal,
  expirySummary,
  hasExpirySite,
  hasExpiryScope,
  nearExpiryOnly,
  refreshExpiryAlerts,
  toggleNearExpiryView,
  visibleExpiryAlerts,
} = useInventoryExpiryView(filters)
// 工厂给默认值、单位跟随物料——用户只需要选物料这一件事。
const { siteOptions, sitesPending, skuOptions, skusPending } = useInventoryScopeDefaults(filters)
const hasSkuSelection = computed(() => filters.skuCode.trim().length > 0)
const showScopePrompt = computed(() => !nearExpiryOnly.value && !hasSkuSelection.value)
filters.qualityStatus = undefined

watch(
  () => route.query,
  (query) => {
    const sku = firstQuery(query.skuCode) || firstQuery(query.skuId)
    const lot =
      firstQuery(query.lotNo) || firstQuery(query.batchNo) || firstQuery(query.materialLotId)
    const serial = firstQuery(query.serialNo)
    const site = firstQuery(query.siteCode)
    const location = firstQuery(query.locationCode)
    if (sku) filters.skuCode = sku
    if (lot) filters.lotNo = lot
    if (serial) filters.serialNo = serial
    if (site) filters.siteCode = site
    if (location) filters.locationCode = location
  },
  { immediate: true },
)

const qualityStatusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '可用', value: 'available' },
  { label: '待检', value: 'inspection' },
  { label: '冻结', value: 'blocked' },
  { label: '不合格', value: 'rejected' },
]
const qualityStatusFilter = computed({
  get: () => filters.qualityStatus || 'all',
  set: (value: string) => {
    filters.qualityStatus = value === 'all' ? undefined : value
  },
})

watch(availabilityError, (error) => {
  if (error && !nearExpiryOnly.value) {
    notifyError(error, '库存批次加载失败，请稍后重试。')
  }
})
const rows = computed<DisplayLine[]>(() =>
  nearExpiryOnly.value ? visibleExpiryAlerts.value : availabilityLines.value,
)
const tablePending = computed(() =>
  nearExpiryOnly.value ? expiryAlertsPending.value : availabilityPending.value,
)
const pageCount = computed(() => {
  if (showScopePrompt.value) return '请选择物料'
  if (!nearExpiryOnly.value) return `${rows.value.length} 条库存明细`
  if (!hasExpirySite.value) return '请选择工厂'
  if (!hasExpiryScope.value) return '业务上下文加载中'
  if (expiryAlertsPending.value) return '加载中'
  if (expiryAlertsError.value) return '加载失败'
  if (!expiryAlertsSuccessful.value) return '等待查询'
  return `${expiryAlertsTotal.value} 条预警明细`
})
const tableEmptyMessage = computed(() => {
  if (nearExpiryOnly.value && !hasExpirySite.value) return '请选择工厂查看效期预警批次。'
  if (nearExpiryOnly.value && !hasExpiryScope.value) return '业务上下文加载中，请稍候。'
  if (nearExpiryOnly.value && expiryAlertsError.value) return '效期预警加载失败，请稍后重试。'
  if (nearExpiryOnly.value) return '当前范围没有已过期或未来30天内到期的批次。'
  if (availabilityError.value) return '库存批次加载失败，请稍后重试。'
  return '这个物料在当前工厂没有批次记录。换个物料、库位或质量状态再查一次。'
})
const onHandQuantity = computed(() => availability.value?.onHandQuantity ?? 0)
const reservedQuantity = computed(
  () =>
    availability.value?.reservedQuantity ??
    sumQuantity(availabilityLines.value, 'reservedQuantity'),
)
const availableQuantity = computed(() => availability.value?.availableQuantity ?? 0)
const blockedQuantity = computed(() =>
  Math.max(onHandQuantity.value - availableQuantity.value - reservedQuantity.value, 0),
)
const lotCount = computed(
  () => new Set(availabilityLines.value.map((line) => line.lotNo).filter(Boolean)).size,
)
const serialCount = computed(
  () => new Set(availabilityLines.value.map((line) => line.serialNo).filter(Boolean)).size,
)
// 可用 + 预留 + 冻结 = 现存量，是真正的构成关系，所以用环形卡。
const stockSegments = computed<NvMetricSegment[]>(() => [
  { key: 'available', label: '可用', value: availableQuantity.value, tone: 'success' },
  { key: 'reserved', label: '预留', value: reservedQuantity.value, tone: 'warning' },
  { key: 'frozen', label: '冻结/其他', value: blockedQuantity.value, tone: 'danger' },
])
// 批次与序列号是「有多少个可追溯单位」，与数量不同量纲，单独一条 Strip。
const traceCells = computed<NvMetricStripCell[]>(() => [
  { key: 'lots', label: '批次数', value: lotCount.value, unit: '个' },
  { key: 'serials', label: '序列号数', value: serialCount.value, unit: '个' },
  {
    key: 'lines',
    label: '库存行',
    value: availabilityLines.value.length,
    unit: '行',
    meta: filters.uomCode ? `计量单位 ${filters.uomCode}` : undefined,
  },
])

type Line = BusinessConsoleInventoryAvailabilityLineResponse
type DisplayLine = InventoryExpiryDisplayLine
/**
 * 主要列只留下仓管据以决策的字段；单位、序列号、生产日期、保质期、效期来源、
 * 货主收进对应单元格的第二行，避免十七列表格逼出横向滚动。
 */
const columns: NvDataTableColumn<DisplayLine>[] = [
  { key: 'lotNo', header: '批次', cellClass: 'font-medium', accessor: (r) => r.lotNo ?? '无批次' },
  { key: 'skuCode', header: '物料', accessor: (r) => (r.skuCode ?? filters.skuCode) || '—' },
  {
    key: 'expiryDate',
    header: '效期',
    headerTitle: 'FEFO：预留与拣货建议优先选择更早到期的批次。',
    accessor: (r) => formatInventoryExpiryDate(r.expiryDate),
  },
  { key: 'expiryStatus', header: '效期状态' },
  { key: 'locationCode', header: '库位', width: 'w-28', accessor: (r) => r.locationCode ?? '无' },
  { key: 'qualityStatus', header: '质量状态', width: 'w-28' },
  { key: 'onHandQuantity', header: '现存量', align: 'end', width: 'w-24' },
  { key: 'reservedQuantity', header: '预留量', align: 'end', width: 'w-24' },
  { key: 'availableQuantity', header: '可用量', align: 'end', width: 'w-24' },
  { key: 'blockedQuantity', header: '冻结/其他', align: 'end', width: 'w-24' },
  { key: 'actions', header: '关联', align: 'end', width: 'w-56' },
]

function lineKey(line: DisplayLine) {
  return inventoryExpiryRowKey(line, filters.skuCode)
}

function lineContextQuery(line: DisplayLine) {
  const lotNo = line.lotNo ?? undefined
  return {
    skuCode: (line.skuCode ?? filters.skuCode) || undefined,
    siteCode: filters.siteCode || undefined,
    locationCode: line.locationCode ?? undefined,
    lotNo,
    batchNo: lotNo,
    materialLotId: lotNo,
    serialNo: line.serialNo ?? undefined,
  }
}

function traceabilityQuery(line: DisplayLine) {
  return {
    mode: 'batch',
    batchOrSerial: line.serialNo ?? line.lotNo ?? undefined,
  }
}

function barcodeQuery(line: DisplayLine) {
  const identifier = line.serialNo ?? line.lotNo ?? filters.skuCode
  return {
    sourceWorkflow: 'inventory.count',
    sourceDocumentId: identifier || undefined,
    scannedValue: identifier || undefined,
  }
}

function lineBlockedQuantity(line: DisplayLine) {
  return Math.max(
    (line.onHandQuantity ?? 0) - (line.availableQuantity ?? 0) - (line.reservedQuantity ?? 0),
    0,
  )
}

function movementBlockReason(line: DisplayLine) {
  if (line.movementAllowed === true) return ''
  return line.movementBlockReason ?? '该库存行暂不能发起移动，请稍后重试或联系管理员。'
}

function sumQuantity(lines: Line[], key: 'reservedQuantity') {
  return lines.reduce((total, line) => total + (line[key] ?? 0), 0)
}

function formatQuantity(value?: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}

function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}

async function refreshCurrentView() {
  if (nearExpiryOnly.value) await refreshExpiryAlerts()
  else await refreshAvailability()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="批次与预留" :breadcrumbs="[{ label: '库存' }]" :count="pageCount">
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          :variant="nearExpiryOnly ? 'default' : 'outline'"
          @click="toggleNearExpiryView"
        >
          效期预警（30天）
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/inventory/availability',
              query: {
                skuCode: filters.skuCode || undefined,
                siteCode: filters.siteCode || undefined,
              },
            }"
          >
            <PackageSearchIcon aria-hidden="true" />
            可用量
          </RouterLink>
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="tablePending"
          @click="refreshCurrentView"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <InventoryExpirySummaryCards v-if="nearExpiryOnly" :summary="expirySummary" />
    <!-- 还没选物料时，环形卡与批次统计都只会是 0，不摆空壳指标。 -->
    <div v-else-if="!showScopePrompt" class="grid gap-4 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
      <NvMetricRing
        label="现存量构成"
        :value="formatQuantity(onHandQuantity)"
        :center-caption="filters.uomCode ? `现存量 · ${filters.uomCode}` : '现存量'"
        :segments="stockSegments"
      />
      <NvMetricStrip :cells="traceCells" />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvEntityPicker
          v-model="filters.skuCode"
          class="w-56"
          :options="skuOptions"
          title="选择物料"
          placeholder="选择物料"
          source-text="数据来自基础数据物料主数据"
          empty-text="暂无物料主数据，请先在基础数据维护物料"
          :loading="skusPending"
          clearable
          aria-label="物料"
        />
        <!-- 单位不是独立筛选项：台账维度上由物料的基本单位决定，手输只会查不到货。 -->
        <span
          v-if="!nearExpiryOnly && filters.uomCode"
          class="inline-flex h-9 items-center rounded-md border border-input px-2.5 text-sm text-muted-foreground"
          >单位 {{ filters.uomCode }}</span
        >
        <NvEntityPicker
          v-model="filters.siteCode"
          class="w-40"
          :options="siteOptions"
          title="选择工厂"
          placeholder="选择工厂"
          source-text="数据来自基础数据工厂主数据"
          empty-text="暂无工厂主数据，请先在基础数据维护工厂"
          :loading="sitesPending"
          aria-label="工厂"
        />
        <NvInput
          v-model="filters.locationCode"
          class="h-9 w-24"
          placeholder="库位"
          aria-label="库位"
        />
        <NvInput
          v-if="!nearExpiryOnly"
          v-model="filters.lotNo"
          class="h-9 w-28"
          placeholder="批次"
          aria-label="批次"
        />
        <NvInput
          v-if="!nearExpiryOnly"
          v-model="filters.serialNo"
          class="h-9 w-28"
          placeholder="序列号"
          aria-label="序列号"
        />
        <NvSelect v-if="!nearExpiryOnly" v-model="qualityStatusFilter">
          <NvSelectTrigger class="h-9 w-28" aria-label="质量状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in qualityStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <section
      v-if="showScopePrompt"
      class="grid content-start justify-items-start gap-3 rounded-md border border-dashed border-border p-8"
    >
      <h2 class="text-base font-semibold">选择物料，查看批次、序列号与预留占用</h2>
      <p class="text-sm text-muted-foreground">
        批次台账按「物料 × 单位 × 工厂」查询，单位随所选物料自动带出，当前工厂
        {{ filters.siteCode || '未选择' }}。
      </p>
      <div class="flex flex-wrap items-center gap-2">
        <NvEntityPicker
          v-model="filters.skuCode"
          class="w-64"
          :options="skuOptions"
          title="选择物料"
          placeholder="选择物料"
          source-text="数据来自基础数据物料主数据"
          empty-text="暂无物料主数据，请先在基础数据维护物料"
          :loading="skusPending"
          aria-label="选择物料查看批次"
        />
        <NvButton type="button" variant="outline" @click="toggleNearExpiryView">
          查看效期预警（30天）
        </NvButton>
      </div>
    </section>

    <NvDataTable
      v-else
      :columns="columns"
      :rows="rows"
      :row-key="lineKey"
      :loading="tablePending"
      :searchable="false"
      :column-settings="false"
      :pagination="false"
      :empty-message="tableEmptyMessage"
    >
      <template #cell-lotNo="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ row.lotNo ?? '无批次' }}</span>
          <span class="text-xs text-muted-foreground">{{ row.serialNo ?? '无序列号' }}</span>
        </div>
      </template>
      <template #cell-skuCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ (row.skuCode ?? filters.skuCode) || '—' }}</span>
          <span class="text-xs text-muted-foreground"
            >单位 {{ (row.uomCode ?? filters.uomCode) || '—' }}</span
          >
        </div>
      </template>
      <template #cell-expiryDate="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ formatInventoryExpiryDate(row.expiryDate) }}</span>
          <span class="text-xs text-muted-foreground">
            生产 {{ formatInventoryExpiryDate(row.productionDate) }} ·
            {{ formatInventoryShelfLife(row.shelfLifeDays) }} ·
            {{ formatInventoryExpirySource(row.expiryDateSource) }}
          </span>
        </div>
      </template>
      <template #cell-locationCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ row.locationCode ?? '无' }}</span>
          <span class="text-xs text-muted-foreground"
            >货主 {{ row.ownerId ?? row.ownerType ?? '无' }}</span
          >
        </div>
      </template>
      <template #cell-qualityStatus="{ row }">
        <NvStatusBadge :value="row.qualityStatus" />
      </template>
      <template #cell-expiryStatus="{ row }">
        <InventoryExpiryStatusBadge :line="row" />
      </template>
      <template #cell-onHandQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.onHandQuantity) }}</span></template
      >
      <template #cell-reservedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.reservedQuantity) }}</span></template
      >
      <template #cell-availableQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.availableQuantity) }}</span></template
      >
      <template #cell-blockedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(lineBlockedQuantity(row)) }}</span></template
      >
      <template #cell-actions="{ row }">
        <div class="flex min-w-56 flex-col items-end gap-1">
          <p
            v-if="movementBlockReason(row)"
            data-operation-block-reason
            class="max-w-56 text-right text-xs leading-4 text-muted-foreground"
          >
            {{ movementBlockReason(row) }}
          </p>
          <div class="flex flex-wrap justify-end gap-2">
            <NvButton size="sm" variant="ghost" as-child>
              <RouterLink :to="{ path: '/mes/traceability', query: traceabilityQuery(row) }">
                <RouteIcon aria-hidden="true" />
                MES追溯
              </RouterLink>
            </NvButton>
            <NvButton size="sm" variant="ghost" as-child>
              <RouterLink :to="{ path: '/barcode/scans', query: barcodeQuery(row) }">
                <BarcodeIcon aria-hidden="true" />
                扫码
              </RouterLink>
            </NvButton>
            <NvButton v-if="row.movementAllowed === true" size="sm" variant="ghost" as-child>
              <RouterLink :to="{ path: '/wms/picking', query: lineContextQuery(row) }">
                <WarehouseIcon aria-hidden="true" />
                WMS
              </RouterLink>
            </NvButton>
            <NvButton
              v-else
              size="sm"
              variant="ghost"
              disabled
              :title="row.movementBlockReason ?? undefined"
            >
              <WarehouseIcon aria-hidden="true" />
              WMS
            </NvButton>
            <NvButton size="sm" variant="ghost" as-child>
              <RouterLink :to="{ path: '/quality/inspections', query: lineContextQuery(row) }">
                <ClipboardCheckIcon aria-hidden="true" />
                质量
              </RouterLink>
            </NvButton>
          </div>
        </div>
      </template>
    </NvDataTable>
    <NvPagination
      v-if="nearExpiryOnly && hasExpiryScope"
      v-model:page="expiryAlertsPage"
      v-model:page-size="expiryAlertsPageSize"
      :total-items="expiryAlertsTotal"
      :page-size-options="[25, 50, 100, 200]"
      :show-edges="false"
      :sibling-count="0"
      class="mt-4"
    />
  </BusinessLayout>
</template>
