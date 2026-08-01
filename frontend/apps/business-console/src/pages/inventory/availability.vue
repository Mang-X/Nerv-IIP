<script setup lang="ts">
import type { NvDataTableColumn, NvMetricSegment } from '@nerv-iip/ui'
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
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useSkuNames } from '@/composables/useSkuNames'
import { useInventoryExpiryView } from '@/composables/useInventoryExpiryView'
import {
  useInventoryScopeDefaults,
  useInventorySiteExpiryOverview,
} from '@/composables/useInventoryScope'
import {
  useInventorySiteStockOverview,
  type SiteStockRow,
} from '@/composables/useInventorySiteStock'
import {
  useWarehouseCodeCatalog,
  WAREHOUSE_CATALOG_SOURCE_TEXT,
  WAREHOUSE_LOCATION_EMPTY_TEXT,
  WAREHOUSE_LOT_EMPTY_TEXT,
  WAREHOUSE_SERIAL_EMPTY_TEXT,
} from '@/composables/useWarehouseCodeCatalog'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { buildKpiTrend } from '@/utils/kpiTrend'
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
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvEntityPicker,
  NvMetricCard,
  NvMetricRing,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { ArrowLeftIcon, ClipboardListIcon, MoveRightIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '库存可用量',
    requiredPermissions: ['business.inventory.ledger.read'],
  },
})

const route = useRoute()
const router = useRouter()
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
// 寄售 / 客供库存的货主是业务伙伴编码，摆 CUST-WB-001 没人看得懂是谁。
const { resolvePartner } = useBusinessPartnerNames()
// 选物料之前先给一块跨物料的真实事实：全厂效期风险（库存域唯一只要工厂就能出行的读面）。
const {
  overviewError,
  overviewExpiredCount,
  overviewNearExpiryCount,
  overviewPending,
  overviewSkuCount,
  overviewTotalCount,
  overviewUrgentLines,
} = useInventorySiteExpiryOverview(() => filters.siteCode)
// 首屏不该要求先填条件：按物料目录并发扫台账，直接摆出本厂库存表，选物料才是「下钻」。
const {
  refreshSiteStock,
  scanMoreSiteStock,
  siteStockError,
  siteStockFailedCount,
  siteStockHasMore,
  siteStockRows,
  siteStockScannedCount,
  siteStockScanning,
  siteStockTotalSkuCount,
} = useInventorySiteStockOverview(() => filters.siteCode)
// 库位/批次/序列号后端无主数据读面，从已加载的台账行与仓储作业记录里派生可选项。
const { locationOptions, lotOptions, serialOptions, warehouseCatalogPending } =
  useWarehouseCodeCatalog(() => availabilityLines.value)

// 上下文穿透：从 MES 齐套/领料/完工入库带入 SKU/批次/库位/工厂查询库存事实。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
watch(
  () => route.query,
  (query) => {
    const sku = firstQuery(query.skuCode) || firstQuery(query.skuId)
    const lot = firstQuery(query.lotNo) || firstQuery(query.materialLotId)
    const site = firstQuery(query.siteCode)
    const location = firstQuery(query.locationCode)
    if (sku) filters.skuCode = sku
    if (lot) filters.lotNo = lot
    if (site) filters.siteCode = site
    if (location) filters.locationCode = location
  },
  { immediate: true },
)
watch(availabilityError, (error) => {
  if (error && !nearExpiryOnly.value) {
    notifyError(error, '库存可用量加载失败，请稍后重试。')
  }
})
watch(siteStockError, (error) => {
  if (error) notifyError(error, '本厂库存台账读取失败，请稍后重试。')
})
const onHandQuantity = computed(() => availability.value?.onHandQuantity ?? 0)
const availableQuantity = computed(() => availability.value?.availableQuantity ?? 0)
const reservedQuantity = computed(() => availability.value?.reservedQuantity ?? 0)
const frozenQuantity = computed(() =>
  Math.max(onHandQuantity.value - availableQuantity.value - reservedQuantity.value, 0),
)
// 可用 + 预留 + 冻结 = 现存量，是真正的构成关系，所以用环形卡。
const stockSegments = computed<NvMetricSegment[]>(() => [
  { key: 'available', label: '可用', value: availableQuantity.value, tone: 'success' },
  { key: 'reserved', label: '预留', value: reservedQuantity.value, tone: 'warning' },
  { key: 'frozen', label: '冻结/其他', value: frozenQuantity.value, tone: 'danger' },
])

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

type DisplayLine = InventoryExpiryDisplayLine
const rows = computed<DisplayLine[]>(() =>
  nearExpiryOnly.value ? visibleExpiryAlerts.value : availabilityLines.value,
)
const tablePending = computed(() =>
  nearExpiryOnly.value ? expiryAlertsPending.value : availabilityPending.value,
)
// 选了物料就进入该物料的台账明细，否则停在全厂库存总览——物料选择器本身就是下钻动作。
const hasSkuSelection = computed(() => filters.skuCode.trim().length > 0)
const showSiteOverview = computed(() => !nearExpiryOnly.value && !hasSkuSelection.value)
const urgentOverviewLines = computed(() => overviewUrgentLines.value.slice(0, 3))
const overviewFacets = computed(() => [
  {
    key: 'expired',
    label: '已过期',
    value: overviewExpiredCount.value,
    tone: overviewExpiredCount.value > 0 ? ('danger' as const) : ('neutral' as const),
  },
  {
    key: 'near',
    label: '30天内到期',
    value: overviewNearExpiryCount.value,
    tone: overviewNearExpiryCount.value > 0 ? ('warning' as const) : ('neutral' as const),
  },
  { key: 'sku', label: '涉及物料', value: overviewSkuCount.value },
])
const siteSelected = computed(() => (filters.siteCode ?? '').trim().length > 0)
/**
 * 效期风险批次数的走势**只是形状**：库存域没有历史读面，日期字段全是批次属性
 * （效期/生产日期），台账本身不带时间轴，算不出真的环比。当前值仍是后端真值，
 * 末点就落在卡片那个数上。没选工厂 / 还在读 / 读失败时一律不画——
 * 那三种情况下的 0 不是事实，不配挂一个"持平"。
 */
const expiryRiskTrend = computed(() =>
  siteSelected.value && !overviewPending.value && !overviewError.value
    ? buildKpiTrend('inventory.expiryRisk', overviewTotalCount.value, {
        kind: 'count',
        polarity: 'lower-better',
      })
    : undefined,
)
/** 同理：可用量也只补形状；读不出可用量时（未加载/失败）不挂趋势。 */
const availableTrend = computed(() =>
  availabilityPending.value || availabilityError.value || !availability.value
    ? undefined
    : buildKpiTrend('inventory.available', availableQuantity.value, { kind: 'amount' }),
)
/**
 * 计数三态：数不出来就别说「0 条」——0 是一个结论，只有真的查成功才配下。
 * 未选范围 / 加载中 / 失败各说各的，别让一次 500 长得跟「本厂真没有库存」一样。
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
    // 扫描是增量的：已经扫出物料就照实报数，个别失败在下方覆盖范围里单独交代。
    if (siteStockRows.value.length === 0 && siteStockError.value) return '读取失败'
    if (siteStockRows.value.length === 0 && siteStockScanning.value) return '扫描中'
    return `${siteStockRows.value.length} 个物料有库存`
  }
  if (availabilityError.value) return '读取失败'
  if (availabilityPending.value) return '加载中'
  return `${rows.value.length} 条明细`
})
/** 表格错误态：读失败就说读失败，不许伪装成「没有数据」。 */
const tableError = computed(() =>
  nearExpiryOnly.value ? expiryAlertsError.value : availabilityError.value,
)
const tableErrorMessage = computed(() =>
  nearExpiryOnly.value
    ? '效期预警读取失败，现在无法判断有没有临期批次。'
    : '库存可用量读取失败，现在无法判断这个物料有多少货。',
)
/** 还没形成查询条件时的中性态——什么结论都不下。 */
const tableAwaitingScope = computed(() => {
  if (nearExpiryOnly.value) return !hasExpirySite.value || !hasExpiryScope.value
  return !siteSelected.value
})
const tableAwaitingScopeMessage = computed(() => {
  if (nearExpiryOnly.value && !hasExpirySite.value) return '请选择工厂查看效期预警批次。'
  if (nearExpiryOnly.value) return '业务上下文加载中，请稍候。'
  return '请先选择工厂，再查库存可用量。'
})
/** 走到这里才是真的查成功且 0 条。 */
const tableEmptyMessage = computed(() =>
  nearExpiryOnly.value
    ? '当前范围没有已过期或未来30天内到期的批次。'
    : '没有查到库存明细。换个物料、工厂或库位再查一次。',
)
/**
 * 全厂库存总览的列：按物料汇总。**不跨物料加总数量**——不同物料单位不同
 * （原材料按 kg / l，件号按 pcs），把 kg 和 pcs 加在一起是错的业务口径。
 * 所以汇总只出现在可加的计数上（有货物料数、涉及库位数）。
 */
const siteStockColumns: NvDataTableColumn<SiteStockRow>[] = [
  { key: 'skuCode', header: '物料', cellClass: 'font-medium' },
  { key: 'onHandQuantity', header: '现存量', align: 'end', width: 'w-28' },
  { key: 'availableQuantity', header: '可用量', align: 'end', width: 'w-28' },
  { key: 'reservedQuantity', header: '预留', align: 'end', width: 'w-24' },
  { key: 'locationCount', header: '分布库位', align: 'end', width: 'w-24' },
  { key: 'lineCount', header: '台账行', align: 'end', width: 'w-20' },
  {
    key: 'earliestExpiry',
    header: '最早到期',
    headerTitle: 'FEFO：预留与拣货建议优先选择更早到期的批次。',
    accessor: (r) => (r.earliestExpiry ? formatInventoryExpiryDate(r.earliestExpiry) : '无效期'),
  },
]
const siteStockEmptyMessage = computed(() => {
  if (siteStockTotalSkuCount.value === 0) return '暂无物料主数据，请先在基础数据维护物料。'
  return '已扫描的物料在本厂都没有库存台账。可继续扫描其余物料，或换一个工厂。'
})
/** 一条都没扫出来才算整表失败；已经扫出物料时，失败数走下方的覆盖范围文案。 */
const siteStockTableError = computed(() =>
  siteStockRows.value.length === 0 ? siteStockError.value : undefined,
)
/** 覆盖范围必须如实说清楚：后端没有全量库存读面，这张表是按物料逐个查出来再汇总的。 */
const siteStockCoverageText = computed(() => {
  const scanned = siteStockScannedCount.value
  const total = siteStockTotalSkuCount.value
  const failed = siteStockFailedCount.value
  const base = `已扫描 ${scanned}/${total} 个物料`
  return failed > 0 ? `${base}，其中 ${failed} 个读取失败` : base
})

/**
 * 主要列只留下仓管真正据以决策的字段；单位、生产日期、保质期、效期来源
 * 收进对应单元格的第二行，避免十五列表格逼出横向滚动。
 */
const columns: NvDataTableColumn<DisplayLine>[] = [
  {
    key: 'skuCode',
    header: '物料',
    // 排序/导出取「名称 编码」，界面上名称在上、编码在下。
    accessor: (r) => skuLabelOf(r),
  },
  {
    key: 'locationCode',
    header: '库位',
    cellClass: 'font-medium',
    accessor: (r) => locationLabelOf(r),
  },
  { key: 'lot', header: '批次/序列号' },
  {
    key: 'expiryDate',
    header: '效期',
    headerTitle: 'FEFO：预留与拣货建议优先选择更早到期的批次。',
    accessor: (r) => formatInventoryExpiryDate(r.expiryDate),
  },
  { key: 'expiryStatus', header: '效期状态' },
  { key: 'qualityStatus', header: '质量状态', width: 'w-28' },
  { key: 'owner', header: '货主', accessor: (r) => ownerLabel(r.ownerType, r.ownerId) },
  { key: 'onHandQuantity', header: '现存量', align: 'end', width: 'w-24' },
  { key: 'availableQuantity', header: '可用量', align: 'end', width: 'w-24' },
  {
    key: 'frozen',
    header: '冻结/其他',
    align: 'end',
    width: 'w-24',
    accessor: (r) => lineFrozen(r.onHandQuantity, r.availableQuantity),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
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
  return {
    skuCode: (line.skuCode ?? filters.skuCode) || undefined,
    siteCode: (line.siteCode ?? filters.siteCode) || undefined,
    locationCode: line.locationCode ?? undefined,
    lotNo: line.lotNo ?? undefined,
    serialNo: line.serialNo ?? undefined,
  }
}
function scanContextQuery(line: DisplayLine) {
  const sourceDocumentId = line.lotNo ?? line.serialNo ?? filters.skuCode
  return {
    sourceWorkflow: 'inventory.count',
    sourceDocumentId: sourceDocumentId || undefined,
    scannedValue: line.serialNo ?? line.lotNo ?? undefined,
  }
}
function openMovement(line: DisplayLine) {
  if (line.movementAllowed !== true) return
  void router.push({ path: '/inventory/movements', query: lineContextQuery(line) })
}
function openCount(line: DisplayLine) {
  if (line.countAllowed !== true) return
  void router.push({ path: '/inventory/counts', query: lineContextQuery(line) })
}
function operationBlockReason(line: DisplayLine) {
  const reasons = [
    line.movementAllowed === true
      ? undefined
      : (line.movementBlockReason ?? '该库存行暂不能发起移动，请稍后重试或联系管理员。'),
    line.countAllowed === true
      ? undefined
      : (line.countBlockReason ?? '该库存行暂不能创建盘点，请稍后重试或联系管理员。'),
  ]
  return [...new Set(reasons.filter(Boolean))].join('；')
}
function lineFrozen(onHand?: number, available?: number) {
  return Math.max((onHand ?? 0) - (available ?? 0), 0)
}
function formatQuantity(value?: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
/** 选中总览里的一行 = 下钻到该物料的逐批次台账（单位会自动跟随物料带出）。 */
function drillIntoSku(row: SiteStockRow) {
  filters.skuCode = row.skuCode
}
function backToSiteOverview() {
  filters.skuCode = ''
  filters.locationCode = ''
  filters.lotNo = ''
  filters.serialNo = ''
}
async function refreshCurrentView() {
  if (nearExpiryOnly.value) await refreshExpiryAlerts()
  else if (showSiteOverview.value) await refreshSiteStock()
  else await refreshAvailability()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="库存可用量" :breadcrumbs="[{ label: '库存' }]" :count="pageCount">
      <template #actions>
        <NvButton
          v-if="hasSkuSelection && !nearExpiryOnly"
          size="sm"
          type="button"
          variant="outline"
          @click="backToSiteOverview"
        >
          <ArrowLeftIcon aria-hidden="true" />
          返回全厂库存
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          :variant="nearExpiryOnly ? 'default' : 'outline'"
          @click="toggleNearExpiryView"
        >
          效期预警（30天）
        </NvButton>
        <NvButton v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`"
            >返回工单 {{ contextWorkOrderId }}</RouterLink
          >
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
    <!-- 还没选物料时，环形卡只会画出一圈 0；改用只需工厂就能出数的全厂效期风险。 -->
    <div
      v-else-if="showSiteOverview"
      class="grid gap-4 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]"
    >
      <NvMetricCard
        variant="facets"
        label="全厂效期风险批次"
        :value="overviewPending ? '—' : overviewTotalCount"
        unit="批"
        :tone="overviewExpiredCount > 0 ? 'danger' : 'neutral'"
        :trend="expiryRiskTrend?.delta"
        :facets="overviewFacets"
      />
      <section class="grid content-start gap-2 rounded-md border bg-card p-4">
        <h2 class="text-sm font-semibold">最早到期的批次</h2>
        <ul v-if="urgentOverviewLines.length" class="grid gap-1.5 text-sm">
          <li
            v-for="line in urgentOverviewLines"
            :key="`${line.skuCode}-${line.locationCode}-${line.lotNo ?? ''}`"
            class="flex flex-wrap items-baseline gap-x-3 gap-y-0.5"
          >
            <span class="font-medium">{{ resolveSkuName(line.skuCode) ?? line.skuCode }}</span>
            <span class="text-muted-foreground">{{
              resolveLocation(line.locationCode) ?? line.locationCode
            }}</span>
            <span class="text-muted-foreground">批次 {{ line.lotNo ?? '无批次' }}</span>
            <span class="text-muted-foreground"
              >到期 {{ formatInventoryExpiryDate(line.expiryDate) }}</span
            >
            <span class="tabular-nums">{{ formatQuantity(line.availableQuantity) }} 可用</span>
          </li>
        </ul>
        <p v-else class="text-sm text-muted-foreground">
          {{
            overviewPending ? '正在读取全厂效期批次。' : '本厂没有已过期或未来30天内到期的批次。'
          }}
        </p>
      </section>
    </div>
    <!--
      选中物料后的单物料口径：左边现存量构成是真实构成关系，右边可用量的**当前值**
      同样是后端真值，只有那条走势线是补的形状（库存域无历史读面）。
    -->
    <div v-else class="grid gap-4 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
      <NvMetricRing
        label="现存量构成"
        :value="formatQuantity(onHandQuantity)"
        :center-caption="filters.uomCode ? `现存量 · ${formatUom(filters.uomCode)}` : '现存量'"
        :segments="stockSegments"
      />
      <NvMetricCard
        variant="sparkline"
        label="可用量"
        :value="availabilityPending ? '—' : formatQuantity(availableQuantity)"
        :unit="filters.uomCode ? formatUom(filters.uomCode) : undefined"
        :trend="availableTrend?.delta"
        :series="availableTrend?.series"
        :series-labels="availableTrend?.seriesLabels"
        :series-unit="filters.uomCode ? formatUom(filters.uomCode) : undefined"
        :foot-start="availableTrend?.footStart"
        :foot-end="availableTrend?.footEnd"
      />
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
          v-if="!showSiteOverview"
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
        <NvEntityPicker
          v-if="!nearExpiryOnly && !showSiteOverview"
          v-model="filters.lotNo"
          class="w-36"
          :options="lotOptions"
          title="选择批次"
          placeholder="批次"
          :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
          :empty-text="WAREHOUSE_LOT_EMPTY_TEXT"
          :loading="warehouseCatalogPending"
          clearable
          aria-label="批次"
        />
        <NvEntityPicker
          v-if="!nearExpiryOnly && !showSiteOverview"
          v-model="filters.serialNo"
          class="w-36"
          :options="serialOptions"
          title="选择序列号"
          placeholder="序列号"
          :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
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
        <NvSelect v-if="!nearExpiryOnly" v-model="filters.ownerType">
          <NvSelectTrigger class="h-9 w-24" aria-label="货主类型"
            ><NvSelectValue placeholder="货主类型"
          /></NvSelectTrigger>
          <NvSelectContent>
            <!-- 取值须落在 Inventory 服务认得的货主类型上（含别名），否则查询直接 400。 -->
            <NvSelectItem value="owned">本公司</NvSelectItem>
            <NvSelectItem value="customer">客户寄售</NvSelectItem>
            <NvSelectItem value="supplier">供应商寄售</NvSelectItem>
            <NvSelectItem value="production">生产领用</NvSelectItem>
            <NvSelectItem value="maintenance">维修备件</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <template v-if="showSiteOverview">
      <NvDataTable
        :columns="siteStockColumns"
        :rows="siteStockRows"
        :row-key="(row: SiteStockRow) => row.skuCode"
        :loading="siteStockScanning && siteStockRows.length === 0"
        :searchable="false"
        :column-settings="false"
        :page-size-options="[25, 50, 100]"
        :error="siteStockTableError"
        error-message="本厂库存台账读取失败，现在无法判断本厂有多少货。"
        :awaiting-scope="!siteSelected"
        awaiting-scope-message="请先选择工厂，再查本厂库存。"
        :empty-message="siteStockEmptyMessage"
        @retry="refreshSiteStock"
      >
        <template #cell-skuCode="{ row }">
          <button
            type="button"
            class="grid justify-items-start gap-0.5 text-left text-primary underline-offset-4 hover:underline"
            @click="drillIntoSku(row)"
          >
            <span>{{ row.skuName }}</span>
            <span class="text-xs text-muted-foreground">{{ row.skuCode }} · {{ row.uomCode }}</span>
          </button>
        </template>
        <template #cell-onHandQuantity="{ row }">
          <span class="tabular-nums">{{ formatQuantity(row.onHandQuantity) }}</span>
        </template>
        <template #cell-availableQuantity="{ row }">
          <span class="tabular-nums">{{ formatQuantity(row.availableQuantity) }}</span>
        </template>
        <template #cell-reservedQuantity="{ row }">
          <span class="tabular-nums">{{ formatQuantity(row.reservedQuantity) }}</span>
        </template>
        <template #cell-locationCount="{ row }">
          <span class="tabular-nums">{{ row.locationCount }}</span>
        </template>
        <template #cell-lineCount="{ row }">
          <span class="tabular-nums">{{ row.lineCount }}</span>
        </template>
        <template #cell-earliestExpiry="{ row }">
          <div class="flex items-center justify-start gap-2">
            <span>{{
              row.earliestExpiry ? formatInventoryExpiryDate(row.earliestExpiry) : '无效期'
            }}</span>
            <NvStatusBadge v-if="row.hasBlocked" value="blocked" />
          </div>
        </template>
      </NvDataTable>
      <!--
        覆盖范围如实交代：后端没有全量库存读面，这张表按物料逐个查台账再汇总，
        所以要给出已扫描进度和继续扫描的出路，不能让人以为看到的就是全部。
      -->
      <div class="mt-3 flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
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
    </template>

    <NvDataTable
      v-else
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
      @update:page="expiryAlertsPage = $event"
      @update:page-size="expiryAlertsPageSize = $event"
      :error="tableError"
      :error-message="tableErrorMessage"
      :awaiting-scope="tableAwaitingScope"
      :awaiting-scope-message="tableAwaitingScopeMessage"
      :empty-message="tableEmptyMessage"
      @retry="refreshCurrentView"
    >
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
      <template #cell-locationCode="{ row }">
        <CodeWithNameCell
          :code="row.locationCode"
          :name="resolveLocation(row.locationCode)"
          fallback="无"
        />
      </template>
      <template #cell-lot="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ row.lotNo ?? '无批次' }}</span>
          <span class="text-xs text-muted-foreground">{{ row.serialNo ?? '无序列号' }}</span>
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
      <template #cell-qualityStatus="{ row }"
        ><NvStatusBadge
          :value="row.qualityStatus"
          :label="qualityStatusLabel(row.qualityStatus)"
          :tone="qualityStatusTone(row.qualityStatus)"
      /></template>
      <template #cell-expiryStatus="{ row }">
        <InventoryExpiryStatusBadge :line="row" />
      </template>
      <template #cell-onHandQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.onHandQuantity) }}</span></template
      >
      <template #cell-availableQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.availableQuantity) }}</span></template
      >
      <template #cell-frozen="{ row }"
        ><span class="tabular-nums">{{
          formatQuantity(lineFrozen(row.onHandQuantity, row.availableQuantity))
        }}</span></template
      >
      <template #cell-actions="{ row }">
        <div class="flex min-w-48 flex-col items-end gap-1">
          <p
            v-if="operationBlockReason(row)"
            data-operation-block-reason
            class="max-w-56 text-right text-xs leading-4 text-muted-foreground"
          >
            {{ operationBlockReason(row) }}
          </p>
          <div class="flex justify-end gap-2">
            <RouterLink
              class="inline-flex h-8 items-center rounded-md px-2 text-sm text-primary underline-offset-4 hover:underline"
              :to="{ path: '/barcode/scans', query: scanContextQuery(row) }"
            >
              扫码记录
            </RouterLink>
            <NvRowActions :label="`库存操作 ${row.locationCode ?? ''}`">
              <NvDropdownMenuItem
                :disabled="row.movementAllowed !== true"
                :title="row.movementBlockReason ?? undefined"
                @click="openMovement(row)"
              >
                <MoveRightIcon aria-hidden="true" />
                发起移动
              </NvDropdownMenuItem>
              <NvDropdownMenuSeparator />
              <NvDropdownMenuItem
                :disabled="row.countAllowed !== true"
                :title="row.countBlockReason ?? undefined"
                @click="openCount(row)"
              >
                <ClipboardListIcon aria-hidden="true" />
                创建盘点
              </NvDropdownMenuItem>
            </NvRowActions>
          </div>
        </div>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
