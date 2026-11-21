<script setup lang="ts">
import { scopeKeyOf } from '@/composables/useScopedReading'
import type { ScopeOption } from '@/data/real/workScope'
defineProps<{ label: string; options: ScopeOption[]; selected: string }>()
defineEmits<{ select: [key: string] }>()
</script>
<template>
  <label
    >{{ label }}
    <select :value="selected" @change="$emit('select', ($event.target as HTMLSelectElement).value)">
      <option v-if="!options.some((s) => scopeKeyOf(s) === selected)" disabled :value="selected">
        请选择授权范围
      </option>
      <option v-for="s in options" :key="scopeKeyOf(s)" :value="scopeKeyOf(s)">
        {{ s.label }}{{ s.label === s.id ? '' : ' · ' + s.id }}
      </option>
    </select>
  </label>
</template>
<style scoped>
label {
  color: var(--nv-scr-muted);
}
select {
  color: var(--nv-scr-text);
  background: var(--nv-scr-bg);
  margin-inline: 8px;
}
</style>
