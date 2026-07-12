<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import type { StatusTone } from '../../blocks/status-badge/statusMap'
import NvCard from '../card/NvCard.vue'
import { NvStatusBadge } from '../status'

export interface RecordCardMeta {
  label: string
  value: string | number
}

export interface RecordCardStatus {
  label: string
  tone: StatusTone
}

/**
 * Pro — business-record card template (work order / sales order / task). Header
 * is the human-readable record no (mono, prominent) + status badge; an optional
 * title, a 2–3 column meta grid, and an optional progress bar with a percent
 * label. Composes NvCard + NvStatusBadge. Never edits原版.
 */
const props = withDefaults(
  defineProps<{
    recordNo: string
    title?: string
    status?: RecordCardStatus
    meta?: RecordCardMeta[]
    progress?: number
    interactive?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { interactive: false },
)

const hasProgress = computed(() => typeof props.progress === 'number')
const progressPct = computed(() =>
  hasProgress.value ? Math.max(0, Math.min(100, props.progress as number)) : 0,
)
</script>

<template>
  <NvCard
    :interactive="interactive"
    :class="cn('flex flex-col gap-3 p-4', props.class)"
    data-slot="nv-record-card"
  >
    <div class="flex items-start justify-between gap-3">
      <div class="min-w-0">
        <p class="truncate font-mono text-sm font-medium tracking-tight tabular-nums">
          {{ recordNo }}
        </p>
        <p v-if="title" class="mt-1 truncate text-sm text-muted-foreground">{{ title }}</p>
      </div>
      <NvStatusBadge v-if="status" :label="status.label" :tone="status.tone" class="shrink-0" />
    </div>

    <dl v-if="meta && meta.length" class="grid grid-cols-2 gap-x-4 gap-y-2.5 sm:grid-cols-3">
      <div v-for="(m, i) in meta" :key="i" class="min-w-0">
        <dt class="truncate text-xs text-muted-foreground">{{ m.label }}</dt>
        <dd class="mt-0.5 truncate text-sm font-medium tabular-nums">{{ m.value }}</dd>
      </div>
    </dl>

    <div v-if="hasProgress">
      <div class="flex items-center justify-between text-xs">
        <span class="text-muted-foreground">进度</span>
        <span class="font-medium tabular-nums">{{ Math.round(progressPct) }}%</span>
      </div>
      <div class="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-muted">
        <div class="nv-rc-bar h-full rounded-full bg-brand" :style="{ width: `${progressPct}%` }" />
      </div>
    </div>

    <slot />

    <div v-if="$slots.actions" class="mt-1 flex items-center justify-end gap-2">
      <slot name="actions" />
    </div>
  </NvCard>
</template>

<style scoped>
@layer nv-components {
  .nv-rc-bar {
    transition: width 0.4s var(--nv-ease-out-quart, ease-out);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-rc-bar {
      transition: none;
    }
  }
}
</style>
