<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { ChevronDown } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Mobile Collapse — a single collapsible panel (Vant / tdesign-mobile style).
 * Smooth height animation via the grid 0fr↔1fr technique (no max-height jank),
 * chevron rotates. v-model:open optional; works uncontrolled too.
 */
defineProps<{ title?: string; class?: HTMLAttributes['class'] }>()
const open = defineModel<boolean>('open', { default: false })
</script>

<template>
  <div data-slot="collapse" :class="cn('overflow-hidden bg-card', $props.class)">
    <button
      type="button"
      :aria-expanded="open"
      class="ds-collapse-head flex min-h-touch w-full items-center gap-3 px-4 py-3 text-left text-[15px] active:bg-accent"
      @click="open = !open"
    >
      <span class="min-w-0 flex-1"
        ><slot name="title">{{ title }}</slot></span
      >
      <ChevronDown
        class="ds-collapse-chevron size-5 shrink-0 text-muted-foreground"
        :class="open && 'rotate-180'"
        aria-hidden="true"
      />
    </button>
    <div class="ds-collapse-wrap grid" :class="open ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'">
      <div class="min-h-0 overflow-hidden">
        <div class="px-4 pb-3 text-sm text-muted-foreground"><slot /></div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ds-collapse-head {
  -webkit-tap-highlight-color: transparent;
  touch-action: manipulation;
}
.ds-collapse-chevron {
  transition: transform 0.24s var(--ease-out-quart, ease-out);
}
.ds-collapse-wrap {
  transition: grid-template-rows 0.28s var(--ease-out-expo, cubic-bezier(0.16, 1, 0.3, 1));
}
@media (prefers-reduced-motion: reduce) {
  .ds-collapse-chevron,
  .ds-collapse-wrap {
    transition: none;
  }
}
</style>
