<script setup lang="ts">
import type { DropdownMenuRootEmits, DropdownMenuRootProps } from 'reka-ui'
import { reactiveOmit } from '@vueuse/core'
import { DropdownMenuRoot, useForwardPropsEmits } from 'reka-ui'
import { computed } from 'vue'

/**
 * Pro — dropdown menu root (does NOT touch原版 DropdownMenu). Pure logic
 * container; forwards props/emits to reka's DropdownMenuRoot.
 *
 * `modal` defaults to FALSE (reka/Radix default is true). A dropdown is a
 * non-modal popover that appears alongside its trigger — it should not lock body
 * scroll or set `body { pointer-events: none }` (reka's modal scroll lock does
 * both). Locking a whole page for a menu freezes the background + shifts layout by
 * the scrollbar width on every open. Only true modals (Dialog / AlertDialog /
 * Sheet) keep scroll lock. Pass `:modal="true"` for a menu nested inside a modal
 * that must trap interaction.
 */
const props = defineProps<DropdownMenuRootProps>()
const emits = defineEmits<DropdownMenuRootEmits>()

// Default to non-modal via a computed fallback rather than `withDefaults` — with
// an imported prop type the compiler may not register a runtime default for
// `modal`, leaving it undefined so reka's own `true` wins. `?? false` is robust.
const modal = computed(() => props.modal ?? false)
// Forward the rest; `modal` is bound explicitly below.
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'modal'), emits)
</script>

<template>
  <DropdownMenuRoot
    v-slot="slotProps"
    data-slot="dropdown-menu-pro"
    :modal="modal"
    v-bind="forwarded"
  >
    <slot v-bind="slotProps" />
  </DropdownMenuRoot>
</template>
