<script setup lang="ts">
import TaskListShell from '@/components/task-list/TaskListShell.vue'
import DeviceAssetPicker from '@/components/equipment/DeviceAssetPicker.vue'
import DowntimeReasonPicker from '@/components/equipment/DowntimeReasonPicker.vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useBusinessMaintenance } from '@/composables/useBusinessMaintenance'
import {
  useMaintenanceDowntimeReasonDirectory,
  type DowntimeReasonOption,
} from '@/composables/useMaintenanceDowntimeReasonDirectory'
import { confirmedMaintenanceCreateWorkOrderId } from '@/composables/maintenanceCreateReceipt'
import { useMaintenanceSelfWorkOrderDetail } from '@/composables/useMaintenanceSelfWorkOrders'
import { useNonIdempotentWriteResult } from '@/composables/useNonIdempotentWriteResult'
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import {
  maintenancePriorityLabel,
  maintenanceWorkOrderStatusLabel,
  maintenanceWorkOrderStatusOptions,
  repairOrderFlow,
  type RepairCtx,
} from '@nerv-iip/business-core'
import {
  NvActionSheet,
  NvAppShellMobile,
  NvListRow,
  NvMobileButton,
  NvMobileDropdownMenu,
  NvMobileDropdownMenuItem,
  NvMobileResult,
  NvScanBar,
  NvSearchBar,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '故障报修',
  },
})

const route = useRoute()
const router = useRouter()

const {
  workOrders,
  workOrderFilters,
  workOrdersLoaded,
  workOrdersLoadingMore,
  workOrdersRefreshing,
  workOrdersLoadMoreError,
  loadMoreWorkOrders,
  workOrdersPending,
  workOrdersError,
  refreshWorkOrders,
  createWorkOrder,
  createPending,
  canReadWorkOrderDetail,
  organizationId,
  environmentId,
  scopeReady,
  workOrdersTotal,
  workOrdersLastUpdatedAt,
  workOrdersHasSuccessfulResponse,
  workOrdersHasFailedResponse,
} = useBusinessMaintenance()
const maintenanceScope = computed(() =>
  scopeReady.value ? '当前登录组织 / 当前业务环境' : '组织/环境范围未就绪',
)
const maintenanceTotal = computed(() => workOrdersTotal.value)
const workOrderListError = computed(
  () =>
    workOrdersError.value ??
    (workOrdersHasFailedResponse.value ? '维修工单服务未成功返回' : undefined),
)
const workOrderFilterState = computed(() => ({
  status: workOrderFilters.status ?? '',
  keyword: workOrderFilters.keyword ?? '',
}))
const workOrderKeywordModel = computed({
  get: () => workOrderFilters.keyword ?? '',
  set: (value: string) => {
    workOrderFilters.keyword = value.trim() || undefined
  },
})
const workOrderStatusModel = computed<string | number>({
  get: () => workOrderFilters.status ?? '',
  set: (value) => {
    workOrderFilters.status = String(value) || undefined
  },
})
const workOrderStatusOptions = [
  { label: '全部状态', value: '' },
  ...maintenanceWorkOrderStatusOptions,
]

function restoreWorkOrderState(state: { filters: Record<string, unknown> }) {
  workOrderFilters.status = String(state.filters.status ?? '') || undefined
  workOrderFilters.keyword = String(state.filters.keyword ?? '') || undefined
}

// 报修端点持久化逐操作幂等键；超时后仍复用同一键重试，服务端返回原工单而不重复创建。
const {
  phase,
  errorTitle,
  errorDescription,
  canRetry,
  isOutcomeIndeterminate,
  run,
  retry,
  verify,
  reset,
} = useNonIdempotentWriteResult({
  failureTitle: '报修提交失败',
  verifyListLabel: '近期维修工单',
  verifyVerb: '创建',
  idempotent: true,
  onVerify: () => {
    void refreshWorkOrders()
  },
})
const operationKey = ref('')
const operationFingerprint = ref('')
const submittedIntent = ref<RepairIntent | null>(null)
const intentLocked = ref(false)
const createdWorkOrderId = ref('')
const {
  authoritativeWorkOrder: confirmedCreatedWorkOrder,
  authoritativeHasSuccessfulResponse: createdDetailHasSuccessfulResponse,
  authoritativePending: createdDetailPending,
  authoritativeHasFailedResponse: createdDetailHasFailedResponse,
  deviceHasFailedResponse: createdDeviceHasFailedResponse,
  refresh: refreshCreatedWorkOrder,
} = useMaintenanceSelfWorkOrderDetail(createdWorkOrderId)
const canViewCreatedWorkOrder = computed(
  () =>
    canReadWorkOrderDetail.value &&
    Boolean(createdWorkOrderId.value) &&
    createdDetailHasSuccessfulResponse.value &&
    confirmedCreatedWorkOrder.value?.workOrderId === createdWorkOrderId.value,
)
// 成功页复述"这次到底登没登设备占用"：决策时刻已经告知过，但结果页只说"报修已提交"
// 会让"没记"这件事无声通过——尤其目录读失败时用户是被迫走的 null 路径。
const createdAssetUnavailableState = computed(() => {
  const code = submittedIntent.value?.assetUnavailableReasonCode
  if (!code) return '本次未登记设备占用原因，只创建了维修工单。'
  const name = selectedReason.value?.code === code ? selectedReason.value.name : code
  return `已登记设备占用原因：${name}（${code}），工单完工时自动释放。`
})
const createdAssignmentState = computed(() => {
  if (createdDetailPending.value) return '正在核验工单指派状态…'
  if (canViewCreatedWorkOrder.value) return '已确认工单指派给当前维修人员，可查看详情。'
  if (createdDetailHasFailedResponse.value) return '工单指派状态暂不可核实，请稍后重试。'
  return '尚未确认工单指派给当前账号，当前暂不可查看详情。'
})

// ---- 设备上下文来源优先级：route query 预填 > 扫码 > 目录选择 -----------------------
const queryDeviceAssetId = computed(() => {
  const v = route.query.deviceAssetId
  return typeof v === 'string' ? v.trim() : ''
})
const routeSourceAlarmId = computed(() => {
  const v = route.query.sourceAlarmId
  return typeof v === 'string' && v.trim().length > 0 ? v.trim() : undefined
})
const sourceAlarmId = ref(routeSourceAlarmId.value)

// 报修表单 = repairOrderFlow 的上下文（selectDevice → fillDetails → create）。
const form = reactive<RepairCtx>({
  deviceAssetId: queryDeviceAssetId.value,
  priority: '',
})

// 设备占用原因 = Maintenance 权威 `downtime-reason` 目录里的一条；`null` 表示明确不登记。
// 目录条目的 `code` 一路原样传到 v2 请求，中间不 trim、不改大小写。
const selectedReason = ref<DowntimeReasonOption | null>(null)

type DeviceSource = 'route' | 'scan' | 'directory'
type RepairIntent = {
  deviceAssetId: string
  priority: string
  assetUnavailableReasonCode: string | null
  sourceAlarmId?: string
}
type SelectedDevice = BusinessConsoleResourceItem & {
  deviceAssetId: string
  source: DeviceSource
}

const selectedDevice = ref<SelectedDevice | null>(
  queryDeviceAssetId.value
    ? {
        deviceAssetId: queryDeviceAssetId.value,
        displayName: queryDeviceAssetId.value,
        source: 'route',
      }
    : null,
)
const devicePickerOpen = ref(false)
const prioritySheetOpen = ref(false)
const reasonPickerOpen = ref(false)
const {
  reasonOptions,
  reasonsTotal,
  reasonsTruncated,
  state: reasonDirectoryState,
  stateMessage: reasonDirectoryMessage,
  canSelectReason,
  search: searchReasons,
  refreshReasons,
} = useMaintenanceDowntimeReasonDirectory()
// 目录读不出来时**只报错**：既不回退自由文本，也不塞伪默认码。表单上仍能提交，
// 但唯一可达的原因值是 null（不登记设备不可用）。
const reasonDirectoryBroken = computed(
  () =>
    reasonDirectoryState.value === 'forbidden' ||
    reasonDirectoryState.value === 'failed' ||
    reasonDirectoryState.value === 'unavailable',
)
const reasonTriggerSubtitle = computed(() => {
  if (selectedReason.value) return `${selectedReason.value.name}（${selectedReason.value.code}）`
  if (reasonDirectoryBroken.value) return reasonDirectoryMessage.value
  return '不登记设备不可用'
})

function applyRouteRepairPair(deviceAssetId: string, alarmId: string | undefined) {
  form.deviceAssetId = deviceAssetId
  sourceAlarmId.value = alarmId
  selectedDevice.value = deviceAssetId
    ? {
        deviceAssetId,
        displayName: deviceAssetId,
        source: 'route',
      }
    : null
}

watch(
  [queryDeviceAssetId, routeSourceAlarmId],
  ([deviceAssetId, alarmId]) => {
    if (phase.value !== 'form' || intentLocked.value) return
    applyRouteRepairPair(deviceAssetId, alarmId)
  },
  { flush: 'sync' },
)

// 建单契约只开放高/中/低三档；完整生产词表还包含自动开单读面值，不能反向扩张写入选项。
const maintenanceCreatePriorityValues = ['high', 'medium', 'low'] as const
const priorityOptions = maintenanceCreatePriorityValues.map((value) => ({
  value,
  label: maintenancePriorityLabel(value),
}))

// 流程驱动的校验：deviceAssetId + priority 必填（设备占用原因非必填，填写即登记设备停机）。
const valid = computed(() => repairOrderFlow.progress(form).completed >= 2)

// ScanBar 在浮层（成功/失败 Result）展示时停止抢焦。
const scanActive = computed(
  () =>
    phase.value === 'form' &&
    !intentLocked.value &&
    !devicePickerOpen.value &&
    !prioritySheetOpen.value &&
    !reasonPickerOpen.value,
)

function onScan(value: string) {
  if (intentLocked.value) return
  const deviceAssetId = value.trim()
  if (!deviceAssetId) return
  form.deviceAssetId = deviceAssetId
  sourceAlarmId.value = undefined
  selectedDevice.value = {
    deviceAssetId,
    displayName: deviceAssetId,
    source: 'scan',
  }
}

function onDeviceSelected(device: BusinessConsoleResourceItem & { deviceAssetId: string }) {
  if (intentLocked.value) return
  const deviceCode = device.code?.trim()
  if (!deviceCode) return
  form.deviceAssetId = deviceCode
  sourceAlarmId.value = undefined
  selectedDevice.value = { ...device, source: 'directory' }
}

function onPrioritySelected(priority: string) {
  if (intentLocked.value) return
  if (maintenanceCreatePriorityValues.some((value) => value === priority)) {
    form.priority = priority
  }
}

// 目录不可用时仍允许打开抽屉：抽屉里只有明确错误态与「不登记设备不可用」，没有任何
// 自由文本入口。把入口整个禁掉反而让已选原因无法撤回，也看不到归因。
function onReasonSelected(reason: DowntimeReasonOption | null) {
  if (intentLocked.value) return
  selectedReason.value = reason
}

const selectedDeviceTitle = computed(
  () =>
    selectedDevice.value?.displayName?.trim() ||
    selectedDevice.value?.code?.trim() ||
    selectedDevice.value?.deviceAssetId ||
    '请选择设备',
)

const selectedDeviceSubtitle = computed(() => {
  const device = selectedDevice.value
  if (!device) return '可按名称或编码搜索，也可直接扫码'
  if (device.source === 'route') {
    return sourceAlarmId.value ? `报警上下文 · ${sourceAlarmId.value}` : '来自页面上下文'
  }
  if (device.source === 'scan') return `来自扫码 · ${device.deviceAssetId}`
  const context = [
    device.code?.trim() !== selectedDeviceTitle.value ? device.code?.trim() : undefined,
    device.workshopCode,
    device.lineCode,
    device.workCenterCode,
    device.stationCode,
  ]
    .filter((part): part is string => Boolean(part?.trim()))
    .filter((part, index, parts) => parts.indexOf(part) === index)
  return context.join(' · ') || device.deviceAssetId
})

async function submit() {
  if (!valid.value || createPending.value) return
  const draftIntent: RepairIntent = {
    deviceAssetId: form.deviceAssetId as string,
    priority: form.priority as string,
    assetUnavailableReasonCode: selectedReason.value ? selectedReason.value.code : null,
    ...(sourceAlarmId.value ? { sourceAlarmId: sourceAlarmId.value } : {}),
  }
  const intent = intentLocked.value && submittedIntent.value ? submittedIntent.value : draftIntent
  const fingerprint = JSON.stringify(intent)
  if (
    !operationKey.value ||
    (!intentLocked.value &&
      operationFingerprint.value.length > 0 &&
      operationFingerprint.value !== fingerprint)
  ) {
    operationKey.value = makeIdempotencyKey()
  }
  operationFingerprint.value = fingerprint
  submittedIntent.value = intent
  await run(async () => {
    const response = await createWorkOrder({
      ...intent,
      idempotencyKey: operationKey.value,
    })
    createdWorkOrderId.value = confirmedMaintenanceCreateWorkOrderId(response)
    return response
  })
}

function retrySubmission() {
  intentLocked.value = isOutcomeIndeterminate.value
  retry()
}

function resetForm() {
  // 成功后开启一个新的操作意图；失败重试不会经过这里，因此继续复用原键。
  operationKey.value = ''
  operationFingerprint.value = ''
  submittedIntent.value = null
  intentLocked.value = false
  createdWorkOrderId.value = ''
  applyRouteRepairPair(queryDeviceAssetId.value, routeSourceAlarmId.value)
  form.priority = ''
  selectedReason.value = null
  reset()
}

function goBack() {
  router.push('/').catch(() => {})
}

function viewCreatedWorkOrder() {
  if (!canViewCreatedWorkOrder.value) return
  const confirmedAlarmId = submittedIntent.value?.sourceAlarmId
  router
    .push({
      path: `/equipment/work-orders/${encodeURIComponent(createdWorkOrderId.value)}`,
      ...(confirmedAlarmId ? { query: { sourceAlarmId: confirmedAlarmId } } : {}),
    })
    .catch(() => {})
}

async function recheckCreatedWorkOrderAssignment() {
  if (!createdWorkOrderId.value || !canReadWorkOrderDetail.value || createdDetailPending.value)
    return
  await refreshCreatedWorkOrder()
}

function workOrderSubtitle(item: { priority?: string; status?: string; openedAtUtc?: string }) {
  const parts = [
    `优先级 ${maintenancePriorityLabel(item.priority)}`,
    maintenanceWorkOrderStatusLabel(item.status),
  ]
  if (item.openedAtUtc) {
    parts.push(new Date(item.openedAtUtc).toLocaleString('zh-CN'))
  }
  return parts.join(' · ')
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">故障报修</h1>
      </div>
    </template>

    <!-- 成功 / 失败：离场态（清空表单，防重复提交） -->
    <NvMobileResult
      v-if="phase === 'success'"
      status="success"
      title="报修已提交"
      description="维修工单已创建，正在等待派工。"
    >
      <template #actions>
        <p
          data-testid="created-work-order-asset-unavailable-state"
          class="text-sm leading-6 text-muted-foreground"
        >
          {{ createdAssetUnavailableState }}
        </p>
        <p
          data-testid="created-work-order-assignment-state"
          class="text-sm leading-6 text-muted-foreground"
        >
          {{ createdAssignmentState }}
        </p>
        <p
          v-if="createdDeviceHasFailedResponse"
          data-testid="created-work-order-device-state"
          class="text-sm leading-6 text-muted-foreground"
        >
          设备资料暂不可用，不影响已确认的工单指派结果。
        </p>
        <NvMobileButton
          v-if="!canViewCreatedWorkOrder && canReadWorkOrderDetail && createdWorkOrderId"
          data-testid="recheck-created-work-order-assignment"
          variant="outline"
          size="lg"
          block
          :disabled="createdDetailPending"
          @click="recheckCreatedWorkOrderAssignment"
        >
          {{ createdDetailPending ? '正在核验指派状态…' : '重新核验指派状态' }}
        </NvMobileButton>
        <NvMobileButton
          v-if="canViewCreatedWorkOrder"
          data-testid="view-created-work-order"
          variant="primary"
          size="lg"
          block
          @click="viewCreatedWorkOrder"
        >
          查看工单详情
        </NvMobileButton>
        <NvMobileButton variant="primary" size="lg" block @click="resetForm">
          继续报修
        </NvMobileButton>
        <NvMobileButton variant="outline" size="lg" block @click="goBack"> 返回 </NvMobileButton>
      </template>
    </NvMobileResult>

    <NvMobileResult
      v-else-if="phase === 'error'"
      status="error"
      :title="errorTitle"
      :description="errorDescription"
    >
      <template #actions>
        <!-- 可安全重试（离线未发出 / 服务端已响应）→ 重试；结果不确定 → 只给核实入口。 -->
        <NvMobileButton
          v-if="canRetry"
          data-testid="retry"
          variant="primary"
          size="lg"
          block
          @click="retrySubmission"
        >
          重试
        </NvMobileButton>
        <NvMobileButton
          v-else
          data-testid="verify-list"
          variant="primary"
          size="lg"
          block
          @click="verify"
        >
          查看维修工单
        </NvMobileButton>
        <NvMobileButton variant="outline" size="lg" block @click="goBack"> 返回 </NvMobileButton>
      </template>
    </NvMobileResult>

    <div v-else class="space-y-6 p-4">
      <!-- 新建报修 -->
      <section class="space-y-3">
        <h2 class="text-sm font-medium text-muted-foreground">新建报修</h2>

        <NvScanBar placeholder="扫描设备码" :active="scanActive" @scan="onScan" />

        <div class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            data-testid="device-trigger"
            :title="selectedDeviceTitle"
            :subtitle="selectedDeviceSubtitle"
            :interactive="!intentLocked"
            class="border-b-0"
            @select="intentLocked ? undefined : (devicePickerOpen = true)"
          />
        </div>

        <div class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            data-testid="priority-trigger"
            title="优先级"
            :subtitle="form.priority ? maintenancePriorityLabel(form.priority) : '请选择优先级'"
            :interactive="!intentLocked"
            class="border-b-0"
            @select="intentLocked ? undefined : (prioritySheetOpen = true)"
          />
        </div>

        <div class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            data-testid="reason-trigger"
            title="设备占用原因（选择即登记设备停机）"
            :subtitle="reasonTriggerSubtitle"
            :interactive="!intentLocked"
            class="border-b-0"
            @select="intentLocked ? undefined : (reasonPickerOpen = true)"
          />
        </div>
        <p
          v-if="reasonDirectoryBroken"
          role="alert"
          data-testid="reason-directory-blocked"
          class="text-sm text-destructive"
        >
          {{ reasonDirectoryMessage }}；当前只能提交不登记设备停机的报修，不能手工填写原因。
        </p>
        <span class="block text-xs text-muted-foreground">
          选择原因后从提交时刻登记该设备不可用并计入产能影响，工单完工时自动释放；选择「不登记设备不可用」则只提交报修、不登记占用。
        </span>

        <NvMobileButton
          data-testid="submit"
          :disabled="!valid || createPending"
          variant="primary"
          size="lg"
          block
          @click="submit"
        >
          {{ createPending ? '提交中…' : '提交报修' }}
        </NvMobileButton>
      </section>

      <!-- 近期维修工单 -->
      <section class="h-[70vh] min-h-[32rem] space-y-2">
        <h2 class="text-sm font-medium text-muted-foreground">近期维修工单</h2>
        <TaskListShell
          state-key="maintenance-work-orders"
          :scope="maintenanceScope"
          source="维修工单服务（组织/环境范围，暂不支持按维修人员归属筛选）"
          :loaded="workOrdersLoaded"
          :total="maintenanceTotal"
          :updated-at="workOrdersLastUpdatedAt"
          :pending="workOrdersPending"
          :refreshing="workOrdersRefreshing"
          :loading-more="workOrdersLoadingMore"
          :error="workOrderListError"
          :load-more-error="workOrdersLoadMoreError"
          error-test-id="work-orders-error"
          failure-explanation="维修工单服务未成功返回，请刷新重试。"
          :filter-state="workOrderFilterState"
          :empty-description="
            scopeReady
              ? '当前组织/环境范围暂无维修工单；暂不支持按维修人员归属筛选，空态不代表个人工单。'
              : '缺少组织或环境范围，未发起查询。'
          "
          @refresh="refreshWorkOrders"
          @retry="refreshWorkOrders"
          @load-more="loadMoreWorkOrders"
          @retry-load-more="loadMoreWorkOrders"
          @restore="restoreWorkOrderState"
        >
          <template #filters>
            <div class="space-y-2 p-3">
              <NvSearchBar
                v-model="workOrderKeywordModel"
                data-testid="work-order-keyword"
                aria-label="维修工单关键字"
                placeholder="搜索设备、来源或负责人"
              />
              <NvMobileDropdownMenu>
                <NvMobileDropdownMenuItem
                  v-model="workOrderStatusModel"
                  data-testid="work-order-status"
                  title="维修工单状态"
                  :options="workOrderStatusOptions"
                />
              </NvMobileDropdownMenu>
            </div>
          </template>

          <div class="overflow-hidden rounded-lg border border-border">
            <NvListRow
              v-for="item in workOrders"
              :key="item.workOrderId"
              :title="item.deviceAssetId ?? '未知设备'"
              :subtitle="workOrderSubtitle(item)"
              :interactive="false"
            />
          </div>
        </TaskListShell>
      </section>
    </div>

    <DeviceAssetPicker v-model:open="devicePickerOpen" @select="onDeviceSelected" />
    <DowntimeReasonPicker
      v-model:open="reasonPickerOpen"
      :selected-code="selectedReason?.code ?? null"
      :options="reasonOptions"
      :state="reasonDirectoryState"
      :state-message="reasonDirectoryMessage"
      :can-select="canSelectReason"
      :truncated="reasonsTruncated"
      :total="reasonsTotal"
      @select="onReasonSelected"
      @search="searchReasons"
      @retry="refreshReasons"
    />
    <NvActionSheet
      v-model:open="prioritySheetOpen"
      title="选择优先级"
      :actions="priorityOptions"
      @select="onPrioritySelected"
    />
  </NvAppShellMobile>
</template>
