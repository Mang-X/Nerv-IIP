<script setup lang="ts">
import type { InventoryExpirySummary } from '@/utils/inventoryExpiryPresentation'
import type { NvMetricSegment } from '@nerv-iip/ui'
import { NvMetricRing, NvMetricStrip } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{ summary: InventoryExpirySummary }>()

/** 范围不足时汇总回的是「—」占位；环形与色调只认数字，占位一律按 0 处理（不画弧、不染色）。 */
function toCount(value: number | string) {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0
}
const expiredCount = computed(() => toCount(props.summary.expiredCount))
const nearCount = computed(() => toCount(props.summary.nearCount))

// 已过期 + 30 天内到期 = 全部预警明细，是真正的构成关系，所以用环形卡。
const alertSegments = computed<NvMetricSegment[]>(() => [
  { key: 'expired', label: '已过期', value: expiredCount.value, tone: 'danger' },
  { key: 'near', label: '30 天内到期', value: nearCount.value, tone: 'warning' },
])
</script>

<template>
  <div class="grid gap-4 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
    <NvMetricRing
      label="效期预警构成"
      :value="summary.alertCount"
      center-caption="条 · 预警明细"
      :segments="alertSegments"
    />
    <NvMetricStrip
      :cells="[
        {
          key: 'expired',
          label: '已过期',
          value: summary.expiredCount,
          unit: '条',
          valueTone: expiredCount > 0 ? 'danger' : undefined,
          meta: '需要先隔离再决定让步或报废',
        },
        {
          key: 'near',
          label: '30 天内到期',
          value: summary.nearCount,
          unit: '条',
          valueTone: nearCount > 0 ? 'warning' : undefined,
          meta: '优先安排出库或转用',
        },
        { key: 'sku', label: '涉及物料', value: summary.skuCount, unit: '种' },
      ]"
    />
  </div>
</template>
