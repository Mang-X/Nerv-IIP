<script setup lang="ts">
import type { BusinessConsoleQualityNcrDetailResponse } from '@nerv-iip/api-client'
import { NvCell, NvCellGroup } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

const props = defineProps<{
  ncr: BusinessConsoleQualityNcrDetailResponse
}>()
const emit = defineEmits<{ openRecord: [recordId: string] }>()

// 来源检验记录取服务端权威业务关系（NCR 聚合的 SourceInspectionRecordId），
// 而非客户端 query 参数——直接打开 NCR URL 也有回链，且不可被 query 篡改指向无关记录。
const sourceRecordId = computed(() => props.ncr.sourceInspectionRecordId ?? null)
</script>

<template>
  <!-- 元数据字段组 + NCR → 检验记录互链 -->
  <NvCellGroup>
    <NvCell v-if="ncr.skuCode" title="物料" :value="ncr.skuCode" />
    <NvCell v-if="ncr.sourceDocumentId" title="来源单据" :value="ncr.sourceDocumentId" />
    <NvCell v-if="ncr.defectReason" title="不良原因" :value="ncr.defectReason" />
    <NvCell v-if="ncr.defectQuantity != null" title="不良数" :value="ncr.defectQuantity" />
    <NvCell v-if="ncr.batchNo" title="批次" :value="ncr.batchNo" />
    <NvCell v-if="ncr.serialNo" title="序列号" :value="ncr.serialNo" />
    <!-- NCR → 检验记录互链：目标来自服务端权威回链，点按打开真实路由 /quality/record/{id}。 -->
    <NvCell
      v-if="sourceRecordId"
      data-testid="source-record"
      title="来源检验记录"
      value="查看"
      arrow
      @click="emit('openRecord', sourceRecordId)"
    />
  </NvCellGroup>
</template>
