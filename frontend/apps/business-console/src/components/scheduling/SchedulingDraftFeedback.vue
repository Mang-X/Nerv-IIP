<script setup lang="ts">
import type { DraftTaskFeedback } from '@nerv-iip/scheduling'
import { formatDateTime } from '@/utils/format'

defineProps<{ feedback?: DraftTaskFeedback }>()
</script>

<template>
  <div v-if="feedback" class="grid gap-1 text-xs" data-testid="draft-operation-feedback">
    <p class="font-medium">草案即时反馈</p>
    <p v-if="!feedback.issues.length" class="text-muted-foreground">日历、占用及前序未发现问题</p>
    <ul v-else class="grid gap-1">
      <li
        v-for="(issue, index) in feedback.issues"
        :key="index"
        :class="issue.kind === 'unknown' ? 'text-muted-foreground' : 'text-warning'"
      >
        {{ issue.message }}
        <span v-if="issue.startUtc && issue.endUtc" class="block tabular-nums">
          {{ formatDateTime(issue.startUtc) }} 至 {{ formatDateTime(issue.endUtc) }}
        </span>
      </li>
    </ul>
    <p
      v-if="feedback.due"
      :class="feedback.due.status === 'late' ? 'text-warning' : 'text-muted-foreground'"
    >
      交期差 ·
      {{
        feedback.due.status === 'onTime'
          ? '按期'
          : `${feedback.due.status === 'early' ? '提前' : '延期'} ${Math.round(Math.abs(feedback.due.deltaMinutes))} 分钟`
      }}
      <span class="block">方案交期 · {{ formatDateTime(feedback.due.dueUtc) }}</span>
    </p>
    <p v-else class="text-muted-foreground">未记录可核对的工序交期</p>
  </div>
</template>
