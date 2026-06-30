<script setup lang="ts">
defineProps<{ items?: Array<{ key: string, label?: string, color?: string }> }>()
</script>
<template>
  <div class="flex items-center justify-center gap-4 pt-3">
    <div v-for="item in items ?? []" :key="item.key" class="flex items-center gap-1.5 text-xs">
      <span class="size-2 rounded-[2px]" :style="{ backgroundColor: item.color }" />
      {{ item.label ?? item.key }}
    </div>
  </div>
</template>
