<script setup lang="ts">
import type { BusinessConsoleInventoryAvailabilityLineResponse } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useInventoryAvailability } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DropdownMenuItem,
  DropdownMenuSeparator,
  Input,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { ClipboardListIcon, MoveRightIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '库存可用量' } })

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

const errorMessage = computed(() => formatError(availabilityError.value))
const onHandQuantity = computed(() => availability.value?.onHandQuantity ?? 0)
const availableQuantity = computed(() => availability.value?.availableQuantity ?? 0)
const reservedQuantity = computed(() => availability.value?.reservedQuantity ?? 0)
const frozenQuantity = computed(() => Math.max(onHandQuantity.value - availableQuantity.value - reservedQuantity.value, 0))

const qualityStatusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '可用', value: 'available' },
  { label: '待检', value: 'inspection' },
  { label: '冻结', value: 'blocked' },
  { label: '不合格', value: 'rejected' },
]
const qualityStatusFilter = computed({
  get: () => filters.qualityStatus || 'all',
  set: (value: string) => { filters.qualityStatus = value === 'all' ? undefined : value },
})

type Line = BusinessConsoleInventoryAvailabilityLineResponse
const columns: DataTableColumn<Line>[] = [
  { key: 'locationCode', header: '库位', cellClass: 'font-medium', accessor: (r) => r.locationCode ?? '无' },
  { key: 'lot', header: '批次/序列号' },
  { key: 'qualityStatus', header: '质量状态', width: 'w-28' },
  { key: 'owner', header: '货主', accessor: (r) => r.ownerId ?? r.ownerType ?? '无' },
  { key: 'onHandQuantity', header: '现存量', align: 'end', width: 'w-24' },
  { key: 'availableQuantity', header: '可用量', align: 'end', width: 'w-24' },
  { key: 'frozen', header: '冻结/其他', align: 'end', width: 'w-24', accessor: (r) => lineFrozen(r.onHandQuantity, r.availableQuantity) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function lineContextQuery(line: Line) {
  return {
    skuCode: filters.skuCode || undefined,
    siteCode: filters.siteCode || undefined,
    locationCode: line.locationCode ?? undefined,
    lotNo: line.lotNo ?? undefined,
    serialNo: line.serialNo ?? undefined,
  }
}
function openMovement(line: Line) {
  void router.push({ path: '/inventory/movements', query: lineContextQuery(line) })
}
function openCount(line: Line) {
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
</script>

<template>
  <BusinessLayout>
    <PageHeader title="库存可用量" :breadcrumbs="[{ label: '库存' }]" :count="`${availabilityLines.length} 条明细`">
      <template #actions>
        <Button v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`">返回工单 {{ contextWorkOrderId }}</RouterLink>
        </Button>
        <Button size="sm" type="button" variant="outline" :disabled="availabilityPending" @click="refreshAvailability">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="现存量" :value="formatQuantity(onHandQuantity)" :hint="filters.uomCode" />
      <SectionCard description="可用量" :value="formatQuantity(availableQuantity)" :hint="filters.uomCode" />
      <SectionCard description="预留量" :value="formatQuantity(reservedQuantity)" hint="已被占用" />
      <SectionCard description="冻结/其他" :value="formatQuantity(frozenQuantity)" hint="按返回数量推导" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.skuCode" class="h-9 w-32" placeholder="SKU" aria-label="SKU" />
        <Input v-model="filters.uomCode" class="h-9 w-20" placeholder="单位" aria-label="单位" />
        <Input v-model="filters.siteCode" class="h-9 w-20" placeholder="工厂" aria-label="工厂" />
        <Input v-model="filters.locationCode" class="h-9 w-24" placeholder="库位" aria-label="库位" />
        <Input v-model="filters.lotNo" class="h-9 w-28" placeholder="批次" aria-label="批次" />
        <Input v-model="filters.serialNo" class="h-9 w-28" placeholder="序列号" aria-label="序列号" />
        <Select v-model="qualityStatusFilter">
          <SelectTrigger class="h-9 w-28" aria-label="质量状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in qualityStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
        <Select v-model="filters.ownerType">
          <SelectTrigger class="h-9 w-24" aria-label="货主类型"><SelectValue placeholder="货主类型" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="owned">自有</SelectItem>
            <SelectItem value="customer">客户</SelectItem>
            <SelectItem value="supplier">供应商</SelectItem>
            <SelectItem value="consignment">寄售</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="availabilityLines"
      :row-key="(r) => `${r.locationCode ?? 'loc'}-${r.lotNo ?? ''}-${r.serialNo ?? ''}`"
      :loading="availabilityPending"
      empty-message="未返回可用量明细。确认 SKU、工厂等查询条件后再试。"
    >
      <template #cell-lot="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ row.lotNo ?? '无批次' }}</span>
          <span class="text-xs text-muted-foreground">{{ row.serialNo ?? '无序列号' }}</span>
        </div>
      </template>
      <template #cell-qualityStatus="{ row }"><StatusBadge :value="row.qualityStatus" /></template>
      <template #cell-onHandQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.onHandQuantity) }}</span></template>
      <template #cell-availableQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.availableQuantity) }}</span></template>
      <template #cell-frozen="{ row }"><span class="tabular-nums">{{ formatQuantity(lineFrozen(row.onHandQuantity, row.availableQuantity)) }}</span></template>
      <template #cell-actions="{ row }">
        <RowActions :label="`库存操作 ${row.locationCode ?? ''}`">
          <DropdownMenuItem @click="openMovement(row)">
            <MoveRightIcon aria-hidden="true" />
            发起移动
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem @click="openCount(row)">
            <ClipboardListIcon aria-hidden="true" />
            创建盘点
          </DropdownMenuItem>
        </RowActions>
      </template>
    </DataTable>
  </BusinessLayout>
</template>
