<script setup lang="ts">
import { NvMobileButton } from '@nerv-iip/ui-mobile'

const emit = defineEmits<{ return: [] }>()
</script>

<template>
  <div role="alert" class="space-y-2 rounded-lg border border-border bg-card p-3 text-sm">
    <p>上一笔报工尚未核验或标签准备尚未完成，请先处理原报工，再提交新产量。</p>
    <NvMobileButton data-testid="return-to-preparation" block @click="emit('return')"
      >返回原报工处理</NvMobileButton
    >
  </div>
</template>
