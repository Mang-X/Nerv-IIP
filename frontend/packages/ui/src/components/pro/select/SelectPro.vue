<script setup lang="ts">
import type { SelectRootEmits, SelectRootProps } from 'reka-ui'
import { SelectRoot, useForwardPropsEmits } from 'reka-ui'

/**
 * Pro — copy-rebuilt select (does NOT touch原版 Select). Re-composes the same
 * reka-ui primitives with premium styling consistent with ButtonPro/InputPro:
 * full-width h-9 trigger, brand focus glow, chevron flip, brand-tinted items.
 */
const props = defineProps<SelectRootProps>()
const emits = defineEmits<SelectRootEmits>()
const forwarded = useForwardPropsEmits(props, emits)
</script>

<template>
  <SelectRoot data-slot="select-pro" v-bind="forwarded">
    <slot />
  </SelectRoot>
</template>
