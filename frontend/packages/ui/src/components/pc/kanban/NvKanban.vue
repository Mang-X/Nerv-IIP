<script setup lang="ts" generic="T = unknown">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'
import NvBadge from '../badge/NvBadge.vue'

/** Tone for a column header — maps onto NvBadge tonal variants. */
export type KanbanTone = 'neutral' | 'brand' | 'success' | 'warning' | 'danger'

export interface KanbanColumn<TItem = unknown> {
  key: string
  title: string
  tone?: KanbanTone
  items: TItem[]
}

/**
 * Pro — kanban column view (factory scheduling / work-order flow). Display-first:
 * horizontally scrollable fixed-width columns, a header with a tone-tinted count
 * badge, and a vertical card stack. Card content goes through the scoped `#item`
 * slot so consumers render their own cards (e.g. NvRecordCard). Drag is out of
 * scope for this version but columns are keyed for future extension. Never edits原版.
 */
const props = withDefaults(
  defineProps<{
    columns: KanbanColumn<T>[]
    itemKey?: (item: T) => string | number
    class?: HTMLAttributes['class']
  }>(),
  {},
)

function resolveKey(item: T, index: number): string | number {
  return props.itemKey ? props.itemKey(item) : index
}
</script>

<template>
  <div
    data-slot="nv-kanban"
    :class="cn('flex gap-4 overflow-x-auto pb-2', props.class)"
  >
    <section
      v-for="column in columns"
      :key="column.key"
      class="flex w-72 shrink-0 flex-col gap-3"
    >
      <header class="flex items-center justify-between gap-2">
        <h3 class="truncate text-sm font-medium text-foreground">{{ column.title }}</h3>
        <NvBadge :variant="column.tone ?? 'neutral'" class="tabular-nums">
          {{ column.items.length }}
        </NvBadge>
      </header>

      <div class="flex flex-col gap-3">
        <template v-if="column.items.length">
          <slot
            v-for="(item, index) in column.items"
            :key="resolveKey(item, index)"
            name="item"
            :item="item"
            :column="column"
          />
        </template>
        <p
          v-else
          class="rounded-lg border border-dashed border-border px-3 py-6 text-center text-xs text-muted-foreground"
        >
          暂无卡片
        </p>
      </div>
    </section>
  </div>
</template>
