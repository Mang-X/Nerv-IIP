<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import QualityNcrFields from '@/components/quality/QualityNcrFields.vue'
import QualityNcrSummary from '@/components/quality/QualityNcrSummary.vue'
import type { BusinessConsoleQualityNcrDetailResponse } from '@nerv-iip/api-client'
import { NvMobileButton } from '@nerv-iip/ui-mobile'

defineProps<{
  ncr: BusinessConsoleQualityNcrDetailResponse | null
  pending: boolean
  error: unknown
}>()
const emit = defineEmits<{ retry: []; back: []; openRecord: [recordId: string] }>()
</script>

<template>
  <!-- 状态分派 + 区段组合：摘要 / 字段与互链均为聚焦子组件。 -->
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
      <QualityNcrSummary :ncr="ncr" />
      <QualityNcrFields :ncr="ncr" @open-record="(id) => emit('openRecord', id)" />
      <NvMobileButton variant="outline" size="lg" block data-testid="ncr-back" @click="emit('back')">
        返回检验流程
      </NvMobileButton>
    </template>
  </div>
</template>
