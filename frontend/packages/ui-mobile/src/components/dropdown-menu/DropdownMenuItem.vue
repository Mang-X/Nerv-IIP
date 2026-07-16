<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, getCurrentInstance, inject } from 'vue'
import { Check, ChevronDown } from '@lucide/vue'
import { cn } from '../../lib/utils'
import { dropdownMenuKey } from './context'

/**
 * A single filter trigger inside <DropdownMenu>. Shows the current selection as
 * its label with a chevron that rotates when open; tapping opens a panel of
 * `options` below the bar. Selecting one updates the model and emits `change`.
 */
export interface DropdownOption {
  label: string
  value: string | number
}

const props = withDefaults(
  defineProps<{
    /** Fallback trigger label shown when nothing is selected. */
    title: string
    options: DropdownOption[]
    class?: HTMLAttributes['class']
  }>(),
  {},
)
const emit = defineEmits<{ change: [value: string | number] }>()
const model = defineModel<string | number>()

const ctx = inject(dropdownMenuKey)
if (!ctx) throw new Error('DropdownMenuItem must be used inside <DropdownMenu>')

// stable per-instance id so the bar can track which single item is open
const id = `ddm-${getCurrentInstance()?.uid ?? Math.random().toString(36).slice(2)}`
const open = computed(() => ctx.openId.value === id)

const selectedLabel = computed(
  () => props.options.find((o) => o.value === model.value)?.label ?? props.title,
)
const isActive = computed(() => model.value != null)

function pick(value: string | number) {
  model.value = value
  emit('change', value)
  ctx!.close()
}
</script>

<template>
  <!-- min-w-0 lets the flex-1 trigger shrink below its content width so the label
       truncates and the filter bar never overflows (Arco equal-width behaviour);
       without it the item keeps min-width:auto and long labels push the bar past
       the viewport with no way to scroll. -->
  <div data-slot="dropdown-menu-item" class="relative flex min-w-0 flex-1 items-stretch">
    <button
      type="button"
      class="nv-m-ddm-trigger flex h-full w-full min-w-0 items-center justify-center gap-1 px-3 text-[15px] transition-colors"
      :class="
        cn(open || isActive ? 'text-brand' : 'text-foreground', 'active:bg-accent', $props.class)
      "
      :aria-expanded="open"
      @click="ctx!.toggle(id)"
    >
      <span class="truncate">{{ selectedLabel }}</span>
      <ChevronDown
        class="nv-m-ddm-chevron size-4 shrink-0 transition-transform"
        :class="open && 'rotate-180'"
        aria-hidden="true"
      />
    </button>

    <!-- dropdown panel: slides down from under the bar -->
    <Transition name="nv-m-ddm-panel">
      <div
        v-if="open"
        class="nv-m-ddm-panel absolute inset-x-0 top-full z-40 origin-top overflow-hidden border-b border-border bg-card shadow-[0_12px_28px_-12px_rgb(0_0_0/0.35)]"
      >
        <button
          v-for="opt in options"
          :key="opt.value"
          type="button"
          class="flex min-h-touch w-full items-center justify-between gap-3 px-4 py-3 text-left text-[15px] transition-colors active:bg-accent"
          :class="opt.value === model ? 'text-brand' : 'text-foreground'"
          @click="pick(opt.value)"
        >
          <span class="truncate">{{ opt.label }}</span>
          <Check v-if="opt.value === model" class="size-4 shrink-0" aria-hidden="true" />
        </button>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-m-ddm-trigger {
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
  }
  .nv-m-ddm-panel-enter-active,
  .nv-m-ddm-panel-leave-active {
    transition:
      transform 0.26s var(--nv-ease-out-expo),
      opacity 0.26s var(--nv-ease-out-expo);
  }
  .nv-m-ddm-panel-enter-from,
  .nv-m-ddm-panel-leave-to {
    transform: translateY(-8px);
    opacity: 0;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-m-ddm-panel-enter-active,
    .nv-m-ddm-panel-leave-active,
    .nv-m-ddm-chevron {
      transition: none;
    }
  }
}
</style>
