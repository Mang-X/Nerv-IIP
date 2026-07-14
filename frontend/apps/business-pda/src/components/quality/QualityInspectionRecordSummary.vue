<script setup lang="ts">
import type { BusinessConsoleInspectionRecordDetailResponse } from '@nerv-iip/api-client'
import { inspectionRecordResultLabel, inspectionTaskSourceTypeLabel } from '@nerv-iip/business-core'
import { NvMobileTag } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

const props = defineProps<{
  record: BusinessConsoleInspectionRecordDetailResponse
}>()

const rejected = computed(() => props.record.result !== 'passed')
</script>

<template>
  <!-- 摘要卡：物料 + 权威结论 + 来源上下文 -->
  <section class="space-y-2 rounded-lg border border-border bg-card p-4" data-testid="record-detail">
    <div class="flex items-center justify-between gap-2">
      <p class="text-base font-semibold text-foreground">{{ record.skuCode }}</p>
      <NvMobileTag :variant="rejected ? 'danger' : 'success'">
        {{ inspectionRecordResultLabel(record.result) }}
      </NvMobileTag>
    </div>
    <p class="text-sm text-muted-foreground">
      {{ inspectionTaskSourceTypeLabel(record.sourceType) }} · 来源单 {{ record.sourceDocumentId }}
    </p>
  </section>
</template>
