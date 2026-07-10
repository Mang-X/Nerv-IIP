<script setup lang="ts">
import { describeRequestError } from '@/api/request-timeout'
import { useBusinessMaintenance } from '@/composables/useBusinessMaintenance'
import {
  maintenancePriorityLabel,
  maintenancePriorityLabels,
  maintenanceWorkOrderStatusLabel,
  repairOrderFlow,
  type RepairCtx,
} from '@nerv-iip/business-core'
import { NvAppShellMobile, NvListRow, NvMobileResult, NvScanBar } from '@nerv-iip/ui-mobile'
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

// ---- 设备上下文来源优先级：route query 预填 > 扫码 > 手输 -------------------------
const queryDeviceAssetId = computed(() => {
  const v = route.query.deviceAssetId
  return typeof v === 'string' ? v : ''
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

// 优先级选项（中文经 maintenancePriorityLabel）。
const priorityOptions = Object.keys(maintenancePriorityLabels)

// 流程驱动的校验：deviceAssetId + priority 必填（故障描述建议但非必填）。
const valid = computed(() => repairOrderFlow.progress(form).completed >= 2)

type Phase = 'form' | 'success' | 'error'
const phase = ref<Phase>('form')
// 保留原始错误对象，分类为"结果不确定（超时/离线/网络）"或"确定失败（服务端已响应）"。
const lastError = ref<unknown>(null)
const errorInfo = computed(() => describeRequestError(lastError.value, '报修提交失败'))
// 报修端点无服务端幂等键：结果不确定时重试会重复创建工单 → 不给重试，改引导去列表核实。
const errorDescription = computed(() =>
  errorInfo.value.indeterminate
    ? `${errorInfo.value.message}。请勿重复提交，返回后在"近期维修工单"中核实是否已创建。`
    : errorInfo.value.message,
)
const workOrdersErrorMessage = computed(
  () => describeRequestError(workOrdersError.value, '维修工单加载失败，请稍后重试。').message,
)

// ScanBar 在浮层（成功/失败 Result）展示时停止抢焦。
const scanActive = computed(() => phase.value === 'form')

function onScan(value: string) {
  form.deviceAssetId = value
}

async function submit() {
  if (!valid.value || createPending.value) return
  lastError.value = null
  try {
    await createWorkOrder({
      deviceAssetId: form.deviceAssetId as string,
      priority: form.priority as string,
      assetUnavailableReason: form.assetUnavailableReason,
      ...(sourceAlarmId.value ? { sourceAlarmId: sourceAlarmId.value } : {}),
    })
    phase.value = 'success'
  } catch (e) {
    lastError.value = e
    phase.value = 'error'
  }
}

function resetForm() {
  // 成功后清空，避免重复提交相同工单（端点无服务端幂等）。
  form.deviceAssetId = queryDeviceAssetId.value
  form.priority = ''
  form.assetUnavailableReason = ''
  lastError.value = null
  phase.value = 'form'
}

function goBack() {
  router.push('/').catch(() => {})
}

// 确定失败（服务端已响应错误、无副作用）：可安全回表单重试。
function retry() {
  lastError.value = null
  phase.value = 'form'
}

// 结果不确定（超时/离线/网络中断，工单可能已创建）：刷新列表回表单让用户核实，绝不自动重提。
function verifyList() {
  void refreshWorkOrders()
  lastError.value = null
  phase.value = 'form'
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
        <button
          type="button"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="resetForm"
        >
          继续报修
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="goBack"
        >
          返回
        </button>
      </template>
    </NvMobileResult>

    <NvMobileResult
      v-else-if="phase === 'error'"
      status="error"
      :title="errorInfo.indeterminate ? '提交结果未知' : '报修提交失败'"
      :description="errorDescription"
    >
      <template #actions>
        <!-- 确定失败（服务端已响应、无副作用）→ 安全重试；结果不确定 → 只给核实入口，不重试。 -->
        <button
          v-if="!errorInfo.indeterminate"
          type="button"
          data-testid="retry"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="retry"
        >
          重试
        </button>
        <button
          v-else
          type="button"
          data-testid="verify-list"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="verifyList"
        >
          查看维修工单
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="goBack"
        >
          返回
        </button>
      </template>
    </NvMobileResult>

    <div v-else class="space-y-6 p-4">
      <!-- 新建报修 -->
      <section class="space-y-3">
        <h2 class="text-sm font-medium text-muted-foreground">新建报修</h2>

        <NvScanBar placeholder="扫描设备码" :active="scanActive" @scan="onScan" />

        <label class="block space-y-1">
          <span class="text-sm text-foreground">设备资产编号</span>
          <input
            data-testid="device-input"
            v-model="form.deviceAssetId"
            placeholder="扫描或手动输入设备资产编号"
            autocapitalize="off"
            spellcheck="false"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-4 text-base text-foreground outline-none focus:border-brand"
          />
        </label>

        <label class="block space-y-1">
          <span class="text-sm text-foreground">优先级</span>
          <select
            data-testid="priority-select"
            v-model="form.priority"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-4 text-base text-foreground outline-none focus:border-brand"
          >
            <option value="" disabled>请选择优先级</option>
            <option v-for="p in priorityOptions" :key="p" :value="p">
              {{ maintenancePriorityLabel(p) }}
            </option>
          </select>
        </label>

        <label class="block space-y-1">
          <span class="text-sm text-foreground">故障描述（建议填写）</span>
          <textarea
            data-testid="reason-input"
            v-model="form.assetUnavailableReason"
            rows="3"
            placeholder="描述故障现象，便于维修人员处理"
            class="w-full rounded-lg border border-border bg-card px-4 py-2 text-base text-foreground outline-none focus:border-brand"
          />
        </label>

        <button
          data-testid="submit"
          type="button"
          :disabled="!valid || createPending"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
          @click="submit"
        >
          {{ createPending ? '提交中…' : '提交报修' }}
        </button>
      </section>

      <!-- 近期维修工单 -->
      <section class="space-y-2">
        <h2 class="text-sm font-medium text-muted-foreground">近期维修工单</h2>

        <div
          v-if="workOrdersError"
          data-testid="work-orders-error"
          class="space-y-2 rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm"
        >
          <p class="text-destructive">{{ workOrdersErrorMessage }}</p>
          <button
            type="button"
            data-testid="work-orders-retry"
            :disabled="workOrdersPending"
            class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground disabled:opacity-60"
            @click="refreshWorkOrders"
          >
            重试
          </button>
        </div>

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
          暂无维修工单
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
  </NvAppShellMobile>
</template>
