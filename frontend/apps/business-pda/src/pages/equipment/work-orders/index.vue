<script setup lang="ts">
import DeviceAssetPicker from '@/components/equipment/DeviceAssetPicker.vue'
import MaintenanceWorkOrderFilters from './components/MaintenanceWorkOrderFilters.vue'
import MaintenanceWorkOrderList from './components/MaintenanceWorkOrderList.vue'
import TaskListShell from '@/components/task-list/TaskListShell.vue'
import { normalizeCanonicalGuid } from '@/composables/maintenancePublicIds'
import { useMaintenanceSelfWorkOrders } from '@/composables/useMaintenanceSelfWorkOrders'
import type {
  BusinessConsoleMaintenanceWorkOrderItem,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import { normalizeMaintenanceWorkOrderStatusFilter } from '@nerv-iip/business-core'
import { NvAppShellMobile, NvNavBar } from '@nerv-iip/ui-mobile'
import { computed, shallowRef } from 'vue'
import { useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '维修工单' } })

const router = useRouter()
const {
  scopeKey,
  scopeReady,
  items,
  total,
  loaded,
  hasMore,
  loadingMore,
  refreshing,
  loadMoreError,
  loadMore,
  refresh,
  pending,
  error,
  hasFailedResponse,
  filters,
  principalDisplayName,
} = useMaintenanceSelfWorkOrders()
const devicePickerOpen = shallowRef(false)
const selectedDeviceLabel = shallowRef('')
const selectedDeviceCode = shallowRef('')
const displayError = computed(
  () =>
    error.value ?? (hasFailedResponse.value ? new Error('维修工单读取失败，请重试。') : undefined),
)
const filterState = computed(() => ({
  status: filters.status,
  deviceAssetIds: filters.deviceAssetIds,
  deviceCode: filters.deviceAssetIds.length === 1 ? selectedDeviceCode.value : '',
  deviceLabel: filters.deviceAssetIds.length === 1 ? selectedDeviceLabel.value : '',
  keyword: filters.keyword,
}))

function onDeviceSelected(device: BusinessConsoleResourceItem & { deviceAssetId: string }) {
  const deviceCode = device.code?.trim()
  const deviceAssetId = normalizeCanonicalGuid(device.deviceAssetId)
  if (!deviceCode || !deviceAssetId) return
  filters.deviceAssetIds = [deviceAssetId]
  selectedDeviceCode.value = deviceCode
  selectedDeviceLabel.value = device.displayName?.trim() || device.code?.trim() || '已选择设备'
}

function openDevicePicker() {
  if (scopeReady.value) devicePickerOpen.value = true
}

function openDetail(item: BusinessConsoleMaintenanceWorkOrderItem) {
  if (!item.workOrderId) return
  router.push(`/equipment/work-orders/${encodeURIComponent(item.workOrderId)}`).catch(() => {})
}

function restoreState(state: { filters: Record<string, unknown> }) {
  const restored = state.filters
  const restoredDeviceReferences = Array.isArray(restored.deviceAssetIds)
    ? restored.deviceAssetIds
    : []
  const restoredDeviceAssetId =
    restoredDeviceReferences.length === 1
      ? normalizeCanonicalGuid(restoredDeviceReferences[0])
      : undefined
  const restoredDeviceCode =
    typeof restored.deviceCode === 'string' ? restored.deviceCode.trim() : ''
  filters.status = normalizeMaintenanceWorkOrderStatusFilter(restored.status)
  filters.deviceAssetIds =
    restoredDeviceAssetId && restoredDeviceCode ? [restoredDeviceAssetId] : []
  filters.keyword = typeof restored.keyword === 'string' ? restored.keyword : ''
  selectedDeviceCode.value = filters.deviceAssetIds.length ? restoredDeviceCode : ''
  selectedDeviceLabel.value =
    filters.deviceAssetIds.length && typeof restored.deviceLabel === 'string'
      ? restored.deviceLabel.trim()
      : ''
}
</script>

<template>
  <NvAppShellMobile>
    <template #header><NvNavBar title="维修工单" /></template>

    <div class="flex h-full min-h-0 flex-col">
      <TaskListShell
        :state-key="`maintenance-self-work-orders:${scopeKey}`"
        :loaded="loaded"
        :total="total"
        :has-more="hasMore"
        :pending="pending"
        :refreshing="refreshing"
        :loading-more="loadingMore"
        :error="displayError"
        :load-more-error="loadMoreError"
        error-test-id="maintenance-self-work-orders-error"
        :filter-state="filterState"
        :empty-description="
          scopeReady
            ? '当前维修人员暂无符合筛选条件的维修工单。'
            : '当前账号暂无法查看，请重新登录或联系管理员。'
        "
        @refresh="refresh"
        @retry="refresh"
        @load-more="loadMore"
        @retry-load-more="loadMore"
        @restore="restoreState"
      >
        <template #filters>
          <MaintenanceWorkOrderFilters
            v-model:status="filters.status"
            v-model:device-asset-ids="filters.deviceAssetIds"
            v-model:keyword="filters.keyword"
            :device-label="selectedDeviceLabel"
            @choose-device="openDevicePicker"
          />
        </template>

        <MaintenanceWorkOrderList
          :items="items"
          :principal-display-name="principalDisplayName"
          @select="openDetail"
        />
      </TaskListShell>
    </div>

    <DeviceAssetPicker
      v-if="scopeReady"
      v-model:open="devicePickerOpen"
      @select="onDeviceSelected"
    />
  </NvAppShellMobile>
</template>
