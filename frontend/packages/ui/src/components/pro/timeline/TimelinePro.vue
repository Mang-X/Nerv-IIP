<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import type { TimelineItem, TimelineTone } from './types'

/**
 * Pro — a vertical timeline for operation logs / process flow (工序流转·操作日志).
 * Opaque tone-colored nodes sit on a connector rail (the line never bleeds
 * through a node), with a title + meta label + description per event and an
 * optional pulsing "pending" tail node. Display-only; motion limited to the
 * pending pulse (reduced-motion safe).
 */
const props = withDefaults(
  defineProps<{
    items: TimelineItem[]
    /** Append a pulsing in-progress node at the tail. */
    pending?: boolean
    pendingText?: string
    /** Newest-first ordering. */
    reverse?: boolean
    class?: HTMLAttributes['class']
  }>(),
  {
    pending: false,
    pendingText: '进行中…',
    reverse: false,
  },
)

const orderedItems = computed(() => (props.reverse ? [...props.items].reverse() : props.items))

const toneVar: Record<TimelineTone, string> = {
  brand: 'var(--brand)',
  success: 'var(--success)',
  warning: 'var(--warning)',
  danger: 'var(--destructive)',
  neutral: 'var(--muted-foreground)',
}
function dotColor(item: TimelineItem): string {
  return toneVar[item.tone ?? 'brand']
}
</script>

<template>
  <ol :class="cn('ds-tl', props.class)">
    <li v-for="(item, i) in orderedItems" :key="item.key ?? i" class="ds-tl-item">
      <div class="ds-tl-rail">
        <span
          class="ds-tl-dot"
          :data-hollow="item.dotType === 'hollow' || undefined"
          :data-icon="item.icon ? '' : undefined"
          :style="{ '--ds-tl-dot': dotColor(item) }"
        >
          <component :is="item.icon" v-if="item.icon" class="size-3" aria-hidden="true" />
        </span>
        <span v-if="i < orderedItems.length - 1 || pending" class="ds-tl-line" />
      </div>
      <div class="ds-tl-body">
        <div class="ds-tl-head">
          <span v-if="item.title" class="ds-tl-title">{{ item.title }}</span>
          <span v-if="item.label" class="ds-tl-label">{{ item.label }}</span>
        </div>
        <p v-if="item.description" class="ds-tl-desc">{{ item.description }}</p>
        <div v-if="item.key && $slots[item.key]" class="ds-tl-slot">
          <slot :name="item.key" :item="item" />
        </div>
      </div>
    </li>

    <li v-if="pending" class="ds-tl-item">
      <div class="ds-tl-rail">
        <span class="ds-tl-dot ds-tl-dot-pending" style="--ds-tl-dot: var(--brand)">
          <span class="ds-tl-pulse" aria-hidden="true" />
        </span>
      </div>
      <div class="ds-tl-body">
        <span class="ds-tl-title text-muted-foreground">{{ pendingText }}</span>
      </div>
    </li>
  </ol>
</template>

<style scoped>
.ds-tl {
  list-style: none;
  margin: 0;
  padding: 0;
}
.ds-tl-item {
  display: grid;
  grid-template-columns: 0.875rem 1fr;
  gap: 0.75rem;
}
.ds-tl-rail {
  display: flex;
  flex-direction: column;
  align-items: center;
}
/* Opaque node with a card-colored ring so the rail can't show through it. */
.ds-tl-dot {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 0.875rem;
  height: 0.875rem;
  margin-top: 0.28rem;
  border-radius: 9999px;
  background-color: var(--ds-tl-dot);
  box-shadow: 0 0 0 3px var(--card);
  color: var(--brand-foreground);
}
.ds-tl-dot[data-icon] {
  width: 1.375rem;
  height: 1.375rem;
  margin-top: 0.05rem;
}
.ds-tl-dot[data-hollow] {
  background-color: var(--card);
  box-shadow:
    0 0 0 3px var(--card),
    inset 0 0 0 2px var(--ds-tl-dot);
}
/* Connector: a hairline that starts below the node and runs to the next one. */
.ds-tl-line {
  flex: 1;
  width: 2px;
  margin-top: 0.25rem;
  margin-bottom: -0.25rem;
  border-radius: 1px;
  background-color: var(--border);
}
.ds-tl-body {
  min-width: 0;
  padding-bottom: 1.25rem;
}
.ds-tl-item:last-child .ds-tl-body {
  padding-bottom: 0;
}
.ds-tl-head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0.5rem;
}
.ds-tl-title {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--foreground);
  line-height: 1.25rem;
}
.ds-tl-label {
  font-size: 0.75rem;
  font-variant-numeric: tabular-nums;
  color: var(--muted-foreground);
}
.ds-tl-desc {
  margin: 0.25rem 0 0;
  font-size: 0.8125rem;
  line-height: 1.4;
  color: var(--muted-foreground);
  max-width: 70ch;
}
.ds-tl-slot {
  margin-top: 0.5rem;
}

/* Pending node: a calm pulse signalling in-progress. */
.ds-tl-dot-pending {
  background-color: var(--brand);
}
.ds-tl-pulse {
  position: absolute;
  inset: 0;
  border-radius: 9999px;
  background-color: var(--brand);
  animation: ds-tl-ping 1.6s var(--ease-out-quart, ease-out) infinite;
}
@keyframes ds-tl-ping {
  0% {
    transform: scale(1);
    opacity: 0.55;
  }
  70%,
  100% {
    transform: scale(2.4);
    opacity: 0;
  }
}
@media (prefers-reduced-motion: reduce) {
  .ds-tl-pulse {
    animation: none;
  }
}
</style>
