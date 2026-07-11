<script setup lang="ts">
import type { TabsListProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { TabsIndicator, TabsList } from 'reka-ui'
import { cn } from '../../../lib/utils'

const props = defineProps<TabsListProps & { class?: HTMLAttributes['class'] }>()
const delegated = reactiveOmit(props, 'class')
</script>

<template>
  <TabsList
    data-slot="tabs-pro-list"
    v-bind="delegated"
    :class="
      cn('relative inline-flex h-9 w-fit items-center gap-1 rounded-lg bg-muted p-1', props.class)
    "
  >
    <!-- Sliding card pill behind the triggers — glides to the active tab. -->
    <TabsIndicator class="ds-stab-ind" />
    <slot />
  </TabsList>
</template>

<style scoped>
@layer nv-components {
  .ds-stab-ind {
    position: absolute;
    top: 0.25rem;
    bottom: 0.25rem;
    left: 0;
    z-index: 0;
    width: var(--reka-tabs-indicator-size);
    transform: translateX(var(--reka-tabs-indicator-position));
    border-radius: 6px;
    background: var(--card);
    box-shadow:
      inset 0 1px 0 0 color-mix(in oklch, white 6%, transparent),
      var(--nv-shadow-xs);
    transition:
      transform 0.25s var(--nv-ease-out-quart, ease-out),
      width 0.25s var(--nv-ease-out-quart, ease-out);
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-stab-ind {
      transition: none;
    }
  }
}
</style>
