<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { useSlots } from 'vue'
import { ChevronRight } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Mobile Cell — a single info/form row (tdesign-mobile style): leading icon,
 * title + optional note, trailing value, optional arrow. Touch height ≥48px.
 */
withDefaults(
  defineProps<{
    title: string
    note?: string
    value?: string | number
    arrow?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { arrow: false },
)
const emit = defineEmits<{ click: [] }>()
const slots = useSlots()
</script>

<template>
  <div
    data-slot="cell"
    :role="arrow ? 'button' : undefined"
    :tabindex="arrow ? 0 : undefined"
    :class="
      cn(
        'ds-cell relative flex min-h-touch items-center gap-3 bg-card px-4 py-2.5 text-left',
        arrow && 'active:bg-accent',
        $props.class,
      )
    "
    @click="arrow && emit('click')"
  >
    <span v-if="slots.icon" class="flex shrink-0 items-center text-muted-foreground [&_svg]:size-5">
      <slot name="icon" />
    </span>
    <div class="min-w-0 flex-1">
      <div class="text-[15px] text-foreground">{{ title }}</div>
      <div v-if="note" class="mt-0.5 text-sm text-muted-foreground">{{ note }}</div>
    </div>
    <div class="flex shrink-0 items-center gap-1 text-[15px] text-muted-foreground">
      <slot name="value">{{ value }}</slot>
    </div>
    <ChevronRight
      v-if="arrow"
      class="size-5 shrink-0 text-muted-foreground/70"
      aria-hidden="true"
    />
  </div>
</template>

<style scoped>
/* Full-bleed hairline separator (consistent with ListRow / VirtualList / the
   rest of the system); suppressed on the last row of a group. */
.ds-cell::after {
  content: '';
  position: absolute;
  right: 0;
  bottom: 0;
  left: 0;
  height: 1px;
  background: var(--border);
  pointer-events: none;
}
.ds-cell:last-child::after {
  display: none;
}
</style>
