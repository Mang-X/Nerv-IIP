<script setup lang="ts">
import MaintenanceWorkOrderDetail from './components/MaintenanceWorkOrderDetail.vue'
import RetryableListError from '@/components/RetryableListError.vue'
import { useMaintenanceSelfWorkOrderDetail } from '@/composables/maintenance'
import { normalizeMaintenancePublicReference } from '@/composables/maintenance/maintenancePublicIds'
import { NvAppShellMobile, NvMobileButton } from '@nerv-iip/ui-mobile'
import { useMaintenanceLifecycle } from '@/composables/maintenance/useMaintenanceLifecycle'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '维修工单详情' } })

const route = useRoute('/equipment/work-orders/[workOrderId]')
const router = useRouter()
const requestedWorkOrderId = computed(() => {
  const value = route.params.workOrderId
  return typeof value === 'string' ? value.trim() : ''
})
const detail = useMaintenanceSelfWorkOrderDetail(requestedWorkOrderId)
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
} = detail
const {
  action,
  available,
  busy,
  form,
  message,
  pending: pendingAction,
  select,
  submit,
} = useMaintenanceLifecycle(detail)
const actionLabels = { accept: '接单', start: '开工', complete: '完工' }
const sourceContext = computed(() => {
  const requestedSourceAlarmId = normalizeMaintenancePublicReference(route.query.sourceAlarmId)
  const authoritativeSourceAlarmId = normalizeMaintenancePublicReference(
    workOrder.value?.sourceAlarmId,
  )
  return requestedSourceAlarmId && requestedSourceAlarmId === authoritativeSourceAlarmId
    ? '由报警报修创建'
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
      <section v-if="available.length || action || message" class="space-y-3 px-4 pb-4">
        <div v-if="available.length" class="flex flex-wrap gap-2">
          <NvMobileButton
            v-for="item in available"
            :key="item"
            :disabled="busy || Boolean(pendingAction)"
            @click="select(item)"
          >
            {{ actionLabels[item] }}
          </NvMobileButton>
        </div>
        <form v-if="action" class="space-y-3" @submit.prevent="submit">
          <fieldset :disabled="busy || Boolean(pendingAction)" class="grid gap-3">
            <legend class="font-medium">{{ actionLabels[action] }}</legend>
            <label class="grid gap-1">
              操作原因
              <textarea
                v-model="form.reason"
                required
                maxlength="500"
                class="rounded-md border bg-background p-2"
              />
            </label>
            <template v-if="action === 'complete'">
              <label class="grid gap-1">
                处理结果
                <textarea
                  v-model="form.result"
                  required
                  maxlength="1000"
                  class="rounded-md border bg-background p-2"
                />
              </label>
              <label class="grid gap-1">
                停机原因编码
                <input
                  v-model="form.downtimeReasonCode"
                  required
                  maxlength="100"
                  class="rounded-md border bg-background p-2"
                />
              </label>
              <label class="grid gap-1">
                停机时长（分钟）
                <input
                  v-model="form.downtime"
                  inputmode="numeric"
                  required
                  class="rounded-md border bg-background p-2"
                />
              </label>
              <label class="grid gap-1">
                实际工时（分钟）
                <input
                  v-model="form.labor"
                  inputmode="numeric"
                  required
                  class="rounded-md border bg-background p-2"
                />
              </label>
            </template>
          </fieldset>
          <NvMobileButton type="submit" :disabled="busy" class="w-full">
            {{ pendingAction ? '重试确认' : `提交${actionLabels[action]}` }}
          </NvMobileButton>
        </form>
        <p v-if="message" role="status" class="text-sm text-muted-foreground">{{ message }}</p>
      </section>
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
