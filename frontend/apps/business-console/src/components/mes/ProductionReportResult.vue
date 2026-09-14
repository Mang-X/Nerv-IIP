<script setup lang="ts">
import type { BusinessConsoleRecordProductionReportResponse } from '@nerv-iip/api-client'
import { NvButton } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  result: BusinessConsoleRecordProductionReportResponse
  pending: boolean
}>()
defineEmits<{ retry: []; refresh: []; close: [] }>()
const printState = computed(() => {
  if (props.result.printingPreparationPending) return '打印准备待完成'
  if (props.result.printStatus === 'sent-to-printer') return '已发送至打印机'
  if (props.result.printStatus === 'failed') return '标签发送失败'
  return '等待标签发送'
})
</script>

<template>
  <section aria-label="报工凭据" class="grid gap-4">
    <dl class="grid gap-3 rounded-lg border bg-muted/40 p-4">
      <div>
        <dt class="text-xs text-muted-foreground">报工号</dt>
        <dd class="break-all font-medium">{{ result.reportNo }}</dd>
      </div>
      <div>
        <dt class="text-xs text-muted-foreground">打印批次</dt>
        <dd class="break-all font-medium">{{ result.printBatchId }}</dd>
      </div>
      <div>
        <dt class="text-xs text-muted-foreground">标签进度</dt>
        <dd class="font-medium">{{ printState }}</dd>
      </div>
    </dl>
    <p v-if="result.printStatus === 'sent-to-printer'" class="text-sm text-muted-foreground">
      发送成功不代表实际出纸，请在打印机处核对。
    </p>
    <div class="grid gap-2">
      <h3 class="text-base font-semibold">单件序列号（{{ result.serialNumbers?.length ?? 0 }}）</h3>
      <ul class="max-h-60 overflow-y-auto rounded-lg border divide-y">
        <li v-for="serial in result.serialNumbers" :key="serial" class="p-3">
          <a
            :href="`/mes/traceability?mode=batch&serialNo=${encodeURIComponent(serial)}`"
            class="break-all text-primary underline underline-offset-4"
            >{{ serial }}</a
          >
        </li>
      </ul>
    </div>
    <div class="flex justify-end gap-2">
      <NvButton
        v-if="result.printingPreparationPending"
        data-testid="retry-print-preparation"
        :disabled="pending"
        @click="$emit('retry')"
        >继续完成打印准备</NvButton
      >
      <NvButton
        v-else
        data-testid="refresh-print-status"
        :disabled="pending"
        @click="$emit('refresh')"
        >刷新打印进度</NvButton
      >
      <NvButton variant="outline" @click="$emit('close')">关闭</NvButton>
    </div>
  </section>
</template>
