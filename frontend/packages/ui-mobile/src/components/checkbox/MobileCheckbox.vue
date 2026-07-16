<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { Check } from '@lucide/vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Checkbox — tappable row (box + label) in tdesign-mobile / Vant style.
 * Brand fill + check when on; ≥44px touch row. v-model is boolean.
 */
withDefaults(
  defineProps<{
    disabled?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { disabled: false },
)
const model = defineModel<boolean>({ default: false })

function toggle() {
  model.value = !model.value
}
</script>

<template>
  <button
    type="button"
    data-slot="mobile-checkbox"
    role="checkbox"
    :aria-checked="model"
    :disabled="disabled"
    :class="
      cn(
        'nv-m-mcheck flex min-h-11 w-full items-center gap-3 text-left text-[15px] select-none disabled:opacity-40',
        $props.class,
      )
    "
    @click="toggle"
  >
    <span
      :class="
        cn(
          'nv-m-mcheck-box grid size-[22px] shrink-0 place-items-center rounded-md border-2 transition-[colors,transform]',
          model
            ? 'border-brand bg-brand text-brand-foreground'
            : 'border-muted-foreground/40 bg-transparent',
        )
      "
    >
      <Check
        class="nv-m-mcheck-tick size-3.5"
        :class="model ? 'scale-100 opacity-100' : 'scale-0 opacity-0'"
        stroke-width="3"
        aria-hidden="true"
      />
    </span>
    <span class="min-w-0 flex-1"><slot /></span>
  </button>
</template>

<style scoped>
@layer nv-components {
  .nv-m-mcheck {
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
  }
  /* WinUI3-style press: the box depresses (shrinks) while held, then springs back. */
  .nv-m-mcheck-box {
    transition:
      background-color 0.18s ease,
      border-color 0.18s ease,
      transform 0.2s var(--nv-ease-out-quart);
  }
  .nv-m-mcheck:active:not(:disabled) .nv-m-mcheck-box {
    transform: scale(0.88);
  }
  .nv-m-mcheck-tick {
    transition:
      transform 0.18s var(--nv-ease-out-quart),
      opacity 0.14s ease;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-m-mcheck-tick {
      transition: opacity 0.12s linear;
    }
  }
}
</style>
