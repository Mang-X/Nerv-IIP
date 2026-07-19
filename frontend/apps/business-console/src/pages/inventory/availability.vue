<script setup lang="ts">
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
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvInput,
  NvPageHeader,
  NvPagination,
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

type DisplayLine = InventoryExpiryDisplayLine
const rows = computed<DisplayLine[]>(() =>
  nearExpiryOnly.value ? visibleExpiryAlerts.value : availabilityLines.value,
)
const tablePending = computed(() =>
  nearExpiryOnly.value ? expiryAlertsPending.value : availabilityPending.value,
)
const pageCount = computed(() => {
  if (!nearExpiryOnly.value) return `${rows.value.length} 条明细`
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
  if (availabilityError.value) return '库存可用量加载失败，请稍后重试。'
  return '未返回可用量明细。确认 SKU、单位和工厂等查询条件后再试。'
})
const columns: NvDataTableColumn<DisplayLine>[] = [
  { key: 'skuCode', header: 'SKU', accessor: (r) => (r.skuCode ?? filters.skuCode) || '—' },
  { key: 'uomCode', header: '单位', accessor: (r) => (r.uomCode ?? filters.uomCode) || '—' },
  {
    key: 'locationCode',
    header: '库位',
    cellClass: 'font-medium',
    accessor: (r) => r.locationCode ?? '无',
  },
  { key: 'lot', header: '批次/序列号' },
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

function lineKey(line: DisplayLine) {
  return inventoryExpiryRowKey(line, filters.skuCode)
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
      : (line.movementBlockReason ?? '后端未提供移动禁用原因，请稍后重试或联系管理员。'),
    line.countAllowed === true
      ? undefined
      : (line.countBlockReason ?? '后端未提供盘点禁用原因，请稍后重试或联系管理员。'),
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
async function refreshCurrentView() {
  if (nearExpiryOnly.value) await refreshExpiryAlerts()
  else await refreshAvailability()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="库存可用量" :breadcrumbs="[{ label: '库存' }]" :count="pageCount">
      <template #actions>
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
    <NvSectionCards v-else :columns="4">
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
        <NvSelect v-if="!nearExpiryOnly" v-model="filters.ownerType">
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
