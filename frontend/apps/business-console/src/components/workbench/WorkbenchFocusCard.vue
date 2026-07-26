<script setup lang="ts">
import type { Component } from 'vue'
import { computed } from 'vue'
import { ArrowRightIcon } from '@lucide/vue'
import { NvCard, cn } from '@nerv-iip/ui'
import { RouterLink } from 'vue-router'

/**
 * 工作台「行动卡」——待办 / 消息 / 设备预警共用的一张卡。
 *
 * 结构固定为四段：图标 + 主张（这张卡要你做什么）→ 数量读数 → 最近 3 条真实条目
 * → 去处理的出口。列表只作为「值不值得点进去」的证据，不做成迷你表格；条目主行
 * 一律用 facade 返回的人读编码（PO-… / WO-… / 设备号），副行才是状态与时间。
 *
 * 空态不是"没有数据"，而是"这一路已经清空 + 仍然给出出口"，所以空态同样保留 CTA。
 */
export interface WorkbenchFocusItem {
  key: string
  /** 主行：人读编码或事件主体。 */
  primary: string
  /** 副行：状态 · 时间 · 来源。 */
  secondary: string
}

export type WorkbenchFocusTone = 'brand' | 'warning' | 'danger' | 'success' | 'neutral'

const props = withDefaults(
  defineProps<{
    title: string
    /** 主张：这张卡要求使用者做什么，一句话，不写用途说明。 */
    description: string
    icon: Component
    tone?: WorkbenchFocusTone
    count: number
    unit?: string
    /** 右上角补充读数，例如「紧急 2」。 */
    badge?: string
    items?: WorkbenchFocusItem[]
    emptyTitle: string
    emptyHint: string
    /** 出口路由；省略时不渲染 CTA（该域当前没有落地页）。 */
    to?: string
    actionLabel?: string
    pending?: boolean
  }>(),
  { tone: 'brand', items: () => [], pending: false },
)

/** tone → 图标底色（与 NvStatusBadge / NvMetricCard 的色阶保持同一套语义）。 */
const toneTint: Record<WorkbenchFocusTone, string> = {
  brand: 'bg-brand/10 text-brand-strong',
  success: 'bg-success/10 text-success-strong',
  warning: 'bg-warning/15 text-warning-strong',
  danger: 'bg-destructive/10 text-destructive-strong',
  neutral: 'bg-muted text-muted-foreground',
}

const toneText: Record<WorkbenchFocusTone, string> = {
  brand: 'text-brand-strong',
  success: 'text-success-strong',
  warning: 'text-warning-strong',
  danger: 'text-destructive-strong',
  neutral: 'text-muted-foreground',
}

const visibleItems = computed(() => props.items.slice(0, 3))
const showEmptyState = computed(() => !props.pending && visibleItems.value.length === 0)
</script>

<template>
  <NvCard
    class="flex flex-col overflow-hidden bg-gradient-to-t from-primary/5 to-card p-0"
    :data-focus="title"
  >
    <div class="flex items-start gap-3 px-5 pt-5">
      <span :class="cn('grid size-10 flex-none place-items-center rounded-[10px]', toneTint[tone])">
        <component :is="icon" class="size-5" aria-hidden="true" />
      </span>
      <div class="min-w-0 flex-1">
        <div class="flex items-baseline gap-2">
          <h2 class="truncate text-sm font-semibold text-foreground">{{ title }}</h2>
          <span v-if="badge" :class="cn('shrink-0 text-xs font-semibold', toneText[tone])">
            {{ badge }}
          </span>
        </div>
        <p class="mt-1 text-sm leading-6 text-muted-foreground">{{ description }}</p>
      </div>
      <p class="shrink-0 text-2xl font-semibold leading-none tabular-nums tracking-tight">
        {{ count
        }}<span v-if="unit" class="ml-0.5 text-sm font-medium text-muted-foreground">{{
          unit
        }}</span>
      </p>
    </div>

    <div class="mt-4 flex flex-1 flex-col">
      <ul v-if="visibleItems.length > 0" class="flex flex-col divide-y border-t">
        <li
          v-for="item in visibleItems"
          :key="item.key"
          class="flex min-w-0 items-baseline justify-between gap-3 px-5 py-2.5"
        >
          <span class="truncate text-sm font-medium text-foreground">{{ item.primary }}</span>
          <span class="shrink-0 text-xs tabular-nums text-muted-foreground">{{
            item.secondary
          }}</span>
        </li>
      </ul>

      <div
        v-else-if="showEmptyState"
        class="flex flex-1 flex-col items-center justify-center gap-1 border-t px-6 py-6 text-center"
      >
        <p class="text-sm font-medium text-foreground">{{ emptyTitle }}</p>
        <p class="text-sm leading-6 text-muted-foreground">{{ emptyHint }}</p>
      </div>
    </div>

    <RouterLink
      v-if="to"
      class="group flex items-center justify-between gap-2 border-t px-5 py-3 text-sm font-medium text-brand-strong transition-colors hover:bg-accent"
      :to="to"
    >
      {{ actionLabel ?? '查看全部' }}
      <ArrowRightIcon
        class="size-4 transition-transform group-hover:translate-x-0.5"
        aria-hidden="true"
      />
    </RouterLink>
  </NvCard>
</template>
