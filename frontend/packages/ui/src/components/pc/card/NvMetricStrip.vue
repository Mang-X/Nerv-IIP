<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { MinusIcon, TrendingDownIcon, TrendingUpIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'
import NvCard from './NvCard.vue'
import { metricToneText, type NvMetricStripCell } from './metric'

/**
 * Pro — one card holding a row of related metrics, separators standing in for
 * card gaps. Highest-density KPI surface: sits atop a list page pinning the key
 * figures on one line without competing with the table below. Each cell owns a
 * label, a headline value and an optional toned sub-line (a delta or a note).
 */
const props = withDefaults(
  defineProps<{
    cells: NvMetricStripCell[]
    class?: HTMLAttributes['class']
  }>(),
  { cells: () => [] },
)

const metaIcon = { up: TrendingUpIcon, down: TrendingDownIcon, flat: MinusIcon } as const
function metaToneClass(tone?: string) {
  if (tone === 'up') return metricToneText.success
  if (tone === 'down') return metricToneText.danger
  return 'text-muted-foreground'
}
</script>

<template>
  <NvCard :class="cn('flex flex-col overflow-hidden p-0 sm:flex-row', props.class)">
    <div
      v-for="(cell, i) in cells"
      :key="cell.label"
      class="flex flex-1 flex-col gap-1 border-border p-4 [&:not(:first-child)]:border-t sm:px-5 sm:[&:not(:first-child)]:border-l sm:[&:not(:first-child)]:border-t-0"
    >
      <p class="truncate text-sm text-muted-foreground">{{ cell.label }}</p>
      <p
        :class="
          cn(
            'truncate text-xl font-semibold tabular-nums tracking-tight',
            cell.valueTone ? metricToneText[cell.valueTone] : '',
          )
        "
      >
        {{ cell.value
        }}<span v-if="cell.unit" class="ml-0.5 text-xs font-medium text-muted-foreground">{{
          cell.unit
        }}</span>
      </p>
      <span
        v-if="cell.meta"
        :class="
          cn('inline-flex items-center gap-1 text-xs tabular-nums', metaToneClass(cell.metaTone))
        "
      >
        <component
          :is="metaIcon[cell.metaTone as 'up' | 'down' | 'flat']"
          v-if="cell.metaTone && cell.metaTone !== 'neutral'"
          class="size-3"
          aria-hidden="true"
        />{{ cell.meta }}
      </span>
    </div>
  </NvCard>
</template>
