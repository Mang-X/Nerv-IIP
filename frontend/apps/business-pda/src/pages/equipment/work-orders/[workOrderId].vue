<script setup lang="ts">
import MaintenanceWorkOrderDetail from './components/MaintenanceWorkOrderDetail.vue'
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
const {
  scopeReady,
  enabled,
  workOrder,
  device,
  identities,
  identityPending,
  identitiesUnavailable,
  pending,
  error,
  hasFailedResponse,
  refresh,
} = useMaintenanceSelfWorkOrderDetail(requestedWorkOrderId)
const sourceContext = computed(() => {
  const sourceAlarmId = route.query.sourceAlarmId
  return typeof sourceAlarmId === 'string' && workOrder.value?.sourceAlarmId === sourceAlarmId
    ? '来源：报警报修创建结果'
    : ''
})

function backToList() {
  router.push('/equipment/work-orders').catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <NvMobileButton
          variant="text"
          size="sm"
          class="min-h-touch min-w-touch"
          aria-label="返回维修工单列表"
          @click="backToList"
        >
          返回
        </NvMobileButton>
        <h1 class="text-lg font-semibold text-foreground">维修工单详情</h1>
      </div>
    </template>

    <div
      v-if="!scopeReady"
      class="m-4 rounded-xl border border-dashed border-border bg-card px-4 py-8 text-center"
    >
      <h2 class="font-semibold text-foreground">工单不可查看</h2>
      <p class="mt-2 text-sm text-muted-foreground">当前账号暂无法查看，请重新登录或联系管理员。</p>
    </div>

    <div
      v-else-if="!enabled"
      class="m-4 rounded-xl border border-dashed border-border bg-card px-4 py-8 text-center"
    >
      <h2 class="font-semibold text-foreground">工单不可查看</h2>
      <p class="mt-2 text-sm text-muted-foreground">工单标识无效，请返回列表重新选择。</p>
    </div>

    <div v-else-if="pending && !workOrder" class="p-8 text-center text-sm text-muted-foreground">
      正在读取工单详情…
    </div>

    <div v-else-if="hasFailedResponse || error || !workOrder" class="space-y-3 p-4">
      <h2 class="font-semibold text-foreground">工单不可查看</h2>
      <p class="text-sm text-muted-foreground">当前账号不可查看该工单，或工单已失效。</p>
      <RetryableListError
        :error="error"
        :pending="pending"
        fallback="工单详情读取失败，请重试。"
        test-id="maintenance-work-order-detail-error"
        @retry="refresh"
      />
    </div>

    <div v-else>
      <p
        v-if="sourceContext"
        data-testid="maintenance-source-context"
        class="mx-4 mt-4 rounded-lg border border-brand/20 bg-brand/5 px-3 py-2 text-sm text-muted-foreground"
      >
        {{ sourceContext }}
      </p>
      <MaintenanceWorkOrderDetail
        :work-order="workOrder"
        :device="device"
        :identities="identities"
        :identity-pending="identityPending"
        :identities-unavailable="identitiesUnavailable"
      />
      <div v-if="identitiesUnavailable" class="px-4 pb-4">
        <NvMobileButton
          data-testid="refresh-maintenance-identities"
          variant="outline"
          class="min-h-touch w-full"
          :disabled="identityPending"
          @click="refresh"
        >
          刷新身份资料
        </NvMobileButton>
      </div>
    </div>
  </NvAppShellMobile>
</template>
