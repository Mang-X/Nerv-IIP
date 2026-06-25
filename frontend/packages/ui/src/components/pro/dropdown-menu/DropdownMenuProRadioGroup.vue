<script setup lang="ts">
import type { DropdownMenuRadioGroupEmits, DropdownMenuRadioGroupProps } from 'reka-ui'
import { DropdownMenuRadioGroup, useForwardPropsEmits } from 'reka-ui'

/**
 * Pro — dropdown menu radio group. Pure logic container; forwards props/emits.
 */
const props = defineProps<DropdownMenuRadioGroupProps>()
const emits = defineEmits<DropdownMenuRadioGroupEmits>()

const forwarded = useForwardPropsEmits(props, emits)
</script>

<template>
  <DropdownMenuRadioGroup data-slot="dropdown-menu-pro-radio-group" v-bind="forwarded">
    <slot />
  </DropdownMenuRadioGroup>
</template>
