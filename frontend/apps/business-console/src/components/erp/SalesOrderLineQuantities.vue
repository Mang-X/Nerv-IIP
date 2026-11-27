<script setup lang="ts">
import type { BusinessConsoleErpSalesOrderItem } from '@nerv-iip/api-client'

defineProps<{ lines?: BusinessConsoleErpSalesOrderItem['lines'] }>()

function quantityWithUnit(quantity?: number, unit?: string) {
  if (quantity == null) return '—'
  return unit ? `${quantity} ${unit}` : String(quantity)
}
</script>

<template>
  <ul v-if="lines?.length" class="space-y-2 text-sm">
    <li v-for="(line, index) in lines" :key="line.lineNo ?? index">
      <div class="break-words font-medium">
        {{ [line.lineNo, line.skuCode].filter(Boolean).join(' · ') || '—' }}
      </div>
      <div class="flex flex-wrap gap-x-3 tabular-nums">
        <span>已分配 {{ quantityWithUnit(line.allocatedQuantity, line.uomCode) }}</span>
        <span>实际已发运 {{ quantityWithUnit(line.shippedQuantity, line.uomCode) }}</span>
      </div>
    </li>
  </ul>
  <span v-else class="text-muted-foreground">—</span>
</template>
