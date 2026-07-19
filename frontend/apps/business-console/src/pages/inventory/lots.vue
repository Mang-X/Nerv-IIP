<script setup lang="ts">
import type { BusinessConsoleInventoryAvailabilityLineResponse } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import InventoryExpiryStatusBadge from '@/components/inventory/InventoryExpiryStatusBadge.vue'
import InventoryExpirySummaryCards from '@/components/inventory/InventoryExpirySummaryCards.vue'
import { useInventoryAvailability } from '@/composables/useBusinessInventory'
import { useInventoryExpiryView } from '@/composables/useInventoryExpiryView'
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
  NvInput,
  NvPageHeader,
  NvPagination,
  NvSectionCard,
  NvSectionCards,
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
  hasExpiryScope,
  nearExpiryOnly,
  refreshExpiryAlerts,
  toggleNearExpiryView,
  visibleExpiryAlerts,
} = useInventoryExpiryView(filters)
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
  if (!nearExpiryOnly.value) return `${rows.value.length} 条库存明细`
  if (!hasExpiryScope.value) return '请选择工厂'
  if (expiryAlertsPending.value) return '加载中'
  if (expiryAlertsError.value) return '加载失败'
  if (!expiryAlertsSuccessful.value) return '等待查询'
  return `${expiryAlertsTotal.value} 条预警明细`
})
const tableEmptyMessage = computed(() => {
  if (nearExpiryOnly.value && !hasExpiryScope.value) return '请选择工厂查看效期预警批次。'
  if (nearExpiryOnly.value && expiryAlertsError.value) return '效期预警加载失败，请稍后重试。'
  if (nearExpiryOnly.value) return '当前范围没有已过期或未来30天内到期的批次。'
  if (availabilityError.value) return '库存批次加载失败，请稍后重试。'
  return '输入 SKU、单位和工厂后查询批次、序列号和预留信息。'
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

type Line = BusinessConsoleInventoryAvailabilityLineResponse
type DisplayLine = InventoryExpiryDisplayLine
const columns: NvDataTableColumn<DisplayLine>[] = [
  { key: 'skuCode', header: 'SKU', accessor: (r) => (r.skuCode ?? filters.skuCode) || '—' },
  { key: 'uomCode', header: '单位', accessor: (r) => (r.uomCode ?? filters.uomCode) || '—' },
  { key: 'lotNo', header: '批次', cellClass: 'font-medium', accessor: (r) => r.lotNo ?? '无批次' },
  { key: 'serialNo', header: '序列号', accessor: (r) => r.serialNo ?? '无序列号' },
  {
    key: 'productionDate',
    header: '生产日期',
    accessor: (r) => formatInventoryExpiryDate(r.productionDate),
  },
  {
    key: 'expiryDate',
    header: '效期',
    headerTitle: 'FEFO：预留与拣货建议优先选择更早到期的批次。',
    accessor: (r) => formatInventoryExpiryDate(r.expiryDate),
  },
  {
    key: 'shelfLife',
    header: '保质期',
    accessor: (r) => formatInventoryShelfLife(r.shelfLifeDays),
  },
  {
    key: 'expirySource',
    header: '效期来源',
    accessor: (r) => formatInventoryExpirySource(r.expiryDateSource),
  },
  { key: 'expiryStatus', header: '效期状态' },
  { key: 'locationCode', header: '库位', width: 'w-28', accessor: (r) => r.locationCode ?? '无' },
  { key: 'qualityStatus', header: '质量状态', width: 'w-28' },
  { key: 'owner', header: '货主', accessor: (r) => r.ownerId ?? r.ownerType ?? '无' },
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
  return line.movementBlockReason ?? '后端未提供移动禁用原因，请稍后重试或联系管理员。'
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
    <NvSectionCards v-else :columns="4">
      <NvSectionCard description="批次数" :value="lotCount" hint="来自可用量明细" />
      <NvSectionCard description="序列号数" :value="serialCount" hint="来自可用量明细" />
      <NvSectionCard
        description="预留量"
        :value="formatQuantity(reservedQuantity)"
        :hint="filters.uomCode"
      />
      <NvSectionCard
        description="冻结/其他"
        :value="formatQuantity(blockedQuantity)"
        hint="按现存量减可用量和预留量推导"
      />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput v-model="filters.skuCode" class="h-9 w-32" placeholder="SKU" aria-label="SKU" />
        <NvInput
          v-if="!nearExpiryOnly"
          v-model="filters.uomCode"
          class="h-9 w-20"
          placeholder="单位"
          aria-label="单位"
        />
        <NvInput v-model="filters.siteCode" class="h-9 w-20" placeholder="工厂" aria-label="工厂" />
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

    <NvDataTable
      :columns="columns"
      :rows="rows"
      :row-key="lineKey"
      :loading="tablePending"
      :searchable="false"
      :column-settings="false"
      :pagination="false"
      :empty-message="tableEmptyMessage"
    >
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
