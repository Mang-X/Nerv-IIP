<script setup lang="ts">
import type { BusinessConsoleInventoryLocationResponse } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import LocationFormDialog from '@/components/inventory/LocationFormDialog.vue'
import { LOCATION_TYPE_OPTIONS } from '@/components/inventory/locationOptions'
import { useInventoryLocations } from '@/composables/useBusinessInventory'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvDataTable, NvPageHeader, NvStatusBadge, NvToolbar } from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, shallowRef } from 'vue'
import { inlineErrorMessage } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '库位',
    requiredPermissions: ['business.inventory.locations.manage'],
  },
})

const {
  filters,
  locationRows,
  locationsError,
  locationsPage,
  locationsPageSize,
  locationsPending,
  locationsTotal,
  refreshLocations,
} = useInventoryLocations()

function locationTypeLabel(value?: string | null) {
  if (!value) return '—'
  return LOCATION_TYPE_OPTIONS.find((o) => o.value === value)?.label ?? value
}

const siteCatalog = useBusinessMasterDataResources('site')
function siteLabel(code?: string | null) {
  if (!code) return '—'
  const site = siteCatalog.resources.value.find((s) => s.code === code)
  return site?.displayName?.trim() || code
}

const search = computed({
  get: () => filters.keyword ?? '',
  set: (value: string) => {
    filters.keyword = value.trim() ? value : undefined
  },
})

const listErrorMessage = computed(() => inlineErrorMessage(locationsError.value))

const columns: NvDataTableColumn<BusinessConsoleInventoryLocationResponse>[] = [
  { key: 'locationCode', header: '库位编码', cellClass: 'font-medium' },
  { key: 'locationType', header: '类型', width: 'w-36' },
  { key: 'siteCode', header: '工厂' },
  { key: 'parentLocationCode', header: '上级库位' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]

// ── 新建 / 编辑 ─────────────────────────────────────────────────
// 每次打开递增，作弹窗的 key：每次都是全新实例，按当次的库位（或空白）初始化表单。
const formSession = shallowRef(0)
const formOpen = shallowRef(false)
const editing = shallowRef<BusinessConsoleInventoryLocationResponse>()

function openCreate() {
  editing.value = undefined
  formSession.value += 1
  formOpen.value = true
}

function openEdit(row: BusinessConsoleInventoryLocationResponse) {
  if (!row.locationCode) return
  editing.value = row
  formSession.value += 1
  formOpen.value = true
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="库位"
      :breadcrumbs="[{ label: '库存管理' }]"
      :count="`${locationsTotal} 个库位`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="locationsPending"
          @click="refreshLocations"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建库位
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="search" search-placeholder="按库位编码筛选" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="locationsPage"
      :page-size="String(locationsPageSize)"
      :total-items="locationsTotal"
      @update:page="locationsPage = $event"
      @update:page-size="(v) => (locationsPageSize = Number(v))"
      :columns="columns"
      :rows="locationRows"
      row-key="locationCode"
      :loading="locationsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="还没有库位。"
    >
      <template #empty>
        <template v-if="search">
          <p class="text-sm font-medium">没有符合条件的库位</p>
          <NvButton size="sm" type="button" variant="outline" @click="search = ''">
            清空筛选
          </NvButton>
        </template>
        <template v-else>
          <p class="text-sm font-medium">还没有库位</p>
          <p class="max-w-md text-sm text-muted-foreground">
            新建原料库、成品库、线边库等库位后，收发料与线边库存才有去处。
          </p>
          <NvButton size="sm" type="button" @click="openCreate">
            <PlusIcon aria-hidden="true" />
            新建库位
          </NvButton>
        </template>
      </template>
      <template #cell-locationType="{ row }">
        {{ locationTypeLabel(row.locationType) }}
      </template>
      <template #cell-siteCode="{ row }">
        {{ siteLabel(row.siteCode) }}
      </template>
      <template #cell-parentLocationCode="{ row }">
        <span v-if="row.parentLocationCode">{{ row.parentLocationCode }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge
          :label="row.status === 'active' ? '启用' : '停用'"
          :tone="row.status === 'active' ? 'success' : 'neutral'"
        />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <NvButton type="button" variant="ghost" size="sm" @click="openEdit(row)">编辑</NvButton>
        </div>
      </template>
    </NvDataTable>

    <LocationFormDialog
      v-if="formSession"
      :key="formSession"
      v-model:open="formOpen"
      :location="editing"
    />
  </BusinessLayout>
</template>
