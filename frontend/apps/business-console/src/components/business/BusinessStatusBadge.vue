<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  value?: string | null
}>()

const statusLabels: Record<string, string> = {
  active: '启用',
  available: '可用',
  blocked: '阻塞',
  closed: '已关闭',
  completed: '已完成',
  'conditional-release': '条件放行',
  failed: '失败',
  held: '暂停',
  inprogress: '执行中',
  'in-progress': '执行中',
  manual: '手工处理',
  passed: '通过',
  paused: '暂停',
  pending: '待处理',
  queued: '排队中',
  ready: '可开工',
  rejected: '已拒绝',
  released: '已下达',
  running: '执行中',
  started: '已开工',
  unavailable: '不可用',
  warning: '预警',
}
const rawValue = computed(() => props.value?.trim() || '')
const label = computed(() => statusLabels[rawValue.value.toLowerCase()] ?? (rawValue.value || '未知'))
const variant = computed(() => {
  const value = rawValue.value.toLowerCase()
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
