<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { Star } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Mobile Rate — star rating with ≥44px tappable stars. Amber active fill, a brief
 * pop on the just-tapped star (reduced-motion off). Optional half steps via
 * `allowHalf` (tap left/right half of a star). For 满意度 / 质检评级 entry on a PDA.
 */
const props = withDefaults(
  defineProps<{
    modelValue?: number
    count?: number
    readonly?: boolean
    allowHalf?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { modelValue: 0, count: 5, readonly: false, allowHalf: false },
)
const emit = defineEmits<{ 'update:modelValue': [value: number] }>()

const stars = computed(() => Array.from({ length: props.count }, (_, i) => i + 1))

/** Fill ratio (0 / 0.5 / 1) of the star at 1-based `index`. */
function fillOf(index: number) {
  const v = props.modelValue
  if (v >= index) return 1
  if (v >= index - 0.5) return 0.5
  return 0
}

function pick(index: number, ev: PointerEvent | MouseEvent) {
  if (props.readonly) return
  let next = index
  if (props.allowHalf) {
    const el = ev.currentTarget as HTMLElement
    const { left, width } = el.getBoundingClientRect()
    if (ev.clientX - left < width / 2) next = index - 0.5
  }
  // tap the only filled star again to clear
  emit('update:modelValue', next === props.modelValue ? 0 : next)
}
</script>

<template>
  <div
    data-slot="rate"
    :class="cn('inline-flex items-center', props.class)"
    role="slider"
    :aria-valuenow="modelValue"
    aria-valuemin="0"
    :aria-valuemax="count"
  >
    <button
      v-for="i in stars"
      :key="i"
      type="button"
      :disabled="readonly"
      class="ds-rate-star grid size-11 place-items-center disabled:cursor-default"
      :aria-label="`${i} 星`"
      @click="pick(i, $event)"
    >
      <span class="ds-rate-glyph relative block size-7">
        <!-- empty base -->
        <Star class="absolute inset-0 size-7 text-muted-foreground/35" aria-hidden="true" />
        <!-- filled overlay, clipped to fill ratio -->
        <span class="absolute inset-0 overflow-hidden" :style="{ width: `${fillOf(i) * 100}%` }">
          <Star class="size-7 fill-current text-amber-400" aria-hidden="true" />
        </span>
      </span>
    </button>
  </div>
</template>

<style scoped>
@layer nv-components {
  .ds-rate-star {
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
  }
  .ds-rate-star:active:not(:disabled) .ds-rate-glyph {
    animation: ds-rate-pop 0.26s var(--ease-out-back, cubic-bezier(0.34, 1.4, 0.64, 1));
  }
  @keyframes ds-rate-pop {
    0% {
      transform: scale(0.8);
    }
    60% {
      transform: scale(1.18);
    }
    100% {
      transform: scale(1);
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-rate-star:active:not(:disabled) .ds-rate-glyph {
      animation: none;
    }
  }
}
</style>
