<script setup lang="ts">
import type { BusinessConsoleInventoryAvailabilityLineResponse } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useInventoryAvailability } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvPageHeader,
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

const errorMessage = computed(() => formatError(availabilityError.value))
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
const columns: NvDataTableColumn<Line>[] = [
  { key: 'lotNo', header: '批次', cellClass: 'font-medium', accessor: (r) => r.lotNo ?? '无批次' },
  { key: 'serialNo', header: '序列号', accessor: (r) => r.serialNo ?? '无序列号' },
  { key: 'locationCode', header: '库位', width: 'w-28', accessor: (r) => r.locationCode ?? '无' },
  { key: 'qualityStatus', header: '质量状态', width: 'w-28' },
  { key: 'owner', header: '货主', accessor: (r) => r.ownerId ?? r.ownerType ?? '无' },
  { key: 'onHandQuantity', header: '现存量', align: 'end', width: 'w-24' },
  { key: 'reservedQuantity', header: '预留量', align: 'end', width: 'w-24' },
  { key: 'availableQuantity', header: '可用量', align: 'end', width: 'w-24' },
  { key: 'blockedQuantity', header: '冻结/其他', align: 'end', width: 'w-24' },
  { key: 'actions', header: '关联', align: 'end', width: 'w-56' },
]

function lineKey(line: Line) {
  return [
    line.locationCode ?? 'loc',
    line.lotNo ?? 'lot',
    line.serialNo ?? 'serial',
    line.qualityStatus ?? 'status',
    line.ownerType ?? 'owner',
    line.ownerId ?? 'id',
  ].join('|')
}

function lineContextQuery(line: Line) {
  const lotNo = line.lotNo ?? undefined
  return {
    skuCode: filters.skuCode || undefined,
    siteCode: filters.siteCode || undefined,
    locationCode: line.locationCode ?? undefined,
    lotNo,
    batchNo: lotNo,
    materialLotId: lotNo,
    serialNo: line.serialNo ?? undefined,
  }
}

function traceabilityQuery(line: Line) {
  return {
    mode: 'batch',
    batchOrSerial: line.serialNo ?? line.lotNo ?? undefined,
  }
}

function barcodeQuery(line: Line) {
  const identifier = line.serialNo ?? line.lotNo ?? filters.skuCode
  return {
    sourceWorkflow: 'inventory.count',
    sourceDocumentId: identifier || undefined,
    scannedValue: identifier || undefined,
  }
}

function lineBlockedQuantity(line: Line) {
  return Math.max(
    (line.onHandQuantity ?? 0) - (line.availableQuantity ?? 0) - (line.reservedQuantity ?? 0),
    0,
  )
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

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="批次与预留"
      :breadcrumbs="[{ label: '库存' }]"
      :count="`${availabilityLines.length} 条库存明细`"
    >
      <template #actions>
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
          :disabled="availabilityPending"
          @click="refreshAvailability"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="4">
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

    <div class="rounded-md border border-dashed bg-muted/30 p-3 text-sm text-muted-foreground">
      <strong class="font-medium text-foreground">后端缺口：</strong>
      当前只消费 Inventory availability
      facade；独立批次台账、序列号履历、冻结/解冻、预留明细和服务端库存分析尚无 BusinessGateway
      facade，本页不做本地筛选或假分析。
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput v-model="filters.skuCode" class="h-9 w-32" placeholder="SKU" aria-label="SKU" />
        <NvInput v-model="filters.uomCode" class="h-9 w-20" placeholder="单位" aria-label="单位" />
        <NvInput v-model="filters.siteCode" class="h-9 w-20" placeholder="工厂" aria-label="工厂" />
        <NvInput
          v-model="filters.locationCode"
          class="h-9 w-24"
          placeholder="库位"
          aria-label="库位"
        />
        <NvInput v-model="filters.lotNo" class="h-9 w-28" placeholder="批次" aria-label="批次" />
        <NvInput
          v-model="filters.serialNo"
          class="h-9 w-28"
          placeholder="序列号"
          aria-label="序列号"
        />
        <NvSelect v-model="qualityStatusFilter">
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

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      :columns="columns"
      :rows="availabilityLines"
      :row-key="lineKey"
      :loading="availabilityPending"
      :searchable="false"
      :column-settings="false"
      empty-message="输入 SKU、单位和工厂后查询批次、序列号和预留信息。"
    >
      <template #cell-qualityStatus="{ row }">
        <NvStatusBadge :value="row.qualityStatus" />
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
          <NvButton size="sm" variant="ghost" as-child>
            <RouterLink :to="{ path: '/wms/picking', query: lineContextQuery(row) }">
              <WarehouseIcon aria-hidden="true" />
              WMS
            </RouterLink>
          </NvButton>
          <NvButton size="sm" variant="ghost" as-child>
            <RouterLink :to="{ path: '/quality/inspections', query: lineContextQuery(row) }">
              <ClipboardCheckIcon aria-hidden="true" />
              质量
            </RouterLink>
          </NvButton>
        </div>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
