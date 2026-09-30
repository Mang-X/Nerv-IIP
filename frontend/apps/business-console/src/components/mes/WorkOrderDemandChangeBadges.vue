<script setup lang="ts">
defineProps<{
  hasChangedDemand?: boolean
  hasCancelledDemand?: boolean
}>()
</script>

<template>
  <span
    v-if="hasChangedDemand"
    class="rounded border border-warning/40 bg-warning/10 px-1.5 py-0.5 text-xs text-warning-foreground"
    >需求已变更</span
  >
  <span
    v-if="hasCancelledDemand"
    class="rounded border border-destructive/40 bg-destructive/10 px-1.5 py-0.5 text-xs text-destructive"
    >需求已取消</span
  >
</template>
