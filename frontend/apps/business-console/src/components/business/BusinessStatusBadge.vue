<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  value?: string | null
}>()

const label = computed(() => props.value?.trim() || '未知')
const variant = computed(() => {
  const value = label.value.toLowerCase()
  if (['ready', 'completed', 'closed', 'passed', 'available', 'active'].includes(value)) {
    return 'success'
  }
  if (['running', 'inprogress', 'in-progress', 'started', 'manual'].includes(value)) {
    return 'default'
  }
  if (['blocked', 'failed', 'rejected', 'unavailable'].includes(value)) {
    return 'destructive'
  }
  if (['pending', 'warning', 'conditional-release'].includes(value)) {
    return 'warning'
  }
  return 'secondary'
})
</script>

<template>
  <Badge :variant="variant">{{ label }}</Badge>
</template>
