<script setup lang="ts">
import type { SwitchRootEmits, SwitchRootProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { SwitchRoot, SwitchThumb, useForwardPropsEmits } from 'reka-ui'
import { ref } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile switch — iOS proportions (51×31), large thumb with a spring glide.
 * Bigger than the desktop SwitchPro; reads as a native control on touch.
 */
const props = defineProps<SwitchRootProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<SwitchRootEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class'), emits)

// WinUI3/iOS-style press: the thumb depresses while held, then springs back.
const pressed = ref(false)
</script>

<template>
  <SwitchRoot
    data-slot="mobile-switch"
    v-bind="forwarded"
    @pointerdown="pressed = true"
    @pointerup="pressed = false"
    @pointerleave="pressed = false"
    @pointercancel="pressed = false"
    :class="
      cn(
        'ds-mswitch inline-flex h-[31px] w-[51px] shrink-0 cursor-pointer items-center rounded-full px-0.5 outline-none transition-colors focus-visible:ring-[3px] focus-visible:ring-brand/30 disabled:cursor-not-allowed disabled:opacity-50 data-[state=checked]:bg-brand data-[state=unchecked]:bg-muted-foreground/35',
        props.class,
      )
    "
  >
    <SwitchThumb
      class="ds-mswitch-thumb pointer-events-none block size-[27px] rounded-full bg-white shadow-[0_2px_4px_rgb(0_0_0/0.2)] ring-0 data-[state=checked]:translate-x-5 data-[state=unchecked]:translate-x-0"
      :class="pressed && 'is-pressed'"
    />
  </SwitchRoot>
</template>

<style scoped>
@layer nv-components {
  .ds-mswitch {
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
    transition: background-color 0.2s var(--nv-ease-out-quart, ease-out);
  }
  .ds-mswitch-thumb {
    /* Tailwind v4 glides the thumb via the `translate` property — name it in the
     transition (a `transform`-only transition never fires). `transform` carries
     the press depress (separate property, composes with `translate`). Slight
     back-ease spring for the native iOS feel. */
    transition:
      translate 0.26s var(--nv-ease-out-quart),
      transform 0.18s var(--nv-ease-out-quart);
  }
  .ds-mswitch-thumb.is-pressed {
    transform: scale(0.9);
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-mswitch-thumb {
      transition: none;
    }
  }
}
</style>
