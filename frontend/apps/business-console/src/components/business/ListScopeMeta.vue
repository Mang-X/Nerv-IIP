<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    scope: string
    source: string
    loaded: number
    total: number
    updatedAt?: string | null
    empty?: boolean
    emptyExplanation?: string
    failed?: boolean
    failureExplanation?: string
  }>(),
  {
    updatedAt: null,
    empty: false,
    emptyExplanation: '',
    failed: false,
    failureExplanation: '',
  },
)

const updatedText = computed(() => {
  if (!props.updatedAt) return '尚未读取'
  const date = new Date(props.updatedAt)
  return Number.isNaN(date.getTime())
    ? '尚未读取'
    : new Intl.DateTimeFormat('zh-CN', {
        dateStyle: 'short',
        timeStyle: 'short',
        timeZone: 'Asia/Shanghai',
      }).format(date)
})
</script>

<template>
  <div
    data-testid="list-scope-meta"
    class="space-y-1 text-xs leading-5 text-muted-foreground"
    aria-live="polite"
  >
    <div class="flex flex-wrap gap-x-3 gap-y-1">
      <span>范围：{{ scope }}</span>
      <span>来源：{{ source }}</span>
      <span>已加载 {{ loaded }} / 共 {{ total }}</span>
      <span>更新时间（最近成功响应）：{{ updatedText }}</span>
    </div>
    <p v-if="failed && failureExplanation" data-testid="list-failure-explanation">
      查询失败：{{ failureExplanation }}
    </p>
    <p v-else-if="empty && emptyExplanation" data-testid="list-empty-explanation">
      空态说明：{{ emptyExplanation }}
    </p>
  </div>
</template>
