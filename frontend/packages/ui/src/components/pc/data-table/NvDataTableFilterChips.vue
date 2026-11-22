<script setup lang="ts">
import { XIcon } from '@lucide/vue'

defineProps<{ filterChips: { key: string; header: string; text: string }[] }>()
const emit = defineEmits<{ clear: [key: string]; clearAll: [] }>()
</script>

<template>
  <div class="flex flex-wrap items-center gap-1.5">
    <span class="text-xs text-muted-foreground">筛选：</span>
    <button
      v-for="chip in filterChips"
      :key="chip.key"
      type="button"
      class="nv-dt-chip"
      @click="emit('clear', chip.key)"
    >
      <span class="text-muted-foreground">{{ chip.header }}</span>
      <span class="font-medium">{{ chip.text }}</span>
      <XIcon class="size-3 opacity-60" aria-hidden="true" />
    </button>
    <button
      type="button"
      class="text-xs text-muted-foreground underline-offset-2 transition-colors hover:text-foreground hover:underline"
      @click="emit('clearAll')"
    >
      清除全部
    </button>
  </div>
</template>

<style scoped>
@layer nv-components {
  /* Active-filter chips. */
  .nv-dt-chip {
    display: inline-flex;
    align-items: center;
    gap: 0.375rem;
    height: 1.625rem;
    padding-inline: 0.5rem;
    border-radius: 7px;
    border: 1px solid var(--border);
    background-color: color-mix(in oklch, var(--muted) 50%, var(--card));
    font-size: 0.75rem;
    color: var(--foreground);
    transition:
      border-color 0.15s ease,
      background-color 0.15s ease;
  }
  .nv-dt-chip:hover {
    border-color: color-mix(in oklch, var(--foreground) 20%, transparent);
    background-color: var(--muted);
  }
}
</style>
