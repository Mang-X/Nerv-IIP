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
  filters,
} = useMaintenanceSelfWorkOrders()
const devicePickerOpen = shallowRef(false)
const selectedDeviceLabel = shallowRef('')
const scopeLabel = computed(() => {
  if (!scopeReady.value) return '个人维修范围未就绪'
  if (hasSuccessfulResponse.value) return '当前维修人员（服务端 Self 范围）/ 当前业务环境'
  return '正在核验当前维修人员 Self 范围'
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

function openDetail(item: BusinessConsoleMaintenanceWorkOrderItem) {
  if (!item.workOrderId) return
  router.push(`/equipment/work-orders/${encodeURIComponent(item.workOrderId)}`).catch(() => {})
}

function restoreState(state: { filters: Record<string, unknown> }) {
  const restored = state.filters
  filters.status = typeof restored.status === 'string' ? restored.status : ''
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
        source="维修工单服务（服务端 Self 范围）"
        :loaded="loaded"
        :total="total"
        :has-more="hasMore"
        :updated-at="lastUpdatedAt"
        :pending="pending"
        :refreshing="refreshing"
        :loading-more="loadingMore"
        :error="error"
        :load-more-error="loadMoreError"
        error-test-id="maintenance-self-work-orders-error"
        failure-explanation="当前维修人员范围未得到服务端成功响应，不展示旧队列。"
        :filter-state="filterState"
        :empty-description="
          scopeReady
            ? '当前维修人员暂无符合筛选条件的维修工单。'
            : '缺少当前维修人员、组织/环境、维修工单读取权限或设备位置读取权限，未发起查询。'
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
            @choose-device="devicePickerOpen = true"
          />
        </template>

        <MaintenanceWorkOrderList :items="items" @select="openDetail" />
      </TaskListShell>
    </div>

    <DeviceAssetPicker v-model:open="devicePickerOpen" @select="onDeviceSelected" />
  </NvAppShellMobile>
</template>
