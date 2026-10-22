<script setup lang="ts">
import { NvMobileButton, NvMobileInput, NvMobileToast } from '@nerv-iip/ui-mobile'
import { computed, ref, shallowRef, toRef } from 'vue'
import { describeRequestError } from '@/api/request-timeout'
import { useTelemetryCandidateTarget } from '@/composables/mes/useTelemetryCandidateTarget'
import {
  type MesReportExecutionContext,
  useMesTelemetryProductionReportCandidates,
} from '@/composables/useBusinessMes'
import { useDeviceAssetNames } from '@/composables/useBusinessDeviceDirectory'
import TelemetryCandidateTarget from './TelemetryCandidateTarget.vue'
import { formatOperationDateTime, telemetryCandidateStateLabel } from './operationPresentation'

/** 报工页「遥测待确认」候选区：列出候选、选报工目标、转正或忽略。 */
const props = defineProps<{
  context: MesReportExecutionContext | undefined
  scanPending: boolean
}>()
const editing = defineModel<boolean>('editing', { required: true })

const telemetryQueue = useMesTelemetryProductionReportCandidates()
const telemetryCandidateId = ref<string | null>(null)
const activeTelemetryCandidate = computed(() =>
  telemetryQueue.candidates.value.find(
    (candidate) => candidate.candidateId === telemetryCandidateId.value,
  ),
)
const telemetryTarget = useTelemetryCandidateTarget(
  activeTelemetryCandidate,
  toRef(props, 'context'),
)
// 候选读面只回设备标识，卡片标题的「名称（编码）」回主数据查；查不到就显示占位，不露标识。
const telemetryDeviceNames = useDeviceAssetNames(
  computed(() => telemetryQueue.candidates.value.map((candidate) => candidate.deviceAssetId ?? '')),
)
function telemetryDeviceLabel(deviceAssetId?: string) {
  return telemetryDeviceNames.resolveDeviceName(deviceAssetId) ?? '—'
}
// 输入框（工序搜索、忽略原因）获焦时上报 editing，父级据此让页面扫码框让出焦点（同 equipment/inspect.vue）；
// 按钮获焦不让，免得点过按钮后扫码落空、扫码枪的回车又把卡片展开。
function onTelemetryFocusIn(event: FocusEvent) {
  editing.value =
    event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement
}
const telemetryDismissReason = ref('')
function resetTelemetryAction() {
  telemetryDismissReason.value = ''
}
function toggleTelemetryCandidate(candidateId?: string) {
  resetTelemetryAction()
  telemetryCandidateId.value =
    telemetryCandidateId.value === candidateId ? null : (candidateId ?? null)
}

// 转正 / 忽略的结果必须上屏：成功时候选只是从列表里消失，失败时什么都不显示，操作工会以为没生效而重复点。
const telemetryToast = shallowRef<{ show: boolean; message: string; type: 'success' | 'error' }>({
  show: false,
  message: '',
  type: 'success',
})
function showTelemetryToast(message: string, type: 'success' | 'error') {
  telemetryToast.value = { show: true, message, type }
}
async function promoteTelemetryCandidate(candidateId?: string) {
  if (props.scanPending) return
  const workOrderId = telemetryTarget.target.value?.workOrderId
  const operationTaskId = telemetryTarget.target.value?.operationTaskId
  if (!candidateId || !workOrderId || !operationTaskId) return
  try {
    const reportNo = await telemetryQueue.promote(candidateId, workOrderId, operationTaskId)
    showTelemetryToast(reportNo ? `已转为报工 ${reportNo}` : '已转为报工', 'success')
  } catch (error) {
    showTelemetryToast(describeRequestError(error, '转为报工失败，请重试。').message, 'error')
    return
  }
  telemetryCandidateId.value = null
  resetTelemetryAction()
}
async function dismissTelemetryCandidate(candidateId?: string) {
  if (props.scanPending) return
  if (!candidateId || !telemetryDismissReason.value.trim()) return
  try {
    await telemetryQueue.dismiss(candidateId, telemetryDismissReason.value.trim())
    showTelemetryToast('已忽略这条遥测记录', 'success')
  } catch (error) {
    showTelemetryToast(describeRequestError(error, '忽略失败，请重试。').message, 'error')
    return
  }
  telemetryCandidateId.value = null
  resetTelemetryAction()
}
</script>

<template>
  <section
    v-if="telemetryQueue.candidates.value.length"
    class="space-y-3 rounded-lg border border-warning/40 bg-warning/5 p-3"
    @focusin="onTelemetryFocusIn"
    @focusout="editing = false"
  >
    <div class="flex items-center justify-between">
      <h2 class="font-semibold">遥测待确认</h2>
      <span class="text-xs text-muted-foreground">{{ telemetryQueue.total.value }} 条</span>
    </div>
    <div
      v-for="candidate in telemetryQueue.candidates.value"
      :key="candidate.candidateId"
      class="rounded-lg border border-border bg-card p-3"
    >
      <NvMobileButton
        variant="text"
        block
        class="h-auto min-w-0 flex-col items-start justify-start gap-0.5 p-0 text-left whitespace-normal"
        @click="toggleTelemetryCandidate(candidate.candidateId)"
      >
        <span class="block min-w-0 font-medium break-words"
          >设备 {{ telemetryDeviceLabel(candidate.deviceAssetId) }} ·
          {{ candidate.goodQuantity }} 件 ·
          {{ formatOperationDateTime(candidate.bucketStartUtc) }}</span
        ><span class="block text-xs text-muted-foreground">{{
          telemetryCandidateStateLabel(candidate)
        }}</span>
      </NvMobileButton>
      <div v-if="telemetryCandidateId === candidate.candidateId" class="mt-3 space-y-2">
        <TelemetryCandidateTarget
          v-model:keyword="telemetryTarget.keyword.value"
          v-model:choosing="telemetryTarget.choosing.value"
          :device-asset-id="telemetryTarget.deviceAssetId.value"
          :target="telemetryTarget.target.value"
          :tasks="telemetryTarget.tasks.value"
          :pending="telemetryTarget.pending.value"
          :error="telemetryTarget.error.value"
          :scope-ready="Boolean(context)"
          :device-label="telemetryTarget.deviceLabel"
          @choose="telemetryTarget.choose"
          @retry="telemetryTarget.refresh"
        />
        <NvMobileInput v-model="telemetryDismissReason" placeholder="忽略原因（忽略时必填）" />
        <div class="grid grid-cols-2 gap-2">
          <NvMobileButton
            variant="primary"
            data-testid="telemetry-promote"
            :disabled="
              !telemetryTarget.target.value || telemetryTarget.choosing.value || scanPending
            "
            @click="promoteTelemetryCandidate(candidate.candidateId)"
            >确认转正</NvMobileButton
          ><NvMobileButton
            variant="outline"
            :disabled="!telemetryDismissReason.trim() || scanPending"
            @click="dismissTelemetryCandidate(candidate.candidateId)"
            >忽略</NvMobileButton
          >
        </div>
      </div>
    </div>
  </section>
  <NvMobileToast
    :show="telemetryToast.show"
    :message="telemetryToast.message"
    :type="telemetryToast.type"
    @update:show="telemetryToast = { ...telemetryToast, show: $event }"
  />
</template>
