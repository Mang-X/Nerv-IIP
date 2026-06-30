<script setup lang="ts">
import type { SelectContentEmits, SelectContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { SelectContent, SelectPortal, SelectViewport, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../../lib/utils'
import { SelectScrollDownButton, SelectScrollUpButton } from '.'

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(
  defineProps<SelectContentProps & { class?: HTMLAttributes['class'] }>(),
  {
    position: 'item-aligned',
    align: 'center',
  },
)
const emits = defineEmits<SelectContentEmits>()

const delegatedProps = reactiveOmit(props, 'class')

const forwarded = useForwardPropsEmits(delegatedProps, emits)
</script>

<template>
  <SelectPortal>
    <SelectContent
      data-slot="select-content"
      :data-align-trigger="position === 'item-aligned'"
      v-bind="{ ...$attrs, ...forwarded }"
      :class="
        cn(
          'ds-select-content bg-popover text-popover-foreground ring-foreground/10 min-w-36 rounded-lg shadow-md ring-1 cn-menu-target cn-menu-translucent relative z-50 max-h-(--reka-select-content-available-height) origin-(--reka-select-content-transform-origin) overflow-x-hidden overflow-y-auto',
          position === 'popper' &&
            'data-[side=bottom]:translate-y-1 data-[side=left]:-translate-x-1 data-[side=right]:translate-x-1 data-[side=top]:-translate-y-1',
          props.class,
        )
      "
    >
      <SelectScrollUpButton />
      <SelectViewport
        :data-position="position"
        :class="
          cn(
            'data-[position=popper]:h-[var(--reka-select-trigger-height)] data-[position=popper]:w-full data-[position=popper]:min-w-[var(--reka-select-trigger-width)]',
          )
        "
      >
        <slot />
      </SelectViewport>
      <SelectScrollDownButton />
    </SelectContent>
  </SelectPortal>
</template>

<style>
.ds-select-content {
  --select-content-open-duration: var(--duration-fast-invoke, 187ms);
  --select-content-close-duration: var(--duration-fade, 83ms);
  --select-content-transform-ease: var(--ease-fast-invoke, cubic-bezier(0, 0, 0, 1));
  --select-content-opacity-ease: linear;
  --select-content-open-scale: 0.97;
  --select-content-close-scale: 0.99;
  --select-content-offset-x: 0;
  --select-content-offset-y: -2px;
  transform-origin: var(--reka-select-content-transform-origin);
  will-change: transform, opacity;
}

.ds-select-content[data-side='top'] {
  --select-content-offset-y: 2px;
}

.ds-select-content[data-side='left'] {
  --select-content-offset-x: 2px;
  --select-content-offset-y: 0;
}

.ds-select-content[data-side='right'] {
  --select-content-offset-x: -2px;
  --select-content-offset-y: 0;
}

.ds-select-content[data-state='open'] {
  animation:
    ds-select-content-open-transform var(--select-content-open-duration) var(--select-content-transform-ease) both,
    ds-select-content-open-opacity var(--duration-fade, 83ms) var(--select-content-opacity-ease) both;
}

.ds-select-content[data-state='closed'] {
  animation:
    ds-select-content-close-transform var(--select-content-close-duration) var(--select-content-transform-ease) both,
    ds-select-content-close-opacity var(--select-content-close-duration) var(--select-content-opacity-ease) both;
}

@keyframes ds-select-content-open-transform {
  from {
    transform: translate(var(--select-content-offset-x), var(--select-content-offset-y)) scale(var(--select-content-open-scale));
  }
  to {
    transform: translate(0, 0) scale(1);
  }
}

@keyframes ds-select-content-open-opacity {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

@keyframes ds-select-content-close-transform {
  from {
    transform: translate(0, 0) scale(1);
  }
  to {
    transform: translate(var(--select-content-offset-x), var(--select-content-offset-y)) scale(var(--select-content-close-scale));
  }
}

@keyframes ds-select-content-close-opacity {
  from {
    opacity: 1;
  }
  to {
    opacity: 0;
  }
}

@media (prefers-reduced-motion: reduce) {
  .ds-select-content {
    animation: none !important;
  }
}
</style>
