<script setup lang="ts">
import DeviceAssetPicker from '@/components/equipment/DeviceAssetPicker.vue'
import MaintenanceWorkOrderFilters from '@/components/maintenance/MaintenanceWorkOrderFilters.vue'
import MaintenanceWorkOrderList from '@/components/maintenance/MaintenanceWorkOrderList.vue'
import TaskListShell from '@/components/task-list/TaskListShell.vue'
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
  principalId,
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
  lastUpdatedAt,
  hasSuccessfulResponse,
  hasFailedResponse,
  filters,
} = useMaintenanceSelfWorkOrders()
const devicePickerOpen = shallowRef(false)
const selectedDeviceLabel = shallowRef('')
const displayError = computed(
  () =>
    error.value ?? (hasFailedResponse.value ? new Error('维修工单读取失败，请重试。') : undefined),
)
const visibleItems = computed(() => (hasFailedResponse.value ? [] : items.value))
const visibleTotal = computed(() => (hasFailedResponse.value ? 0 : total.value))
const visibleLoaded = computed(() => visibleItems.value.length)
const visibleHasMore = computed(() => !hasFailedResponse.value && hasMore.value)
const scopeLabel = computed(() => {
  if (!scopeReady.value) return '当前账号暂无法查看维修工单'
  if (hasFailedResponse.value) return '维修工单暂不可用'
  if (hasSuccessfulResponse.value) return '分派给当前维修人员 / 当前业务环境'
  return '正在读取当前维修人员的工单'
})
const filterState = computed(() => ({
  status: filters.status,
  deviceAssetId: filters.deviceAssetId,
  keyword: filters.keyword,
}))

function onDeviceSelected(device: BusinessConsoleResourceItem & { deviceAssetId: string }) {
  filters.deviceAssetId = device.deviceAssetId
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
  filters.status = normalizeMaintenanceWorkOrderStatusFilter(restored.status)
  filters.deviceAssetId = typeof restored.deviceAssetId === 'string' ? restored.deviceAssetId : ''
  filters.keyword = typeof restored.keyword === 'string' ? restored.keyword : ''
  if (!filters.deviceAssetId) selectedDeviceLabel.value = ''
}
</script>

<template>
  <NvAppShellMobile>
    <template #header><NvNavBar title="维修工单" /></template>

    <div class="flex h-full min-h-0 flex-col">
      <TaskListShell
        :state-key="`maintenance-self-work-orders:${principalId}`"
        :scope="scopeLabel"
        source="维修工单"
        :loaded="visibleLoaded"
        :total="visibleTotal"
        :has-more="visibleHasMore"
        :updated-at="lastUpdatedAt"
        :pending="pending"
        :refreshing="refreshing"
        :loading-more="loadingMore"
        :error="displayError"
        :load-more-error="loadMoreError"
        error-test-id="maintenance-self-work-orders-error"
        failure-explanation="未成功读取当前维修人员的工单，不展示之前的队列。"
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
            v-model:device-asset-id="filters.deviceAssetId"
            v-model:keyword="filters.keyword"
            :device-label="selectedDeviceLabel"
            @choose-device="openDevicePicker"
          />
        </template>

        <MaintenanceWorkOrderList :items="visibleItems" @select="openDetail" />
      </TaskListShell>
    </div>

    <DeviceAssetPicker
      v-if="scopeReady"
      v-model:open="devicePickerOpen"
      @select="onDeviceSelected"
    />
  </NvAppShellMobile>
</template>
