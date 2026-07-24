<script setup lang="ts">
import { cn } from '../../../../lib/utils'
import type { UseMetricTooltip } from '../useMetricTooltip'

/**
 * Internal — the shared frosted readout panel for the hand-drawn micro-vizzes
 * (bars / breakdown / target). Given a `useMetricTooltip()` instance it teleports
 * a cursor-following glass tooltip, keeping the panel markup + `--nv-glass-*`
 * styling in one place instead of duplicated per viz. Not a public component.
 */
defineProps<{ tip: UseMetricTooltip }>()
</script>

<template>
  <Teleport to="body">
    <div
      v-if="tip.data.value"
      :ref="tip.setEl"
      class="nv-metric-tip pointer-events-none fixed z-50 min-w-32 rounded-lg p-2.5 text-xs"
      :style="{ left: `${tip.pos.value.left}px`, top: `${tip.pos.value.top}px` }"
    >
      <div v-if="tip.data.value.title" class="mb-1 text-[11px] text-muted-foreground tabular-nums">
        {{ tip.data.value.title }}
      </div>
      <div
        v-for="(row, i) in tip.data.value.rows"
        :key="i"
        class="flex items-baseline justify-between gap-4 tabular-nums"
      >
        <span class="inline-flex items-center text-muted-foreground">
          <span
            v-if="row.swatchClass"
            :class="cn('mr-1.5 size-2 flex-none rounded-sm', row.swatchClass)"
          />
          {{ row.label }}
        </span>
        <b class="font-semibold text-foreground">{{ row.value }}</b>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
@layer nv-components {
  /* Same frosted readout surface the chart crosshair tooltips use (--nv-glass-*),
     so a metric's micro-viz and a full chart read as one system. */
  .nv-metric-tip {
    color: var(--popover-foreground);
    background: var(--nv-glass-bg);
    border: 1px solid var(--nv-glass-border);
    box-shadow: var(--nv-glass-shadow);
    backdrop-filter: var(--nv-glass-filter);
    -webkit-backdrop-filter: var(--nv-glass-filter);
    transition: opacity var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-metric-tip {
      transition: none;
    }
  }
}
</style>
