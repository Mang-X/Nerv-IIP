<script setup lang="ts">
import { NvBadge } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  value?: string | null
}>()

const statusLabels: Record<string, string> = {
  active: '启用',
  approved: '已批准',
  available: '可用',
  blocked: '阻塞',
  cancelled: '已取消',
  closed: '已关闭',
  completed: '已完成',
  'conditional-release': '条件放行',
  conditionalrelease: '条件放行',
  created: '已创建',
  disabled: '停用',
  failed: '失败',
  held: '暂停',
  inprogress: '执行中',
  'in-progress': '执行中',
  issued: '已下发',
  manual: '手工处理',
  open: '待处理',
  passed: '通过',
  paused: '暂停',
  pending: '待处理',
  planned: '已计划',
  queued: '排队中',
  ready: '可开工',
  rejected: '已拒绝',
  released: '已下达',
  running: '执行中',
  scheduled: '已排程',
  started: '已开工',
  submitted: '已提交',
  unavailable: '不可用',
  warning: '预警',
}
const rawValue = computed(() => props.value?.trim() || '')
const label = computed(
  () => statusLabels[rawValue.value.toLowerCase()] ?? (rawValue.value || '未知'),
)
const variant = computed(() => {
  const value = rawValue.value.toLowerCase()
  if (
    ['ready', 'completed', 'closed', 'passed', 'available', 'active', 'approved'].includes(value)
  ) {
    return 'success'
  }
  if (
    [
      'running',
      'inprogress',
      'in-progress',
      'started',
      'manual',
      'released',
      'issued',
      'scheduled',
    ].includes(value)
  ) {
    return 'neutral'
  }
  if (['blocked', 'failed', 'rejected', 'unavailable', 'cancelled', 'disabled'].includes(value)) {
    return 'danger'
  }
  if (
    [
      'pending',
      'warning',
      'conditional-release',
      'conditionalrelease',
      'held',
      'paused',
      'open',
      'created',
      'planned',
      'queued',
      'submitted',
    ].includes(value)
  ) {
    return 'warning'
  }
  return 'neutral'
})
</script>

<template>
  <NvBadge class="max-w-32 truncate rounded-sm" :aria-label="`状态：${label}`" :variant="variant">
    {{ label }}
  </NvBadge>
</template>
