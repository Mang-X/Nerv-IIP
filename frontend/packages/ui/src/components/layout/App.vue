<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { ConfigProvider, TooltipProvider } from 'reka-ui'
import { Toaster } from '../ui/sonner'
import { cn } from '../../lib/utils'

/**
 * App — root provider + shell wrapper (Nuxt UI `App` equivalent). Mounts the
 * global reka ConfigProvider (reading direction, scroll-lock) + a single
 * TooltipProvider, hosts the toast outlet, and sets the base surface. Wrap the
 * whole application once at the root.
 */
withDefaults(
  defineProps<{
    /** Tooltip open delay shared by every tooltip in the tree. */
    tooltipDelay?: number
    class?: HTMLAttributes['class']
  }>(),
  { tooltipDelay: 200 },
)
</script>

<template>
  <ConfigProvider>
    <TooltipProvider :delay-duration="tooltipDelay">
      <div
        data-slot="app"
        :class="cn('flex min-h-dvh flex-col bg-background text-foreground', $props.class)"
      >
        <slot />
      </div>
      <Toaster />
    </TooltipProvider>
  </ConfigProvider>
</template>
