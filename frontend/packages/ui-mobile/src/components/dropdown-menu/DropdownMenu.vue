<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { provide, ref } from 'vue'
import { cn } from '../../lib/utils'
import { dropdownMenuKey } from './context'

/**
 * Mobile DropdownMenu (Arco Design Mobile `DropdownMenu` form) — a horizontal
 * filter bar. Hosts several <DropdownMenuItem> triggers; tapping one opens its
 * option panel below the bar, and only one item is open at a time. Tap-away on
 * the shared scrim or re-tapping the trigger closes it.
 */
defineProps<{ class?: HTMLAttributes['class'] }>()

const openId = ref<string | null>(null)

function toggle(id: string) {
  openId.value = openId.value === id ? null : id
}
function close() {
  openId.value = null
}

provide(dropdownMenuKey, { openId, toggle, close })
</script>

<template>
  <div
    data-slot="dropdown-menu"
    :class="
      cn(
        'ds-ddm relative z-10 flex h-11 items-stretch border-b border-border bg-card',
        $props.class,
      )
    "
  >
    <slot />
    <!-- shared tap-away scrim below the bar, rendered only while a panel is open -->
    <Transition name="ds-ddm-scrim">
      <div
        v-if="openId !== null"
        class="ds-ddm-scrim absolute inset-x-0 top-full z-30 h-screen bg-black/30"
        aria-hidden="true"
        @click="close"
      />
    </Transition>
  </div>
</template>

<style scoped>
@layer nv-components {
  .ds-ddm-scrim-enter-active,
  .ds-ddm-scrim-leave-active {
    transition: opacity 0.22s var(--nv-ease-out-expo, cubic-bezier(0.16, 1, 0.3, 1));
  }
  .ds-ddm-scrim-enter-from,
  .ds-ddm-scrim-leave-to {
    opacity: 0;
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-ddm-scrim-enter-active,
    .ds-ddm-scrim-leave-active {
      transition: none;
    }
  }
}
</style>
