<script setup lang="ts">
import { computed } from 'vue'
import { conflictReasonLabel } from '../../model/labels'
import type { ScheduleTask } from '../../model/types'

// 选中工序/工单的完整详情(取代弹出抽屉,常驻右侧栏顶部)。
const props = defineProps<{ task?: ScheduleTask }>()

const isOrder = computed(() => props.task?.type === 'order')
const PRIO = { high: ['高', 'danger'], medium: ['中', 'warning'], low: ['低', 'muted'] } as const

function fmt(iso?: string) {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString('zh-CN', { hour12: false, month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
}
function fmtDate(iso?: string) {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' })
}
const durationH = computed(() => {
  const t = props.task
  if (!t?.startUtc || !t?.endUtc) return '—'
  const h = (Date.parse(t.endUtc) - Date.parse(t.startUtc)) / 3_600_000
  return h >= 1 ? `${Math.round(h)} 小时` : '<1 小时'
})
const pct = (v?: number) => (v == null ? '—' : `${Math.round(v * 100)}%`)
</script>

<template>
  <div class="border-b border-border/60">
    <div v-if="!task" class="flex flex-col items-center gap-1.5 px-4 py-8 text-center">
      <span class="text-sm font-medium text-muted-foreground">未选择</span>
      <span class="text-xs text-muted-foreground/70">点击甘特条 / 排产卡查看完整详情</span>
    </div>

    <div v-else class="px-4 py-3.5">
      <!-- 标题行:WO + 优先级 + 插单 + 锁 -->
      <div class="flex flex-wrap items-center gap-1.5">
        <span class="font-mono text-sm font-semibold tracking-tight text-foreground">{{ task.orderId || '—' }}</span>
        <span
          v-if="task.priority"
          class="rounded px-1.5 py-px text-[0.65rem] font-bold"
          :class="{
            'bg-destructive/15 text-destructive': task.priority === 'high',
            'bg-warning/15 text-warning': task.priority === 'medium',
            'bg-muted text-muted-foreground': task.priority === 'low',
          }"
        >{{ PRIO[task.priority][0] }}优先</span>
        <span v-if="task.isRush" class="inline-flex items-center gap-0.5 rounded bg-[oklch(0.7_0.17_60/0.15)] px-1.5 py-px text-[0.65rem] font-semibold" style="color: oklch(0.62 0.17 60)">⚡ 插单</span>
        <span v-if="task.locked" class="inline-flex items-center gap-0.5 rounded bg-brand/12 px-1.5 py-px text-[0.65rem] font-semibold text-brand">已锁定</span>
      </div>
      <p class="mt-1 text-[0.82rem] text-foreground">
        {{ task.product || (isOrder ? '工单' : '工序') }}
        <span v-if="!isOrder" class="text-muted-foreground"> · {{ task.text }}</span>
      </p>

      <!-- 冲突横幅 -->
      <div
        v-if="task.hasConflict && task.conflictReason"
        class="mt-2.5 rounded-md border border-destructive/40 bg-destructive/10 px-2.5 py-1.5 text-xs font-medium text-destructive"
      >
        冲突 · {{ conflictReasonLabel[task.conflictReason] }}
      </div>

      <!-- 明细网格 -->
      <dl class="mt-3 grid grid-cols-2 gap-x-4 gap-y-2.5 text-xs">
        <div v-if="!isOrder" class="col-span-2 flex justify-between">
          <dt class="text-muted-foreground">资源</dt>
          <dd class="font-medium text-foreground">{{ task.resourceId || '—' }}</dd>
        </div>
        <div class="flex justify-between"><dt class="text-muted-foreground">开始</dt><dd class="font-medium text-foreground">{{ fmt(task.startUtc) }}</dd></div>
        <div class="flex justify-between"><dt class="text-muted-foreground">结束</dt><dd class="font-medium text-foreground">{{ fmt(task.endUtc) }}</dd></div>
        <div class="flex justify-between"><dt class="text-muted-foreground">工时</dt><dd class="font-medium text-foreground">{{ durationH }}</dd></div>
        <div v-if="task.quantity != null" class="flex justify-between"><dt class="text-muted-foreground">数量</dt><dd class="font-medium tabular-nums text-foreground">{{ task.quantity }}</dd></div>
        <div v-if="task.dueUtc" class="flex justify-between"><dt class="text-muted-foreground">交期</dt><dd class="font-medium text-foreground">{{ fmtDate(task.dueUtc) }}</dd></div>
        <div v-if="task.owner" class="flex justify-between"><dt class="text-muted-foreground">负责人</dt><dd class="font-medium text-foreground">{{ task.owner }}</dd></div>
        <div v-if="task.kitting != null" class="flex justify-between">
          <dt class="text-muted-foreground">齐套</dt>
          <dd class="font-medium tabular-nums" :class="task.kitting >= 1 ? 'text-success' : task.kitting >= 0.8 ? 'text-warning' : 'text-destructive'">{{ pct(task.kitting) }}</dd>
        </div>
        <div v-if="task.changeoverMin" class="flex justify-between"><dt class="text-muted-foreground">换型</dt><dd class="font-medium text-foreground">{{ task.changeoverMin }} 分钟</dd></div>
        <div v-if="task.load != null" class="flex justify-between">
          <dt class="text-muted-foreground">占用</dt>
          <dd class="font-medium tabular-nums" :class="task.load > 1 ? 'text-destructive' : 'text-foreground'">{{ pct(task.load) }}</dd>
        </div>
        <div v-if="task.progress != null" class="flex justify-between"><dt class="text-muted-foreground">进度</dt><dd class="font-medium tabular-nums text-foreground">{{ pct(task.progress) }}</dd></div>
        <div v-if="task.status" class="flex justify-between"><dt class="text-muted-foreground">状态</dt><dd class="font-medium text-foreground">{{ task.status.label }}</dd></div>
      </dl>
    </div>
  </div>
</template>
