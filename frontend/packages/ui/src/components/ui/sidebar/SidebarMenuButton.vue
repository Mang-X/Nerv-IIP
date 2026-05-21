<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import { Primitive } from 'reka-ui'
import Tooltip from '../tooltip/Tooltip.vue'
import TooltipContent from '../tooltip/TooltipContent.vue'
import TooltipTrigger from '../tooltip/TooltipTrigger.vue'
import { useSidebar } from './utils'

const props = withDefaults(defineProps<{
  asChild?: boolean
  isActive?: boolean
  size?: 'default' | 'sm' | 'lg'
  tooltip?: string
  class?: string
}>(), {
  asChild: false,
})

const { state, isMobile } = useSidebar()

const buttonClass = computed(() =>
  cn(
    'peer/menu-button flex w-full items-center gap-2 overflow-hidden rounded-md p-2 text-left text-sm outline-none ring-sidebar-ring',
    'transition-[width,height,padding] hover:bg-sidebar-accent hover:text-sidebar-accent-foreground',
    'focus-visible:ring-2 active:bg-sidebar-accent active:text-sidebar-accent-foreground',
    'disabled:pointer-events-none disabled:opacity-50',
    'aria-disabled:pointer-events-none aria-disabled:opacity-50',
    'data-[active=true]:bg-sidebar-accent data-[active=true]:font-medium data-[active=true]:text-sidebar-accent-foreground',
    'group-data-[collapsible=icon]:size-8 group-data-[collapsible=icon]:p-2 [&>span:last-child]:truncate',
    '[&>svg]:size-4 [&>svg]:shrink-0',
    props.size === 'sm' && 'h-7 text-xs',
    props.size === 'lg' && 'h-12 text-sm group-data-[collapsible=icon]:p-0',
    (!props.size || props.size === 'default') && 'h-8',
    props.isActive && 'bg-sidebar-accent font-medium text-sidebar-accent-foreground',
    props.class,
  )
)

const showTooltip = computed(() => Boolean(props.tooltip) && state.value === 'collapsed' && !isMobile.value)
</script>

<template>
  <Tooltip v-if="showTooltip">
    <TooltipTrigger as-child>
      <Primitive
        data-slot="sidebar-menu-button"
        :data-active="isActive"
        :as="asChild ? 'template' : 'button'"
        :as-child="asChild"
        :class="buttonClass"
      >
        <slot />
      </Primitive>
    </TooltipTrigger>
    <TooltipContent side="right" align="center" :hidden="!showTooltip">
      {{ tooltip }}
    </TooltipContent>
  </Tooltip>
  <Primitive
    v-else
    data-slot="sidebar-menu-button"
    :data-active="isActive"
    :as="asChild ? 'template' : 'button'"
    :as-child="asChild"
    :class="buttonClass"
  >
    <slot />
  </Primitive>
</template>
