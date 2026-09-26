<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import DeviceFormDialog from '@/components/masterData/DeviceFormDialog.vue'
import IncludeDisabledFilter from '@/components/masterData/IncludeDisabledFilter.vue'
import MasterDataLifecycleDialog from '@/components/masterData/MasterDataLifecycleDialog.vue'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useIncludeDisabledFilter } from '@/composables/masterDataIncludeDisabled'
import { useMasterDataLifecycleConfirm } from '@/composables/masterDataLifecycleConfirm'
import {
  useBusinessWorkshops,
  useMasterDataResource,
  useMasterDataResourceActions,
} from '@/composables/useBusinessMasterData'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvDataTable, NvPageHeader, NvStatusBadge, NvToolbar } from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, ref, shallowRef, watch } from 'vue'
import { formatDate, formatDateTime } from '@/utils/format'
import { inlineErrorMessage } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备台账',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

const devices = useMasterDataResource('device-asset')
const sites = useMasterDataResource('site')
const workshops = useBusinessWorkshops()
const lines = useMasterDataResource('production-line')
const workCenters = useMasterDataResource('work-center')
const deviceActions = useMasterDataResourceActions('device-asset')
// 停用/启用确认框收在页面层单实例，行操作只负责指向当前行（#1591）。
const lifecycle = useMasterDataLifecycleConfirm()

const { resolvePartnerLabel } = useBusinessPartnerNames()

// 列表回传的是 lineCode/workCenterCode（编码）；解析成名称显示（取自产线/工作中心实体，找不到回退编码）。
const siteNameByCode = computed(
  () => new Map(sites.items.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])),
)
const workshopNameByCode = computed(
  () =>
    new Map(workshops.workshops.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])),
)
const lineNameByCode = computed(
  () => new Map(lines.items.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])),
)
const wcNameByCode = computed(
  () => new Map(workCenters.items.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])),
)
function siteName(code?: string | null) {
  return code ? (siteNameByCode.value.get(code) ?? code) : '无'
}
function workshopName(code?: string | null) {
  return code ? (workshopNameByCode.value.get(code) ?? code) : '无'
}
function lineName(code?: string | null) {
  return code ? (lineNameByCode.value.get(code) ?? code) : '无'
}
/** 供应商列同样显中文名（本页其它编码列都已解名，这一列此前漏了）。 */
function supplierName(code?: string | null) {
  return code ? resolvePartnerLabel(code, '无') : '无'
}
function wcName(code?: string | null) {
  return code ? (wcNameByCode.value.get(code) ?? code) : '无'
}

const keyword = ref('')
const page = ref(1)
// 停用后的行默认不在列表里，「启用」入口就永远够不到（#1594）。
const includeDisabled = useIncludeDisabledFilter([devices.filters], () => {
  page.value = 1
})
const pageSize = ref('10')
// 设备弹窗：每次打开递增作 key，拿到全新实例（按 editing 初始化表单）；0 表示还没打开过。
const formOpen = shallowRef(false)
const formSession = shallowRef(0)
const formEditing = shallowRef<BusinessConsoleResourceItem>()

const columns: NvDataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '设备编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '设备名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'siteCode', header: '工厂', width: 'w-28', accessor: (r) => siteName(r.siteCode) },
  {
    key: 'workshopCode',
    header: '车间',
    width: 'w-28',
    accessor: (r) => workshopName(r.workshopCode),
  },
  { key: 'lineCode', header: '所属产线', width: 'w-32', accessor: (r) => lineName(r.lineCode) },
  { key: 'stationCode', header: '工位', width: 'w-28', accessor: (r) => r.stationCode ?? '无' },
  {
    key: 'warrantyExpiresOn',
    header: '保修到期',
    width: 'w-28',
    accessor: (r) => formatDate(r.warrantyExpiresOn),
  },
  {
    key: 'supplierPartnerCode',
    header: '供应商',
    width: 'w-28',
    accessor: (r) => supplierName(r.supplierPartnerCode),
  },
  { key: 'active', header: '状态', width: 'w-24' },
  {
    key: 'snapshotVersion',
    header: '更新时间',
    width: 'w-40',
    accessor: (r) => formatDateTime(r.snapshotVersion),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

function deviceDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '设备编码', value: row.code ?? '' },
    { label: '设备名称', value: row.displayName ?? '' },
    { label: '所属工厂', value: siteName(row.siteCode) },
    { label: '所属车间', value: workshopName(row.workshopCode) },
    { label: '所属产线', value: lineName(row.lineCode) },
    { label: '所属工作中心', value: wcName(row.workCenterCode) },
    { label: '所属工位', value: row.stationCode ?? '' },
    { label: '购置日期', value: formatDate(row.purchaseDate) },
    { label: '购置成本', value: formatMoney(row.purchaseCost, row.purchaseCurrencyCode) },
    { label: '保修到期', value: formatDate(row.warrantyExpiresOn) },
    { label: '供应商', value: row.supplierPartnerCode ?? '' },
    { label: '父设备', value: row.parentDeviceId ?? '' },
    { label: '退役日期', value: formatDate(row.retiredOn) },
  ]
}
const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return devices.items.value
  return devices.items.value.filter((row) =>
    [row.code, row.displayName, row.snapshotVersion].some((value) =>
      (value ?? '').toLowerCase().includes(kw),
    ),
  )
})
const listErrorMessage = computed(() => inlineErrorMessage(devices.error.value))

watch([keyword, pageSize], () => {
  page.value = 1
})
watch(
  [page, pageSize],
  () => {
    devices.filters.skip = (page.value - 1) * (Number(pageSize.value) || 10)
    devices.filters.take = Number(pageSize.value) || 10
  },
  { immediate: true },
)

function rowKey(item: BusinessConsoleResourceItem) {
  return `${item.resourceType ?? 'device-asset'}:${item.code || item.displayName || ''}`
}
function formatMoney(value?: number | null, currency?: string | null) {
  if (value == null) return '无'
  const code = currency?.trim() || 'CNY'
  try {
    return new Intl.NumberFormat('zh-CN', {
      style: 'currency',
      currency: code,
      maximumFractionDigits: 2,
    }).format(value)
  } catch {
    return `${value.toFixed(2)} ${code}`
  }
}
function refreshAll() {
  void devices.refresh()
  void sites.refresh()
  void workshops.refreshWorkshops()
  void lines.refresh()
  void workCenters.refresh()
}
function openForm(row?: BusinessConsoleResourceItem) {
  formEditing.value = row
  formSession.value += 1
  formOpen.value = true
}
function openEdit(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  openForm(row)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="设备台账"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${devices.total.value} 台设备`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="devices.pending.value"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openForm()">
          <PlusIcon aria-hidden="true" />
          新建设备
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="在当前页内筛选设备编码、名称">
      <template #filters>
        <IncludeDisabledFilter v-model="includeDisabled" />
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="devices.total.value"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :searchable="false"
      :column-settings="false"
      :columns="columns"
      :rows="listRows"
      :row-key="rowKey"
      :loading="devices.pending.value"
      empty-message="暂无设备。可清空筛选或新建设备。"
    >
      <template #cell-siteCode="{ row }">{{ siteName(row.siteCode) }}</template>
      <template #cell-workshopCode="{ row }">{{ workshopName(row.workshopCode) }}</template>
      <template #cell-lineCode="{ row }">{{ lineName(row.lineCode) }}</template>
      <template #cell-stationCode="{ row }">{{ row.stationCode ?? '无' }}</template>
      <template #cell-warrantyExpiresOn="{ row }">{{ formatDate(row.warrantyExpiresOn) }}</template>
      <template #cell-active="{ row }">
        <NvStatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions
          :row="row"
          entity-label="设备"
          :detail-fields="deviceDetailFields(row)"
          @toggle="(row) => lifecycle.request(row, deviceActions, '设备')"
          @edit="openEdit"
        />
      </template>
    </NvDataTable>
    <MasterDataLifecycleDialog :controller="lifecycle" />
    <DeviceFormDialog
      v-if="formSession"
      :key="formSession"
      v-model:open="formOpen"
      :editing="formEditing"
    />
  </BusinessLayout>
</template>
