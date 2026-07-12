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
        'nv-m-ddm relative z-10 flex h-11 items-stretch border-b border-border bg-card',
        $props.class,
      )
    "
  >
    <slot />
    <!-- shared tap-away scrim below the bar, rendered only while a panel is open -->
    <Transition name="nv-m-ddm-scrim">
      <div
        v-if="openId !== null"
        class="nv-m-ddm-scrim absolute inset-x-0 top-full z-30 h-screen bg-black/30"
        aria-hidden="true"
        @click="close"
      />
    </Transition>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-m-ddm-scrim-enter-active,
  .nv-m-ddm-scrim-leave-active {
    transition: opacity 0.22s var(--nv-ease-out-expo);
  }
  .nv-m-ddm-scrim-enter-from,
  .nv-m-ddm-scrim-leave-to {
    opacity: 0;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-m-ddm-scrim-enter-active,
    .nv-m-ddm-scrim-leave-active {
      transition: none;
    }
  }
}
</style>
