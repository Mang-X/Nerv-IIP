<script setup lang="ts">
import { isForbiddenRequestError } from '@nerv-iip/api-client'
defineProps<{ error: unknown; loading?: boolean }>()
defineEmits<{ retry: [] }>()
</script>
<template>
  <span role="status">
    {{ isForbiddenRequestError(error) ? '无权查看当前范围' : '读取失败' }}
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
