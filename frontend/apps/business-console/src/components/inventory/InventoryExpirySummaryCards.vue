<script setup lang="ts">
import type { InventoryExpirySummary } from '@/utils/inventoryExpiryPresentation'
import { NvSectionCard, NvSectionCards } from '@nerv-iip/ui'

defineProps<{ summary: InventoryExpirySummary }>()
</script>

<template>
  <NvSectionCards :columns="4">
    <NvSectionCard
      description="效期预警明细"
      :value="summary.alertCount"
      hint="已过期及未来30天内到期"
    />
    <NvSectionCard description="已过期明细" :value="summary.expiredCount" hint="按库存台账行" />
    <NvSectionCard description="30天内明细" :value="summary.nearCount" hint="不含已过期明细" />
    <NvSectionCard description="涉及 SKU" :value="summary.skuCount" hint="当前筛选范围" />
  </NvSectionCards>
</template>
