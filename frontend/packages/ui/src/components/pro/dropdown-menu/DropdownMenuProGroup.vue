<script setup lang="ts">
import type { DropdownMenuGroupProps } from 'reka-ui'
import { DropdownMenuGroup } from 'reka-ui'

/**
 * Pro — dropdown menu group. Unstyled semantic grouping container.
 */
const props = defineProps<DropdownMenuGroupProps>()
</script>

<template>
  <DropdownMenuGroup data-slot="dropdown-menu-pro-group" v-bind="props">
    <slot />
  </DropdownMenuGroup>
</template>
