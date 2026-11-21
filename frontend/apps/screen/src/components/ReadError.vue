<script setup lang="ts">
import { accessError } from '@/data/real/readError'
import { formatScreenFreshness } from '@/data/freshness'
defineProps<{ error: unknown; loading?: boolean; lastUpdated?: number }>()
defineEmits<{ retry: [] }>()
</script>
<template>
  <span role="status">
    {{ accessError(error) || '读取失败' }}
    <small v-if="lastUpdated && !accessError(error)">{{
      formatScreenFreshness(true, lastUpdated).text
    }}</small>
    <button type="button" :disabled="loading" @click="$emit('retry')">重试</button>
  </span>
</template>
<style scoped>
button {
  margin-inline-start: 12px;
  color: var(--nv-scr-cyan);
  cursor: pointer;
}
</style>
