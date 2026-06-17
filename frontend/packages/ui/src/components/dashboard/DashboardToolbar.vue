<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * DashboardToolbar — secondary bar under a `DashboardNavbar` (Nuxt UI Pro
 * style) for tabs / filters / segmented controls; default slot is leading,
 * `#right` is trailing.
 */
const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <div
    data-slot="dashboard-toolbar"
    :class="
      cn(
        'flex h-11 shrink-0 items-center gap-2 overflow-x-auto border-b border-border bg-background px-4',
        props.class,
      )
    "
  >
    <slot />
    <div v-if="$slots.right" class="ms-auto flex items-center gap-1.5">
      <slot name="right" />
    </div>
  </div>
</template>
