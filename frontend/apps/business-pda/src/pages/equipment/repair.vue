<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import DeviceAssetPicker from '@/components/equipment/DeviceAssetPicker.vue'
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
  NvMobileResult,
  NvScanBar,
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
  workOrdersPending,
  workOrdersError,
  refreshWorkOrders,
  createWorkOrder,
  createPending,
} = useBusinessMaintenance()

// 报修端点无服务端幂等键 → 写结果状态机由共享 composable 统一：结果不确定（超时/网络中断）
// 不给盲目重试、引导核实；离线（未发出）与确定业务失败可安全重试。
const { phase, errorTitle, errorDescription, canRetry, run, retry, verify, reset } =
  useNonIdempotentWriteResult({
    failureTitle: '报修提交失败',
    verifyListLabel: '近期维修工单',
    verifyVerb: '创建',
    onVerify: () => {
      void refreshWorkOrders()
    },
  })

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
    !devicePickerOpen.value &&
    !prioritySheetOpen.value &&
    !reasonFocused.value,
)

function onScan(value: string) {
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
  form.deviceAssetId = device.deviceAssetId
  selectedDevice.value = { ...device, source: 'directory' }
}

function onPrioritySelected(priority: string) {
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
  await run(() =>
    createWorkOrder({
      deviceAssetId: form.deviceAssetId as string,
      priority: form.priority as string,
      assetUnavailableReason: form.assetUnavailableReason,
      ...(sourceAlarmId.value ? { sourceAlarmId: sourceAlarmId.value } : {}),
    }),
  )
}

function resetForm() {
  // 成功后清空，避免重复提交相同工单（端点无服务端幂等）。
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
          @click="retry"
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
            class="border-b-0"
            @select="devicePickerOpen = true"
          />
        </div>

        <div class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            data-testid="priority-trigger"
            title="优先级"
            :subtitle="form.priority ? maintenancePriorityLabel(form.priority) : '请选择优先级'"
            class="border-b-0"
            @select="prioritySheetOpen = true"
          />
        </div>

        <label class="block space-y-1">
          <span class="text-sm text-foreground">故障描述（建议填写）</span>
          <textarea
            data-testid="reason-input"
            v-model="form.assetUnavailableReason"
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
      <section class="space-y-2">
        <h2 class="text-sm font-medium text-muted-foreground">近期维修工单</h2>

        <RetryableListError
          v-if="workOrdersError"
          :error="workOrdersError"
          :pending="workOrdersPending"
          fallback="维修工单加载失败，请稍后重试。"
          test-id="work-orders-error"
          @retry="() => refreshWorkOrders()"
        />

        <div
          v-else-if="workOrdersPending"
          class="px-4 py-6 text-center text-sm text-muted-foreground"
        >
          加载中…
        </div>

        <div
          v-else-if="workOrders.length === 0"
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          当前登录范围暂无维修工单
        </div>

        <div v-else class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            v-for="item in workOrders"
            :key="item.workOrderId"
            :title="item.deviceAssetId ?? '未知设备'"
            :subtitle="workOrderSubtitle(item)"
            :interactive="false"
          />
        </div>
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
