<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import type { BusinessConsoleInspectionRecordDetailResponse } from '@nerv-iip/api-client'
import { inspectionRecordResultLabel, inspectionTaskSourceTypeLabel } from '@nerv-iip/business-core'
import { NvCell, NvCellGroup, NvMobileButton, NvMobileTag } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

const props = defineProps<{
  record: BusinessConsoleInspectionRecordDetailResponse | null
  pending: boolean
  error: unknown
}>()
const emit = defineEmits<{ retry: []; back: []; openNcr: [ncrId: string] }>()

const rejected = computed(() => props.record != null && props.record.result !== 'passed')
const resultLines = computed(() => props.record?.resultLines ?? [])
</script>

<template>
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
      <section
        class="space-y-2 rounded-lg border border-border bg-card p-4"
        data-testid="record-detail"
      >
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

      <section v-if="resultLines.length > 0" class="space-y-2">
        <h2 class="text-sm font-medium text-muted-foreground">特性结果</h2>
        <div class="overflow-hidden rounded-lg border border-border">
          <NvCell
            v-for="line in resultLines"
            :key="line.characteristicCode ?? ''"
            data-testid="record-line"
            :title="line.characteristicCode ?? ''"
            :note="line.defectReason ? `原因码 ${line.defectReason}` : undefined"
          >
            <template #value>
              <span :class="line.result === 'passed' ? 'text-foreground' : 'text-destructive'">
                {{ line.measuredValue ?? line.observedValue
                }}{{ line.unitCode ? ` ${line.unitCode}` : '' }} ·
                {{ line.result === 'passed' ? '合格' : '不合格' }}
              </span>
            </template>
          </NvCell>
        </div>
      </section>

      <NvMobileButton variant="outline" size="lg" block data-testid="record-back" @click="emit('back')">
        返回
      </NvMobileButton>
    </template>
  </div>
</template>
