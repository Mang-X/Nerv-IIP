<script setup lang="ts">
interface Props {
  laborHours?: number | null
  machineHours?: number | null
}

defineProps<Props>()

const hoursFormatter = new Intl.NumberFormat('zh-CN', {
  maximumFractionDigits: 4,
})
const minimumDisplayedHours = 0.0001

function formatHours(value?: number | null) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '暂无实绩'
  if (value > 0 && value < minimumDisplayedHours) {
    return `小于 ${hoursFormatter.format(minimumDisplayedHours)} 小时`
  }
  return `${hoursFormatter.format(value)} 小时`
}
</script>

<template>
  <div data-testid="actual-hours" class="grid gap-0.5 text-xs">
    <span>
      <span class="text-muted-foreground">人工</span>
      <span class="ml-1 tabular-nums">{{ formatHours(laborHours) }}</span>
    </span>
    <span>
      <span class="text-muted-foreground">机器</span>
      <span class="ml-1 tabular-nums">{{ formatHours(machineHours) }}</span>
    </span>
  </div>
</template>
