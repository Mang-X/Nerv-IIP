<script setup lang="ts">
import TaskListShell from '@/components/task-list/TaskListShell.vue'
import DeviceAssetPicker from '@/components/equipment/DeviceAssetPicker.vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useBusinessMaintenance } from '@/composables/useBusinessMaintenance'
import { useNonIdempotentWriteResult } from '@/composables/useNonIdempotentWriteResult'
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import {
  maintenancePriorityLabel,
  maintenancePriorityLabels,
  maintenanceWorkOrderStatusLabel,
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
  type DropdownOption,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, ref } from 'vue'
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
const workOrderStatusOptions: DropdownOption[] = [
  { label: '全部状态', value: '' },
  { label: '待处理', value: 'open' },
  { label: '处理中', value: 'inProgress' },
  { label: '已完成', value: 'completed' },
  { label: '已取消', value: 'cancelled' },
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

// ---- 设备上下文来源优先级：route query 预填 > 扫码 > 目录选择 -----------------------
const queryDeviceAssetId = computed(() => {
  const v = route.query.deviceAssetId
  return typeof v === 'string' ? v.trim() : ''
})
const sourceAlarmId = computed(() => {
  const v = route.query.sourceAlarmId
  return typeof v === 'string' && v.length > 0 ? v : undefined
})

// 报修表单 = repairOrderFlow 的上下文（selectDevice → fillDetails → create）。
const form = reactive<RepairCtx & { assetUnavailableReason: string }>({
  deviceAssetId: queryDeviceAssetId.value,
  priority: '',
  assetUnavailableReason: '',
})

type DeviceSource = 'route' | 'scan' | 'directory'
type RepairIntent = {
  deviceAssetId: string
  priority: string
  assetUnavailableReason: string
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
const reasonFocused = ref(false)

// 优先级选项仅使用 business-core 的三项稳定值，ActionSheet 负责移动选择。
const priorityOptions = Object.keys(maintenancePriorityLabels).map((value) => ({
  value,
  label: maintenancePriorityLabel(value),
}))

// 流程驱动的校验：deviceAssetId + priority 必填（故障描述建议但非必填）。
const valid = computed(() => repairOrderFlow.progress(form).completed >= 2)

// ScanBar 在浮层（成功/失败 Result）展示时停止抢焦。
const scanActive = computed(
  () =>
    phase.value === 'form' &&
    !intentLocked.value &&
    !devicePickerOpen.value &&
    !prioritySheetOpen.value &&
    !reasonFocused.value,
)

function onScan(value: string) {
  if (intentLocked.value) return
  const deviceAssetId = value.trim()
  if (!deviceAssetId) return
  form.deviceAssetId = deviceAssetId
  selectedDevice.value = {
    deviceAssetId,
    displayName: deviceAssetId,
    source: 'scan',
  }
}

function onDeviceSelected(device: BusinessConsoleResourceItem & { deviceAssetId: string }) {
  if (intentLocked.value) return
  form.deviceAssetId = device.deviceAssetId
  selectedDevice.value = { ...device, source: 'directory' }
}

function onPrioritySelected(priority: string) {
  if (intentLocked.value) return
  if (priority in maintenancePriorityLabels) {
    form.priority = priority
  }
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
    assetUnavailableReason: form.assetUnavailableReason,
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
  await run(() =>
    createWorkOrder({
      ...intent,
      idempotencyKey: operationKey.value,
    }),
  )
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
  form.deviceAssetId = queryDeviceAssetId.value
  form.priority = ''
  form.assetUnavailableReason = ''
  selectedDevice.value = queryDeviceAssetId.value
    ? {
        deviceAssetId: queryDeviceAssetId.value,
        displayName: queryDeviceAssetId.value,
        source: 'route',
      }
    : null
  reset()
}

function goBack() {
  router.push('/').catch(() => {})
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
      description="维修工单已创建，等待处理。"
    >
      <template #actions>
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

        <label class="block space-y-1">
          <span class="text-sm text-foreground">故障描述（建议填写）</span>
          <textarea
            data-testid="reason-input"
            v-model="form.assetUnavailableReason"
            :disabled="intentLocked"
            rows="3"
            placeholder="描述故障现象，便于维修人员处理"
            class="min-h-24 w-full scroll-mb-24 rounded-lg border border-border bg-card px-4 py-3 text-base text-foreground outline-none focus:border-brand"
            @focus="reasonFocused = true"
            @blur="reasonFocused = false"
          />
        </label>

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
    <NvActionSheet
      v-model:open="prioritySheetOpen"
      title="选择优先级"
      :actions="priorityOptions"
      @select="onPrioritySelected"
    />
  </NvAppShellMobile>
</template>
