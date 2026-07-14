<script setup lang="ts">
import type { BusinessConsoleInspectionRecordDetailResponse } from '@nerv-iip/api-client'
import { NvCell, NvCellGroup } from '@nerv-iip/ui-mobile'

defineProps<{
  record: BusinessConsoleInspectionRecordDetailResponse
}>()
const emit = defineEmits<{ openNcr: [ncrId: string] }>()
</script>

<template>
  <!-- 元数据字段组 + 记录 → NCR 互链 -->
  <NvCellGroup>
    <NvCell title="检验数量" :value="`${record.inspectedQuantity}${record.uomCode ?? ''}`" />
    <NvCell v-if="record.batchNo" title="批次" :value="record.batchNo" />
    <NvCell v-if="record.serialNo" title="序列号" :value="record.serialNo" />
    <NvCell v-if="record.dispositionReason" title="处置原因" :value="record.dispositionReason" />
    <!-- 记录 → NCR 互链：不合格自动开出的 NCR，点按打开详情。 -->
    <NvCell
      v-if="record.nonconformanceReportId"
      data-testid="record-ncr-link"
      title="不合格报告"
      value="查看"
      arrow
      @click="emit('openNcr', record.nonconformanceReportId)"
    />
  </NvCellGroup>
</template>
