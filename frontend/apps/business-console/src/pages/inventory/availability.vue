<script setup lang="ts">
import type {
  BusinessConsoleInventoryAvailabilityLineResponse,
  BusinessConsoleInventoryExpiryAlertLineResponse,
} from '@nerv-iip/api-client'
import { expiryToneFromAlert, expiryToneLabel } from '@nerv-iip/business-core'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import {
  useInventoryAvailability,
  useInventoryExpiryAlerts,
} from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvInput,
  NvPageHeader,
  NvRowActions,
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
import { ClipboardListIcon, MoveRightIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
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
  expiryAlerts,
  expiryAlertsError,
  expiryAlertsPending,
  filters: expiryFilters,
  refreshExpiryAlerts,
} = useInventoryExpiryAlerts()
const nearExpiryOnly = ref(false)

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
watch(
  () => [filters.siteCode, filters.skuCode, filters.locationCode] as const,
  ([siteCode, skuCode, locationCode]) => {
    expiryFilters.siteCode = siteCode
    expiryFilters.skuCode = skuCode || undefined
    expiryFilters.locationCode = locationCode || undefined
    nearExpiryOnly.value = false
  },
  { immediate: true },
)

const errorMessage = computed(() => formatError(availabilityError.value))
watch(expiryAlertsError, (error) => {
  if (error) notifyError(error, '近效期批次加载失败，请稍后重试。')
})
const onHandQuantity = computed(() => availability.value?.onHandQuantity ?? 0)
const availableQuantity = computed(() => availability.value?.availableQuantity ?? 0)
const reservedQuantity = computed(() => availability.value?.reservedQuantity ?? 0)
const frozenQuantity = computed(() =>
  Math.max(onHandQuantity.value - availableQuantity.value - reservedQuantity.value, 0),
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

type Line = BusinessConsoleInventoryAvailabilityLineResponse
type DisplayLine = Line & Partial<BusinessConsoleInventoryExpiryAlertLineResponse>
const rows = computed<DisplayLine[]>(() =>
  nearExpiryOnly.value ? expiryAlerts.value : availabilityLines.value,
)
const columns: NvDataTableColumn<DisplayLine>[] = [
  {
    key: 'locationCode',
    header: '库位',
    cellClass: 'font-medium',
    accessor: (r) => r.locationCode ?? '无',
  },
  { key: 'lot', header: '批次/序列号' },
  {
    key: 'expiryDate',
    header: '效期',
    headerTitle: 'FEFO：预留与拣货建议优先选择更早到期的批次。',
    accessor: (r) => formatDate(r.expiryDate),
  },
  { key: 'expiryStatus', header: '效期状态', accessor: (r) => expiryLabel(r) },
  { key: 'qualityStatus', header: '质量状态', width: 'w-28' },
  { key: 'owner', header: '货主', accessor: (r) => r.ownerId ?? r.ownerType ?? '无' },
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

function expiryLabel(line: DisplayLine) {
  return expiryToneLabel(expiryToneFromAlert(line))
}
function expiryToneValue(line: DisplayLine) {
  const tone = expiryToneFromAlert(line)
  return tone === 'fresh' ? 'success' : tone === 'near' ? 'warning' : tone ? 'danger' : 'neutral'
}
function formatDate(value?: string | null) {
  return value ? value.slice(0, 10) : '接口未提供'
}
function lineContextQuery(line: DisplayLine) {
  return {
    skuCode: filters.skuCode || undefined,
    siteCode: filters.siteCode || undefined,
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
  void router.push({ path: '/inventory/movements', query: lineContextQuery(line) })
}
function openCount(line: DisplayLine) {
  void router.push({ path: '/inventory/counts', query: lineContextQuery(line) })
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
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
async function refreshAll() {
  await Promise.all([refreshAvailability(), refreshExpiryAlerts()])
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="库存可用量"
      :breadcrumbs="[{ label: '库存' }]"
      :count="`${rows.length} 条明细`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          :variant="nearExpiryOnly ? 'default' : 'outline'"
          :disabled="expiryAlertsPending"
          @click="nearExpiryOnly = !nearExpiryOnly"
        >
          近效期（30天）
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
          :disabled="availabilityPending || expiryAlertsPending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="4">
      <NvSectionCard
        description="现存量"
        :value="formatQuantity(onHandQuantity)"
        :hint="filters.uomCode"
      />
      <NvSectionCard
        description="可用量"
        :value="formatQuantity(availableQuantity)"
        :hint="filters.uomCode"
      />
      <NvSectionCard
        description="预留量"
        :value="formatQuantity(reservedQuantity)"
        hint="已被占用"
      />
      <NvSectionCard
        description="冻结/其他"
        :value="formatQuantity(frozenQuantity)"
        hint="按返回数量推导"
      />
      <NvSectionCard
        description="近效期批次"
        :value="expiryAlerts.length"
        hint="服务端返回条数；当前 facade 未提供 total 字段"
      />
    </NvSectionCards>

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
        <NvSelect v-model="filters.ownerType">
          <NvSelectTrigger class="h-9 w-24" aria-label="货主类型"
            ><NvSelectValue placeholder="货主类型"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="owned">自有</NvSelectItem>
            <NvSelectItem value="customer">客户</NvSelectItem>
            <NvSelectItem value="supplier">供应商</NvSelectItem>
            <NvSelectItem value="consignment">寄售</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      :columns="columns"
      :rows="rows"
      :row-key="(r) => `${r.locationCode ?? 'loc'}-${r.lotNo ?? ''}-${r.serialNo ?? ''}`"
      :loading="availabilityPending || expiryAlertsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="未返回可用量明细。确认 SKU、工厂等查询条件后再试。"
    >
      <template #cell-lot="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ row.lotNo ?? '无批次' }}</span>
          <span class="text-xs text-muted-foreground">{{ row.serialNo ?? '无序列号' }}</span>
        </div>
      </template>
      <template #cell-qualityStatus="{ row }"
        ><NvStatusBadge :value="row.qualityStatus"
      /></template>
      <template #cell-expiryStatus="{ row }">
        <NvStatusBadge
          :value="expiryToneFromAlert(row)"
          :label="expiryLabel(row)"
          :tone="expiryToneValue(row)"
        />
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
        <div class="flex justify-end gap-2">
          <RouterLink
            class="inline-flex h-8 items-center rounded-md px-2 text-sm text-primary underline-offset-4 hover:underline"
            :to="{ path: '/barcode/scans', query: scanContextQuery(row) }"
          >
            扫码记录
          </RouterLink>
          <NvRowActions :label="`库存操作 ${row.locationCode ?? ''}`">
            <NvDropdownMenuItem @click="openMovement(row)">
              <MoveRightIcon aria-hidden="true" />
              发起移动
            </NvDropdownMenuItem>
            <NvDropdownMenuSeparator />
            <NvDropdownMenuItem @click="openCount(row)">
              <ClipboardListIcon aria-hidden="true" />
              创建盘点
            </NvDropdownMenuItem>
          </NvRowActions>
        </div>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
