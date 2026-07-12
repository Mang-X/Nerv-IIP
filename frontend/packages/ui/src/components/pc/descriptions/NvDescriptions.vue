<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import { NvTooltip, NvTooltipContent, NvTooltipProvider, NvTooltipTrigger } from '../tooltip'
import type { DescriptionItem } from './types'

/**
 * Pro — a key/value details list for entity detail pages (work orders, devices…).
 * Two surfaces: a clean `grid` (card-friendly) and a `bordered` table grid
 * (formal records). Responsive collapse to one column, item spanning, horizontal
 * / vertical label layout, and per-item `#<key>` value slots. With `ellipsis`
 * (+ a fixed `labelWidth`) long **labels** clamp to one line and reveal their
 * full text in a NvTooltip on hover; values are yours to render via slots.
 */
const props = withDefaults(
  defineProps<{
    items: DescriptionItem[]
    /** Pairs per row (collapses to 1 under ~640px). */
    columns?: number
    title?: string
    /** Table-like bordered records (label cells tinted) vs. open grid. */
    bordered?: boolean
    /** Open-grid only: label beside value (`horizontal`) or above (`vertical`). */
    layout?: 'horizontal' | 'vertical'
    /** Fixed label column width (e.g. `6rem`); defaults to content width. */
    labelWidth?: string
    /** Tighter padding + type. */
    size?: 'default' | 'compact'
    /** Placeholder for empty values. */
    emptyText?: string
    /** Clamp the **label** to one line + ellipsis, with a NvTooltip for the full
     *  text on hover. Pair with `labelWidth` to give the label a width to clamp. */
    ellipsis?: boolean
    class?: HTMLAttributes['class']
  }>(),
  {
    columns: 2,
    bordered: false,
    layout: 'horizontal',
    size: 'default',
    emptyText: '—',
    ellipsis: false,
  },
)

function spanOf(item: DescriptionItem): number {
  return Math.max(1, Math.min(item.span ?? 1, props.columns))
}
function isEmpty(item: DescriptionItem): boolean {
  return !item.key && (item.value === undefined || item.value === null || item.value === '')
}

interface PlacedItem {
  item: DescriptionItem
  span: number
}
// Pack items into rows for the bordered grid; the last cell of every row grows
// to fill any leftover columns so the border grid never shows empty tracks.
const rows = computed<PlacedItem[][]>(() => {
  const cols = props.columns
  const out: PlacedItem[][] = []
  let cur: PlacedItem[] = []
  let used = 0
  const flush = () => {
    if (!cur.length) return
    cur[cur.length - 1].span += cols - used
    out.push(cur)
    cur = []
    used = 0
  }
  for (const item of props.items) {
    const span = spanOf(item)
    if (used + span > cols) flush()
    cur.push({ item, span })
    used += span
    if (used >= cols) flush()
  }
  flush()
  return out
})

const gridStyle = computed(() => ({
  '--nv-desc-cols': String(props.columns),
  '--nv-desc-label': props.labelWidth ?? 'auto',
}))
const labelStyle = computed(() => (props.labelWidth ? { width: props.labelWidth } : undefined))
</script>

<template>
  <NvTooltipProvider :delay-duration="280">
    <div :class="cn('nv-desc', size === 'compact' && 'nv-desc-compact', props.class)">
      <div v-if="title || $slots.extra" class="nv-desc-header">
        <h3 v-if="title" class="text-sm font-semibold">{{ title }}</h3>
        <div v-if="$slots.extra" class="ms-auto"><slot name="extra" /></div>
      </div>

      <!-- Bordered: one shared grid so every row's columns line up. Hairlines are
           drawn by a 1px gap over a border-coloured backplate (no per-cell borders). -->
      <div v-if="bordered" class="nv-desc-bordered" :style="gridStyle">
        <template v-for="(row, ri) in rows" :key="ri">
          <template v-for="placed in row" :key="placed.item.key ?? placed.item.label">
            <NvTooltip v-if="ellipsis">
              <NvTooltipTrigger as-child>
                <div class="nv-desc-blabel nv-desc-clip">{{ placed.item.label }}</div>
              </NvTooltipTrigger>
              <NvTooltipContent>{{ placed.item.label }}</NvTooltipContent>
            </NvTooltip>
            <div v-else class="nv-desc-blabel">{{ placed.item.label }}</div>
            <div class="nv-desc-bvalue" :style="{ gridColumn: `span ${placed.span * 2 - 1}` }">
              <slot :name="placed.item.key ?? '__none__'" :item="placed.item">
                <span :class="isEmpty(placed.item) && 'text-muted-foreground'">{{
                  isEmpty(placed.item) ? emptyText : placed.item.value
                }}</span>
              </slot>
            </div>
          </template>
        </template>
      </div>

      <!-- Open grid -->
      <div v-else class="nv-desc-grid" :style="gridStyle">
        <div
          v-for="item in items"
          :key="item.key ?? item.label"
          class="nv-desc-cell"
          :class="layout === 'vertical' ? 'nv-desc-cell-v' : 'nv-desc-cell-h'"
          :style="{ gridColumn: `span ${spanOf(item)}` }"
        >
          <NvTooltip v-if="ellipsis">
            <NvTooltipTrigger as-child>
              <span
                class="nv-desc-label nv-desc-clip"
                :style="layout === 'horizontal' ? labelStyle : undefined"
                >{{ item.label }}</span
              >
            </NvTooltipTrigger>
            <NvTooltipContent>{{ item.label }}</NvTooltipContent>
          </NvTooltip>
          <span
            v-else
            class="nv-desc-label"
            :style="layout === 'horizontal' ? labelStyle : undefined"
            >{{ item.label }}</span
          >
          <span class="nv-desc-value">
            <slot :name="item.key ?? '__none__'" :item="item">
              <span :class="isEmpty(item) && 'text-muted-foreground'">{{
                isEmpty(item) ? emptyText : item.value
              }}</span>
            </slot>
          </span>
        </div>
      </div>
    </div>
  </NvTooltipProvider>
</template>

<style scoped>
@layer nv-components {
  .nv-desc-header {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-bottom: 0.875rem;
  }

  /* ── Open grid ── */
  .nv-desc-grid {
    display: grid;
    grid-template-columns: repeat(var(--nv-desc-cols), minmax(0, 1fr));
    column-gap: 2rem;
    row-gap: 1rem;
  }
  .nv-desc-cell {
    min-width: 0;
  }
  .nv-desc-cell-h {
    display: flex;
    align-items: baseline;
    gap: 0.75rem;
  }
  .nv-desc-cell-v {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }
  .nv-desc-label {
    flex-shrink: 0;
    color: var(--muted-foreground);
    font-size: 0.8125rem;
    line-height: 1.5rem;
  }
  .nv-desc-cell-h .nv-desc-label {
    min-width: 5rem;
  }
  .nv-desc-value {
    min-width: 0;
    flex: 1 1 auto;
    color: var(--foreground);
    font-size: 0.875rem;
    line-height: 1.5rem;
    word-break: break-word;
  }

  /* Single-line clamp + ellipsis for the label (opt-in via `ellipsis`). */
  .nv-desc-clip {
    display: block;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    cursor: default;
  }

  /* ── Bordered ──
   A single grid (shared column tracks → rows align). The 1px gap over a
   border-coloured backplate paints every interior hairline; cells are opaque. */
  .nv-desc-bordered {
    display: grid;
    grid-template-columns: repeat(var(--nv-desc-cols), var(--nv-desc-label, auto) minmax(0, 1fr));
    gap: 1px;
    overflow: hidden;
    background-color: var(--border);
    border: 1px solid var(--border);
    border-radius: 10px;
  }
  .nv-desc-blabel,
  .nv-desc-bvalue {
    padding: 0.625rem 0.875rem;
    font-size: 0.8125rem;
    line-height: 1.375rem;
    background-color: var(--card);
  }
  .nv-desc-blabel {
    white-space: nowrap;
    background-color: color-mix(in oklch, var(--muted) 45%, var(--card));
    color: var(--muted-foreground);
  }
  .nv-desc-bvalue {
    color: var(--foreground);
    word-break: break-word;
  }

  /* ── Compact ── */
  .nv-desc-compact .nv-desc-grid {
    row-gap: 0.625rem;
  }
  .nv-desc-compact .nv-desc-label,
  .nv-desc-compact .nv-desc-value {
    font-size: 0.8125rem;
    line-height: 1.375rem;
  }
  .nv-desc-compact .nv-desc-blabel,
  .nv-desc-compact .nv-desc-bvalue {
    padding: 0.4375rem 0.75rem;
    font-size: 0.78rem;
  }

  /* ── Responsive: single column ── */
  @media (max-width: 640px) {
    .nv-desc-grid {
      grid-template-columns: 1fr;
    }
    .nv-desc-cell {
      grid-column: span 1 !important;
    }
    .nv-desc-bordered {
      grid-template-columns: var(--nv-desc-label, auto) minmax(0, 1fr);
    }
    .nv-desc-bvalue {
      grid-column: span 1 !important;
    }
  }
}
</style>
