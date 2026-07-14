<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import type { BusinessConsoleQualityNcrDetailResponse } from '@nerv-iip/api-client'
import { NvCell, NvCellGroup, NvMobileButton, NvMobileTag } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

const props = defineProps<{
  ncr: BusinessConsoleQualityNcrDetailResponse | null
  pending: boolean
  error: unknown
}>()
const emit = defineEmits<{ retry: []; back: []; openRecord: [recordId: string] }>()

// 来源检验记录取服务端权威业务关系（NCR 聚合的 SourceInspectionRecordId），
// 而非客户端 query 参数——直接打开 NCR URL 也有回链，且不可被 query 篡改指向无关记录。
const sourceRecordId = computed(() => props.ncr?.sourceInspectionRecordId ?? null)

const statusLabel = computed(() => {
  switch (props.ncr?.status) {
    case 'open':
      return '待处置'
    case 'disposition-submitted':
      return '处置待审'
    case 'closed':
      return '已关闭'
    default:
      return props.ncr?.status ?? ''
  }
})
</script>

<template>
  <div class="space-y-4 p-4">
    <RetryableListError
      v-if="error"
      :error="error"
      :pending="pending"
      fallback="不合格报告加载失败，请稍后重试。"
      test-id="ncr-error"
      @retry="() => emit('retry')"
    />

    <div v-else-if="pending" class="px-4 py-8 text-center text-sm text-muted-foreground">
      加载中…
    </div>

    <template v-else-if="ncr">
      <section class="space-y-2 rounded-lg border border-border bg-card p-4" data-testid="ncr-detail">
        <div class="flex items-center justify-between gap-2">
          <p class="text-base font-semibold text-foreground">{{ ncr.code }}</p>
          <NvMobileTag :variant="ncr.status === 'closed' ? 'default' : 'warning'">
            {{ statusLabel }}
          </NvMobileTag>
        </div>
        <p class="text-sm text-muted-foreground">检验不合格已自动发起不合格处置，请在处置流程中跟进。</p>
      </section>

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

      <NvMobileButton variant="outline" size="lg" block data-testid="ncr-back" @click="emit('back')">
        返回检验流程
      </NvMobileButton>
    </template>
  </div>
</template>
