<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { useId } from 'vue'
import { ChevronRightIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'

/**
 * Blocks — 可折叠分组面板：把一张长列表按业务父级（工单 / 客户 / 设备…）切成若干组，
 * 每组一个标题行 + 可折叠内容区。用于「平铺列表读不出归属」的看板类页面：
 * 标题行常驻显示父级单号与本组规模，内容区放该组的明细表或卡片。
 *
 * 不承担数据获取与分页——分组与排序由调用方算好后逐组渲染，本组件只管呈现与展开态。
 */
const props = withDefaults(
  defineProps<{
    /** 分组标题，通常是父级单据的人读编号。 */
    title: string
    /** 标题下方的一行辅助信息（物料 / 工作中心 / 交期…）。 */
    subtitle?: string
    /** 标题右侧的本组规模，如 `4 道工序`。 */
    count?: number | string
    /** 折叠时的一行摘要，避免收起后完全看不到组内情况。 */
    collapsedSummary?: string
    /** 整组置灰（如该组已全部完工）。 */
    muted?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { muted: false },
)

/** 展开态（`v-model:open`）；不绑定时组件自持，默认展开。 */
const open = defineModel<boolean>('open', { default: true })

const contentId = useId()
</script>

<template>
  <section
    :class="
      cn(
        'overflow-hidden rounded-xl border border-border bg-card',
        props.muted && 'opacity-70',
        props.class,
      )
    "
    data-slot="nv-group-panel"
  >
    <div class="flex items-center gap-3 px-4 py-3">
      <button
        type="button"
        :aria-expanded="open"
        :aria-controls="contentId"
        class="flex min-w-0 flex-1 items-center gap-2.5 rounded-md text-left outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
        @click="open = !open"
      >
        <ChevronRightIcon
          :class="
            cn('size-4 shrink-0 text-muted-foreground transition-transform', open && 'rotate-90')
          "
          aria-hidden="true"
        />
        <span class="min-w-0">
          <span class="flex flex-wrap items-center gap-x-2 gap-y-1">
            <span class="truncate text-sm font-semibold text-foreground">{{ title }}</span>
            <slot name="meta" />
          </span>
          <span v-if="subtitle" class="mt-0.5 block truncate text-xs text-muted-foreground">
            {{ subtitle }}
          </span>
          <span
            v-else-if="!open && collapsedSummary"
            class="mt-0.5 block truncate text-xs text-muted-foreground"
          >
            {{ collapsedSummary }}
          </span>
        </span>
      </button>
      <span v-if="count !== undefined" class="shrink-0 text-xs text-muted-foreground tabular-nums">
        {{ count }}
      </span>
      <div v-if="$slots.actions" class="flex shrink-0 items-center gap-2">
        <slot name="actions" />
      </div>
    </div>
    <div v-show="open" :id="contentId" class="border-t border-border">
      <slot />
    </div>
  </section>
</template>
