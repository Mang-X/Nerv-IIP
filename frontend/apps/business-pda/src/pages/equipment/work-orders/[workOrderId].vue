<script setup lang="ts">
import MaintenanceWorkOrderDetail from '@/components/maintenance/MaintenanceWorkOrderDetail.vue'
import RetryableListError from '@/components/RetryableListError.vue'
import { useMaintenanceSelfWorkOrderDetail } from '@/composables/useMaintenanceSelfWorkOrders'
import { NvAppShellMobile, NvMobileButton } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '维修工单详情' } })

const route = useRoute('/equipment/work-orders/[workOrderId]')
const router = useRouter()
const requestedWorkOrderId = computed(() => {
  const value = route.params.workOrderId
  return typeof value === 'string' ? value.trim() : ''
})
const { scopeReady, workOrder, device, pending, error, hasFailedResponse, refresh } =
  useMaintenanceSelfWorkOrderDetail(requestedWorkOrderId)

function backToList() {
  router.push('/equipment/work-orders').catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <NvMobileButton variant="text" size="sm" aria-label="返回维修工单列表" @click="backToList">
          返回
        </NvMobileButton>
        <h1 class="text-lg font-semibold text-foreground">维修工单详情</h1>
      </div>
    </template>

    <div
      v-if="!scopeReady || !requestedWorkOrderId"
      class="m-4 rounded-xl border border-dashed border-border bg-card px-4 py-8 text-center"
    >
      <h2 class="font-semibold text-foreground">工单不可查看</h2>
      <p class="mt-2 text-sm text-muted-foreground">
        当前维修人员、组织/环境、读取权限或工单标识未就绪，未发起详情查询。
      </p>
    </div>

    <div v-else-if="pending && !workOrder" class="p-8 text-center text-sm text-muted-foreground">
      正在重新校验工单详情…
    </div>

    <div v-else-if="hasFailedResponse || error || !workOrder" class="space-y-3 p-4">
      <h2 class="font-semibold text-foreground">工单不可查看</h2>
      <p class="text-sm text-muted-foreground">
        工单可能已失效、超出当前维修人员 Self 范围，或服务端未成功返回。
      </p>
      <RetryableListError
        :error="error"
        :pending="pending"
        fallback="工单详情校验失败，请重试。"
        test-id="maintenance-work-order-detail-error"
        @retry="refresh"
      />
    </div>

    <MaintenanceWorkOrderDetail v-else :work-order="workOrder" :device="device" />
  </NvAppShellMobile>
</template>
