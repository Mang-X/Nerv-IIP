<script setup lang="ts">
import {
  getBusinessConsoleBarcodePrintBatch,
  type BusinessConsoleRecordProductionReportResponse,
} from '@nerv-iip/api-client'
import { NvMobileButton } from '@nerv-iip/ui-mobile'
import { computed, shallowRef, watch } from 'vue'
import type { MesReportExecutionContext } from '@/composables/useBusinessMes'
import { describeRequestError } from '@/api/request-timeout'

const props = defineProps<{
  receipt: BusinessConsoleRecordProductionReportResponse
  context?: MesReportExecutionContext
}>()
const status = shallowRef(props.receipt.printStatus)
const refreshing = shallowRef(false)
const error = shallowRef('')
let generation = 0
watch(
  () => [props.receipt, props.context],
  () => {
    generation++
    status.value = props.receipt.printStatus
    refreshing.value = false
    error.value = ''
  },
  { flush: 'sync' },
)
async function refreshStatus() {
  if (!props.context || !props.receipt.printBatchId || refreshing.value) return
  const requestGeneration = generation
  const receipt = props.receipt
  refreshing.value = true
  error.value = ''
  try {
    const response = await getBusinessConsoleBarcodePrintBatch({
      path: { printBatchId: receipt.printBatchId! },
      query: {
        organizationId: props.context.organizationId,
        environmentId: props.context.environmentId,
      },
      throwOnError: true,
    })
    if (generation !== requestGeneration) return
    const batch = response.data?.data?.printBatch
    if (
      !response.data?.success ||
      !batch ||
      batch?.printBatchId !== receipt.printBatchId ||
      batch?.productionReportId !== receipt.productionReportId ||
      batch?.productionReportNo !== receipt.reportNo
    )
      throw new Error('未能核对本次报工的标签状态，请重试。')
    status.value = batch.status
  } catch (reason) {
    if (generation === requestGeneration)
      error.value = describeRequestError(
        reason,
        '打印状态读取失败，请重试或联系现场打印负责人。',
      ).message
  } finally {
    if (generation === requestGeneration) refreshing.value = false
  }
}
const printMessage = computed(() => {
  if (props.receipt.printingPreparationPending)
    return '报工已成功，标签准备尚未完成。请重试准备，系统会沿用本次报工和号码；准备完成后方可继续新报工。'
  if (status.value === 'sent-to-printer') return '已发送至打印机，请到现场核对出纸。'
  if (status.value === 'failed')
    return '打印发送失败，请联系现场打印负责人处理本批标签，勿重复报工。'
  if (status.value === 'ready-to-print') return '标签已准备，等待现场打印。'
  if (status.value === 'reserved') return '序列号已预留，标签尚未准备完成。'
  return '尚未取得可确认的打印状态，请联系现场打印负责人核对。'
})
</script>

<template>
  <section
    v-if="receipt.serialNumbers?.length"
    class="w-full space-y-3 rounded-lg border border-border bg-card p-3 text-left"
    aria-label="本次序列号与打印状态"
  >
    <p class="text-sm text-foreground" role="status">{{ printMessage }}</p>
    <p v-if="error" role="alert" class="text-sm text-destructive">{{ error }}</p>
    <NvMobileButton
      v-if="context && receipt.printBatchId && !receipt.printingPreparationPending"
      data-testid="refresh-print-status"
      variant="outline"
      size="lg"
      block
      :disabled="refreshing"
      @click="refreshStatus"
      >{{ refreshing ? '正在核对打印状态…' : '刷新打印状态' }}</NvMobileButton
    >
    <h3 class="text-base font-semibold">本次序列号 · {{ receipt.serialNumbers.length }} 个</h3>
    <ol class="max-h-64 space-y-2 overflow-y-auto" aria-label="本次全部序列号" tabindex="0">
      <li
        v-for="serial in receipt.serialNumbers"
        :key="serial"
        class="break-all rounded border border-border px-3 py-2 font-mono text-base"
      >
        {{ serial }}
      </li>
    </ol>
  </section>
</template>
