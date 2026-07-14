<script setup lang="ts">
import type { BusinessConsoleQualityNcrDetailResponse } from '@nerv-iip/api-client'
import { NvMobileTag } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

const props = defineProps<{
  ncr: BusinessConsoleQualityNcrDetailResponse
}>()

const statusLabel = computed(() => {
  switch (props.ncr.status) {
    case 'open':
      return '待处置'
    case 'disposition-submitted':
      return '处置待审'
    case 'closed':
      return '已关闭'
    default:
      return props.ncr.status ?? ''
  }
})
</script>

<template>
  <!-- 摘要卡：NCR 单号 + 处置状态 + 说明 -->
  <section class="space-y-2 rounded-lg border border-border bg-card p-4" data-testid="ncr-detail">
    <div class="flex items-center justify-between gap-2">
      <p class="text-base font-semibold text-foreground">{{ ncr.code }}</p>
      <NvMobileTag :variant="ncr.status === 'closed' ? 'default' : 'warning'">
        {{ statusLabel }}
      </NvMobileTag>
    </div>
    <p class="text-sm text-muted-foreground">检验不合格已自动发起不合格处置，请在处置流程中跟进。</p>
  </section>
</template>
