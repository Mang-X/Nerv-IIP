<script setup lang="ts">
import type { DropdownMenuContentEmits, DropdownMenuContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { DropdownMenuContent, DropdownMenuPortal, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../../lib/utils'

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(
  defineProps<DropdownMenuContentProps & { class?: HTMLAttributes['class'] }>(),
  {
    align: 'start',
    sideOffset: 4,
  },
)
const emits = defineEmits<DropdownMenuContentEmits>()

const delegatedProps = reactiveOmit(props, 'class')

const forwarded = useForwardPropsEmits(delegatedProps, emits)
</script>

<template>
  <DropdownMenuPortal>
    <DropdownMenuContent
      data-slot="dropdown-menu-content"
      v-bind="{ ...$attrs, ...forwarded }"
      :class="
        cn(
          'ds-dropdown-menu-content ring-foreground/10 bg-popover text-popover-foreground min-w-32 rounded-lg p-1 shadow-md ring-1 cn-menu-target cn-menu-translucent z-50 max-h-(--reka-dropdown-menu-content-available-height) w-(--reka-dropdown-menu-trigger-width) origin-(--reka-dropdown-menu-content-transform-origin) overflow-x-hidden overflow-y-auto data-[state=closed]:overflow-hidden',
          props.class,
        )
      "
    >
      <slot />
    </DropdownMenuContent>
  </DropdownMenuPortal>
</template>

<style>
.ds-dropdown-menu-content {
  --dropdown-content-open-duration: var(--duration-fast-invoke, 187ms);
  --dropdown-content-close-duration: var(--duration-fade, 83ms);
  --dropdown-content-transform-ease: var(--ease-fast-invoke, cubic-bezier(0, 0, 0, 1));
  --dropdown-content-opacity-ease: linear;
  --dropdown-content-open-scale: 0.97;
  --dropdown-content-close-scale: 0.99;
  --dropdown-content-offset-x: 0;
  --dropdown-content-offset-y: -2px;
  transform-origin: var(--reka-dropdown-menu-content-transform-origin);
  will-change: transform, opacity;
}

.ds-dropdown-menu-content[data-side='top'] {
  --dropdown-content-offset-y: 2px;
}

.ds-dropdown-menu-content[data-side='left'] {
  --dropdown-content-offset-x: 2px;
  --dropdown-content-offset-y: 0;
}

.ds-dropdown-menu-content[data-side='right'] {
  --dropdown-content-offset-x: -2px;
  --dropdown-content-offset-y: 0;
}

.ds-dropdown-menu-content[data-state='open'] {
  animation:
    ds-dropdown-content-open-transform var(--dropdown-content-open-duration) var(--dropdown-content-transform-ease) both,
    ds-dropdown-content-open-opacity var(--duration-fade, 83ms) var(--dropdown-content-opacity-ease) both;
}

.ds-dropdown-menu-content[data-state='closed'] {
  animation:
    ds-dropdown-content-close-transform var(--dropdown-content-close-duration) var(--dropdown-content-transform-ease) both,
    ds-dropdown-content-close-opacity var(--dropdown-content-close-duration) var(--dropdown-content-opacity-ease) both;
}

@keyframes ds-dropdown-content-open-transform {
  from {
    transform: translate(var(--dropdown-content-offset-x), var(--dropdown-content-offset-y)) scale(var(--dropdown-content-open-scale));
  }
  to {
    transform: translate(0, 0) scale(1);
  }
}

@keyframes ds-dropdown-content-open-opacity {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

@keyframes ds-dropdown-content-close-transform {
  from {
    transform: translate(0, 0) scale(1);
  }
  to {
    transform: translate(var(--dropdown-content-offset-x), var(--dropdown-content-offset-y)) scale(var(--dropdown-content-close-scale));
  }
}

@keyframes ds-dropdown-content-close-opacity {
  from {
    opacity: 1;
  }
  to {
    opacity: 0;
  }
}

@media (prefers-reduced-motion: reduce) {
  .ds-dropdown-menu-content {
    animation: none !important;
  }
}
</style>
