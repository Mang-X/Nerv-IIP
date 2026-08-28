<script setup lang="ts">
import type { BusinessConsoleBarcodeResolveCandidate } from '@nerv-iip/api-client'
import { NvMobileButton, NvScanBar } from '@nerv-iip/ui-mobile'
import { computed, onBeforeUnmount, watch } from 'vue'

import {
  useMesScanPrevalidation,
  type MesScanAccepted,
} from '@/composables/mes/useMesScanPrevalidation'

const props = withDefaults(
  defineProps<{
    organizationId: string
    environmentId: string
    workOrderId?: string
    operationTaskId?: string
    active?: boolean
    placeholder?: string
  }>(),
  {
    workOrderId: '',
    operationTaskId: '',
    active: true,
    placeholder: '扫描工单 / 工序 / 物料 / 设备 / 工牌',
  },
)

const emit = defineEmits<{
  accepted: [value: MesScanAccepted]
  pendingChange: [pending: boolean]
  statusChange: [status: ReturnType<typeof useMesScanPrevalidation>['status']['value']]
}>()

const scanner = useMesScanPrevalidation({
  organizationId: () => props.organizationId,
  environmentId: () => props.environmentId,
  context: () => ({
    workOrderId: props.workOrderId,
    operationTaskId: props.operationTaskId,
  }),
})

watch(scanner.pending, (pending) => emit('pendingChange', pending), { flush: 'sync' })
watch(scanner.status, (status) => emit('statusChange', status), { flush: 'sync' })
onBeforeUnmount(scanner.reset)

const alert = computed(() =>
  ['unknown', 'forbidden', 'rejected', 'error'].includes(scanner.status.value),
)

const candidateLabels: Record<string, string> = {
  'mes-work-order': '生产工单',
  'mes-operation': '工序任务',
  'mes-material-issue-request': '物料批次',
  'equipment-device': '设备',
  personnel: '工牌',
}

function candidateLabel(candidate: BusinessConsoleBarcodeResolveCandidate, index: number) {
  return `${candidateLabels[candidate.objectType ?? ''] ?? '业务对象'}候选 ${index + 1}`
}

async function onScan(value: string) {
  const accepted = await scanner.scan(value)
  if (accepted) emit('accepted', accepted)
}

async function onCandidate(candidate: BusinessConsoleBarcodeResolveCandidate) {
  const accepted = await scanner.selectCandidate(candidate)
  if (accepted) emit('accepted', accepted)
}
</script>

<template>
  <div class="space-y-3" data-testid="mes-scan-prevalidation">
    <fieldset class="m-0 min-w-0 border-0 p-0">
      <NvScanBar :placeholder="props.placeholder" :active="props.active" @scan="onScan" />
    </fieldset>

    <section
      v-if="scanner.message.value"
      data-testid="mes-scan-status"
      :role="alert ? 'alert' : 'status'"
      aria-live="polite"
      class="rounded-xl border border-border bg-card p-3"
    >
      <p class="text-sm font-medium text-foreground">{{ scanner.message.value }}</p>
      <p v-if="scanner.scannedValue.value" class="mt-1 break-all text-xs text-muted-foreground">
        已读取本次扫码内容
      </p>
    </section>

    <div v-if="scanner.status.value === 'ambiguous'" class="space-y-2">
      <NvMobileButton
        v-for="(candidate, index) in scanner.candidates.value"
        :key="`${candidate.objectType}-${index}`"
        :data-testid="`mes-scan-candidate-${index}`"
        variant="outline"
        size="lg"
        block
        :disabled="scanner.pending.value"
        @click="onCandidate(candidate)"
      >
        {{ candidateLabel(candidate, index) }}
      </NvMobileButton>
    </div>
  </div>
</template>
