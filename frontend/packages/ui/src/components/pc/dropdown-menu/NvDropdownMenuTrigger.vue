<script setup lang="ts">
import type { DropdownMenuTriggerProps } from 'reka-ui'
import { DropdownMenuTrigger, useForwardProps } from 'reka-ui'

/**
 * Pro — dropdown menu trigger. Unstyled passthrough (consumer supplies the
 * trigger element, e.g. a NvButton / icon button) — mirrors原版 trigger.
 */
const props = defineProps<DropdownMenuTriggerProps>()

const forwardedProps = useForwardProps(props)
</script>

<template>
  <DropdownMenuTrigger data-slot="nv-dropdown-menu-trigger" v-bind="forwardedProps">
    <slot />
  </DropdownMenuTrigger>
</template>
