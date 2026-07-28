<script setup lang="ts">
/**
 * 「名称在上、编码在下」的通用单元格。
 * 解析不出名称时只显编码——不编造名字，也不留空。
 */
const props = defineProps<{ code?: string | null; name?: string | null; fallback?: string }>()
</script>

<template>
  <span v-if="!props.code && !props.name" class="text-muted-foreground">
    {{ props.fallback ?? '未指定' }}
  </span>
  <span v-else-if="!props.name">{{ props.code }}</span>
  <span v-else class="grid leading-tight">
    <span>{{ props.name }}</span>
    <span v-if="props.code" class="text-xs text-muted-foreground">{{ props.code }}</span>
  </span>
</template>
