<script setup lang="ts">
import { NvMobileButton, NvScanBar } from '@nerv-iip/ui-mobile'

defineProps<{
  scanKeyword: string
  sourceTypeFilter: string | null
  /** 来源筛选 chips（type/label/count 已由容器按扫码筛选后的集合计数）。 */
  chips: Array<{ type: string; label: string; count: number }>
}>()
const emit = defineEmits<{
  scan: [value: string]
  clearScan: []
  pickSourceType: [type: string | null]
}>()
</script>

<template>
  <!-- 扫码输入 + 关键字筛选提示 + 来源类型 chips -->
  <NvScanBar placeholder="扫来源单据 / SKU 直达" @scan="(v) => emit('scan', v)" />

  <div
    v-if="scanKeyword"
    class="flex items-center justify-between rounded-lg border border-border bg-card px-4 py-2 text-sm"
  >
    <span class="truncate text-foreground">筛选：{{ scanKeyword }}</span>
    <NvMobileButton variant="outline" size="sm" data-testid="clear-scan" @click="emit('clearScan')">
      清除
    </NvMobileButton>
  </div>

  <div class="flex flex-wrap gap-2">
    <NvMobileButton
      :variant="sourceTypeFilter === null ? 'primary' : 'outline'"
      size="sm"
      data-testid="chip-all"
      @click="emit('pickSourceType', null)"
    >
      全部
    </NvMobileButton>
    <NvMobileButton
      v-for="chip in chips"
      :key="chip.type"
      :variant="sourceTypeFilter === chip.type ? 'primary' : 'outline'"
      size="sm"
      :data-testid="`chip-${chip.type}`"
      @click="emit('pickSourceType', chip.type)"
    >
      {{ chip.label }} {{ chip.count }}
    </NvMobileButton>
  </div>
</template>
