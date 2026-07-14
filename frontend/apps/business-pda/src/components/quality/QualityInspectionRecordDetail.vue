<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import QualityInspectionRecordFields from '@/components/quality/QualityInspectionRecordFields.vue'
import QualityInspectionRecordLines from '@/components/quality/QualityInspectionRecordLines.vue'
import QualityInspectionRecordSummary from '@/components/quality/QualityInspectionRecordSummary.vue'
import type { BusinessConsoleInspectionRecordDetailResponse } from '@nerv-iip/api-client'
import { NvMobileButton } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

const props = defineProps<{
  record: BusinessConsoleInspectionRecordDetailResponse | null
  pending: boolean
  error: unknown
}>()
const emit = defineEmits<{ retry: []; back: []; openNcr: [ncrId: string] }>()

const resultLines = computed(() => props.record?.resultLines ?? [])
</script>

<template>
  <!-- 状态分派 + 区段组合：摘要 / 字段与互链 / 特性结果均为聚焦子组件。 -->
  <div class="space-y-4 p-4">
    <RetryableListError
      v-if="error"
      :error="error"
      :pending="pending"
      fallback="检验记录加载失败，请稍后重试。"
      test-id="record-error"
      @retry="() => emit('retry')"
    />

    <div v-else-if="pending" class="px-4 py-8 text-center text-sm text-muted-foreground">
      加载中…
    </div>

    <template v-else-if="record">
      <QualityInspectionRecordSummary :record="record" />
      <QualityInspectionRecordFields :record="record" @open-ncr="(id) => emit('openNcr', id)" />
      <QualityInspectionRecordLines :lines="resultLines" />
      <NvMobileButton variant="outline" size="lg" block data-testid="record-back" @click="emit('back')">
        返回
      </NvMobileButton>
    </template>
  </div>
</template>
