<script setup lang="ts">
import type { DropdownMenuGroupProps } from 'reka-ui'
import { DropdownMenuGroup } from 'reka-ui'

/**
 * Pro — dropdown menu group. Unstyled semantic grouping container.
 */
const props = defineProps<DropdownMenuGroupProps>()
</script>

<template>
  <DropdownMenuGroup data-slot="nv-dropdown-menu-group" v-bind="props">
    <slot />
  </DropdownMenuGroup>
</template>
