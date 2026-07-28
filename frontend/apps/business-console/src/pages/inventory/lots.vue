<script setup lang="ts">
import type { BusinessConsoleInventoryAvailabilityLineResponse } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricSegment, NvMetricStripCell } from '@nerv-iip/ui'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import InventoryExpiryStatusBadge from '@/components/inventory/InventoryExpiryStatusBadge.vue'
import InventoryExpirySummaryCards from '@/components/inventory/InventoryExpirySummaryCards.vue'
import {
  labelFor,
  normalizeCode,
  STOCK_LEDGER_OWNER_TYPE_LABELS,
  QUALITY_STATUS_LABELS,
  STOCK_LEDGER_QUALITY_STATUS_TONES,
} from '@/data/businessLabels'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import { useInventoryAvailability } from '@/composables/useBusinessInventory'
import { useInventoryExpiryView } from '@/composables/useInventoryExpiryView'
import { useInventoryScopeDefaults } from '@/composables/useInventoryScope'
import { useInventorySiteStockOverview } from '@/composables/useInventorySiteStock'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useSkuNames } from '@/composables/useSkuNames'
import {
  useWarehouseCodeCatalog,
  WAREHOUSE_LOCATION_EMPTY_TEXT,
  WAREHOUSE_LOT_EMPTY_TEXT,
  WAREHOUSE_SERIAL_EMPTY_TEXT,
} from '@/composables/useWarehouseCodeCatalog'
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
// 读面只回编码（RM-BAR-01 / WH-WB-A-01 / pcs），名称在主数据里，前端按编码 join 出中文名。
const { resolveSkuName } = useSkuNames()
const { formatUom, resolveLocation } = useMasterDataDisplayNames({ locations: true, uoms: true })
const { resolvePartner } = useBusinessPartnerNames()
const hasSkuSelection = computed(() => filters.skuCode.trim().length > 0)
const showSiteOverview = computed(() => !nearExpiryOnly.value && !hasSkuSelection.value)
// 首屏的全厂批次台账：库存域没有全量读面，按物料目录并发扫描后聚合，覆盖范围如实标注。
const {
  refreshSiteStock,
  scanMoreSiteStock,
  siteStockError,
  siteStockFailedCount,
  siteStockHasMore,
  siteStockScannedCount,
  siteStockScanning,
  siteStockTotalSkuCount,
  siteStockTrackedLines,
} = useInventorySiteStockOverview(() => filters.siteCode)
// 库位/批次/序列号后端无主数据读面，从台账与仓储作业记录派生可选项。
const {
  locationOptions,
  locationSourceText,
  lotOptions,
  lotSourceText,
  serialOptions,
  serialSourceText,
  warehouseCatalogPending,
} = useWarehouseCodeCatalog(() => rows.value)
const siteStockCoverageText = computed(() => {
  const base = `已扫描 ${siteStockScannedCount.value}/${siteStockTotalSkuCount.value} 个物料`
  return siteStockFailedCount.value > 0
    ? `${base}，其中 ${siteStockFailedCount.value} 个读取失败`
    : base
})
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
watch(siteStockError, (error) => {
  if (error) notifyError(error, '本厂批次台账读取失败，请稍后重试。')
})
/**
 * 首屏不该要求先选物料：没选物料时铺**全厂批次台账**（扫描得到的逐行结果里
 * 挑出带批次/序列号的可追溯单元），选了物料才收窄到该物料。
 * 两种来源的行形状一致（都带 skuCode），所以下面的列定义不用分叉。
 */
const rows = computed<DisplayLine[]>(() => {
  if (nearExpiryOnly.value) return visibleExpiryAlerts.value
  return hasSkuSelection.value ? availabilityLines.value : siteStockTrackedLines.value
})
const tablePending = computed(() => {
  if (nearExpiryOnly.value) return expiryAlertsPending.value
  if (!hasSkuSelection.value) return siteStockScanning.value && rows.value.length === 0
  return availabilityPending.value
})
const siteSelected = computed(() => (filters.siteCode ?? '').trim().length > 0)
/**
 * 计数三态：数不出来的时候绝不说「0 条」——0 是一个结论，只有真的查成功才配下。
 * 未选范围 / 加载中 / 失败各说各的，别让一次 500 长得跟「本厂真没有批次」一样。
 */
const pageCount = computed(() => {
  if (nearExpiryOnly.value) {
    if (!hasExpirySite.value) return '请选择工厂'
    if (!hasExpiryScope.value) return '业务上下文加载中'
    if (expiryAlertsError.value) return '读取失败'
    if (expiryAlertsPending.value) return '加载中'
    if (!expiryAlertsSuccessful.value) return '等待查询'
    return `${expiryAlertsTotal.value} 条预警明细`
  }
  if (!siteSelected.value) return '请选择工厂'
  if (showSiteOverview.value) {
    // 扫描是增量的：已经扫出行了就照实报数，个别物料失败在下方覆盖范围里单独交代。
    if (rows.value.length === 0 && siteStockError.value) return '读取失败'
    if (rows.value.length === 0 && siteStockScanning.value) return '扫描中'
    return `${rows.value.length} 条全厂批次`
  }
  if (availabilityError.value) return '读取失败'
  if (availabilityPending.value) return '加载中'
  return `${rows.value.length} 条库存明细`
})
/** 表格错误态：读失败就说读失败，不许伪装成「没有数据」。 */
const tableError = computed(() => {
  if (nearExpiryOnly.value) return expiryAlertsError.value
  // 全厂扫描是增量的，已经扫出行就不整表报错——失败数在覆盖范围文案里如实交代。
  if (showSiteOverview.value) return rows.value.length === 0 ? siteStockError.value : undefined
  return availabilityError.value
})
const tableErrorMessage = computed(() => {
  if (nearExpiryOnly.value) return '效期预警读取失败，现在无法判断有没有临期批次。'
  if (showSiteOverview.value) return '本厂批次台账读取失败，现在无法判断本厂有没有批次。'
  return '库存批次读取失败，现在无法判断这个物料有没有批次。'
})
/** 还没形成查询条件时的中性态——什么结论都不下。 */
const tableAwaitingScope = computed(() => {
  if (nearExpiryOnly.value) return !hasExpirySite.value || !hasExpiryScope.value
  return !siteSelected.value
})
const tableAwaitingScopeMessage = computed(() => {
  if (nearExpiryOnly.value && !hasExpirySite.value) return '请选择工厂查看效期预警批次。'
  if (nearExpiryOnly.value) return '业务上下文加载中，请稍候。'
  return '请先选择工厂，再查本厂批次。'
})
/** 走到这里才是真的查成功且 0 条。 */
const tableEmptyMessage = computed(() => {
  if (nearExpiryOnly.value) return '当前范围没有已过期或未来30天内到期的批次。'
  if (showSiteOverview.value) {
    if (siteStockTotalSkuCount.value === 0) return '暂无物料主数据，请先在基础数据维护物料。'
    return '已扫描的物料在本厂没有批次或序列号记录。可继续扫描其余物料，或换一个工厂。'
  }
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
    meta: filters.uomCode ? `计量单位 ${formatUom(filters.uomCode)}` : undefined,
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
  { key: 'skuCode', header: '物料', accessor: (r) => skuLabelOf(r) },
  {
    key: 'expiryDate',
    header: '效期',
    headerTitle: 'FEFO：预留与拣货建议优先选择更早到期的批次。',
    accessor: (r) => formatInventoryExpiryDate(r.expiryDate),
  },
  { key: 'expiryStatus', header: '效期状态' },
  { key: 'locationCode', header: '库位', width: 'w-28', accessor: (r) => locationLabelOf(r) },
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
function skuCodeOf(line: { skuCode?: string | null }) {
  return (line.skuCode ?? filters.skuCode) || ''
}
/** 「名称 编码」串，供排序与导出用；名录查不到就只有编码，不编名字。 */
function skuLabelOf(line: { skuCode?: string | null }) {
  const code = skuCodeOf(line)
  if (!code) return '—'
  const name = resolveSkuName(code)
  return name ? `${name} ${code}` : code
}
function locationLabelOf(line: { locationCode?: string | null }) {
  const code = line.locationCode ?? ''
  if (!code) return '无'
  const name = resolveLocation(code)
  return name ? `${name} ${code}` : code
}
/** 货主：类型说中文，具体货主优先显业务伙伴中文名。 */
function ownerLabel(ownerType?: string | null, ownerId?: string | null) {
  const type = ownerType ? labelFor(STOCK_LEDGER_OWNER_TYPE_LABELS, ownerType, '未知货主类型') : ''
  if (!ownerId) return type || '无'
  const partner = resolvePartner(ownerId) ?? ownerId
  return type ? `${type} · ${partner}` : partner
}
function qualityStatusLabel(value?: string | null) {
  return labelFor(QUALITY_STATUS_LABELS, value, '未知')
}
function qualityStatusTone(value?: string | null) {
  return STOCK_LEDGER_QUALITY_STATUS_TONES[normalizeCode(value)]
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
  else if (showSiteOverview.value) await refreshSiteStock()
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
    <div
      v-else-if="!showSiteOverview"
      class="grid gap-4 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]"
    >
      <NvMetricRing
        label="现存量构成"
        :value="formatQuantity(onHandQuantity)"
        :center-caption="filters.uomCode ? `现存量 · ${formatUom(filters.uomCode)}` : '现存量'"
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
          >单位 {{ formatUom(filters.uomCode) }}</span
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
        <!-- 库位/批次/序列号后端无主数据读面，选项从真实台账与仓储作业记录派生，来源已注明。 -->
        <NvEntityPicker
          v-model="filters.locationCode"
          class="w-36"
          :options="locationOptions"
          title="选择库位"
          placeholder="库位"
          :source-text="locationSourceText"
          :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
          :loading="warehouseCatalogPending"
          clearable
          aria-label="库位"
        />
        <NvEntityPicker
          v-if="!nearExpiryOnly"
          v-model="filters.lotNo"
          class="w-36"
          :options="lotOptions"
          title="选择批次"
          placeholder="批次"
          :source-text="lotSourceText"
          :empty-text="WAREHOUSE_LOT_EMPTY_TEXT"
          :loading="warehouseCatalogPending"
          clearable
          aria-label="批次"
        />
        <NvEntityPicker
          v-if="!nearExpiryOnly"
          v-model="filters.serialNo"
          class="w-36"
          :options="serialOptions"
          title="选择序列号"
          placeholder="序列号"
          :source-text="serialSourceText"
          :empty-text="WAREHOUSE_SERIAL_EMPTY_TEXT"
          :loading="warehouseCatalogPending"
          clearable
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

    <!--
      分页不是装饰：本厂批次台账**首屏就有三四千行**（实测前 24 个物料 = 3811 行，
      且 93 个物料全扫完约 1.4 万行），每行 11 列、4~5 个 RouterLink。
      一次性铺完等于同步做上万次路由解析 + 十万级组件实例，主线程会被钉死到打不开页面。
      效期预警走服务端分页（manual），其余两种来源在前端切页。
    -->
    <NvDataTable
      :columns="columns"
      :rows="rows"
      :row-key="lineKey"
      :loading="tablePending"
      :searchable="false"
      :column-settings="false"
      :manual="nearExpiryOnly"
      :page="expiryAlertsPage"
      :page-size="expiryAlertsPageSize"
      :total-items="expiryAlertsTotal"
      :page-size-options="[25, 50, 100, 200]"
      :error="tableError"
      :error-message="tableErrorMessage"
      :awaiting-scope="tableAwaitingScope"
      :awaiting-scope-message="tableAwaitingScopeMessage"
      :empty-message="tableEmptyMessage"
      @update:page="expiryAlertsPage = $event"
      @update:page-size="expiryAlertsPageSize = $event"
      @retry="refreshCurrentView"
    >
      <template #cell-lotNo="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ row.lotNo ?? '无批次' }}</span>
          <span class="text-xs text-muted-foreground">{{ row.serialNo ?? '无序列号' }}</span>
        </div>
      </template>
      <template #cell-skuCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <CodeWithNameCell
            :code="skuCodeOf(row) || undefined"
            :name="resolveSkuName(skuCodeOf(row))"
            fallback="—"
          />
          <span class="text-xs text-muted-foreground"
            >单位 {{ formatUom(row.uomCode ?? filters.uomCode, '—') }}</span
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
          <CodeWithNameCell
            :code="row.locationCode"
            :name="resolveLocation(row.locationCode)"
            fallback="无"
          />
          <span class="text-xs text-muted-foreground"
            >货主 {{ ownerLabel(row.ownerType, row.ownerId) }}</span
          >
        </div>
      </template>
      <template #cell-qualityStatus="{ row }">
        <NvStatusBadge
          :value="row.qualityStatus"
          :label="qualityStatusLabel(row.qualityStatus)"
          :tone="qualityStatusTone(row.qualityStatus)"
        />
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
    <!--
      覆盖范围如实交代：后端没有全量库存读面，这张全厂批次表是按物料逐个查台账再汇总的，
      所以要给出已扫描进度和继续扫描的出路，不能让人以为看到的就是全部。
    -->
    <div
      v-if="showSiteOverview"
      class="mt-3 flex flex-wrap items-center gap-3 text-sm text-muted-foreground"
    >
      <span>{{ siteStockCoverageText }}</span>
      <NvButton
        v-if="siteStockHasMore"
        size="sm"
        type="button"
        variant="outline"
        :disabled="siteStockScanning"
        @click="scanMoreSiteStock"
      >
        {{ siteStockScanning ? '扫描中…' : '继续扫描其余物料' }}
      </NvButton>
    </div>
  </BusinessLayout>
</template>
