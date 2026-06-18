<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Slider — single-thumb range. Brand filled track, ≥44px thumb hit area,
 * pointer-drag (same gesture model as the other draggables). Optional value bubble
 * above the thumb while dragging. For 数量 / 阈值 adjustment on a PDA.
 */
const props = withDefaults(
  defineProps<{
    modelValue?: number
    min?: number
    max?: number
    step?: number
    showBubble?: boolean
    disabled?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { modelValue: 0, min: 0, max: 100, step: 1, showBubble: false, disabled: false },
)
const emit = defineEmits<{ 'update:modelValue': [value: number] }>()

const trackEl = ref<HTMLElement | null>(null)
const dragging = ref(false)

const ratio = computed(() => {
  const span = props.max - props.min || 1
  return Math.min(1, Math.max(0, (props.modelValue - props.min) / span))
})

function quantize(raw: number) {
  const stepped = Math.round((raw - props.min) / props.step) * props.step + props.min
  const clamped = Math.min(props.max, Math.max(props.min, stepped))
  // avoid float dust like 0.30000000000000004
  return Number(clamped.toFixed(6))
}

function valueFromClientX(clientX: number) {
  const el = trackEl.value
  if (!el) return props.modelValue
  const { left, width } = el.getBoundingClientRect()
  const r = width ? (clientX - left) / width : 0
  return quantize(props.min + Math.min(1, Math.max(0, r)) * (props.max - props.min))
}

function set(clientX: number) {
  const next = valueFromClientX(clientX)
  if (next !== props.modelValue) emit('update:modelValue', next)
}

function onDown(e: PointerEvent) {
  if (props.disabled) return
  dragging.value = true
  ;(e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId)
  set(e.clientX)
}
function onMove(e: PointerEvent) {
  if (!dragging.value || e.buttons === 0) return
  e.preventDefault()
  set(e.clientX)
}
function onUp() {
  dragging.value = false
}

function nudge(delta: number) {
  if (props.disabled) return
  emit('update:modelValue', quantize(props.modelValue + delta * props.step))
}
</script>

<template>
  <div
    data-slot="slider"
    :class="cn('ds-slider relative flex h-11 items-center select-none', disabled && 'opacity-40', props.class)"
  >
    <div
      ref="trackEl"
      class="relative h-1.5 w-full rounded-full bg-muted"
      @pointerdown="onDown"
      @pointermove="onMove"
      @pointerup="onUp"
      @pointercancel="onUp"
    >
      <!-- filled portion -->
      <div
        class="absolute inset-y-0 left-0 rounded-full bg-brand"
        :style="{ width: `${ratio * 100}%` }"
      />
      <!-- thumb (44px hit area centered on the value position) -->
      <div
        class="ds-slider-thumb absolute top-1/2 grid size-11 -translate-x-1/2 -translate-y-1/2 place-items-center"
        :style="{ left: `${ratio * 100}%` }"
        role="slider"
        tabindex="0"
        :aria-valuenow="modelValue"
        :aria-valuemin="min"
        :aria-valuemax="max"
        :aria-disabled="disabled"
        @keydown.left.prevent="nudge(-1)"
        @keydown.down.prevent="nudge(-1)"
        @keydown.right.prevent="nudge(1)"
        @keydown.up.prevent="nudge(1)"
      >
        <!-- Two concentric discs (WinUI3): a solid brand outer circle with a
             smaller dark circle sitting directly inside it (no ring/gap). The
             inner disc is small at rest, grows on hover, shrinks while pressed. -->
        <span
          class="ds-slider-ring grid size-5 place-items-center rounded-full bg-brand shadow-[0_1px_5px_rgb(0_0_0/0.35)]"
        >
          <span class="ds-slider-dot block size-3.5 rounded-full bg-background" :class="dragging && 'is-active'" />
        </span>
        <span
          v-if="showBubble && dragging"
          class="pointer-events-none absolute -top-8 rounded-md bg-foreground px-2 py-0.5 text-xs font-medium tabular-nums text-background"
        >
          {{ modelValue }}
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ds-slider {
  -webkit-tap-highlight-color: transparent;
}
.ds-slider-thumb {
  touch-action: none;
}
/* The outer ring is fixed; the focus outline lives here so it frames the whole
   thumb. */
.ds-slider-thumb:focus-visible .ds-slider-ring {
  outline: 2px solid var(--ring);
  outline-offset: 2px;
}
/* WinUI3-style inner dot: visible at rest, grows a touch on hover, then shrinks
   below rest the instant the pointer goes down. The pressed rule is qualified
   with `.ds-slider-thumb` so it OUT-SPECIFIES the hover rule (0,4,0 / 0,3,0) —
   otherwise hover (still active while pressing) would keep it big. */
.ds-slider-dot {
  transform: scale(0.92);
  transition: transform 0.16s var(--ease-out-quart, cubic-bezier(0.25, 1, 0.5, 1));
}
.ds-slider-thumb:hover .ds-slider-dot,
.ds-slider-thumb:focus-visible .ds-slider-dot {
  transform: scale(1);
}
.ds-slider-thumb .ds-slider-dot.is-active,
.ds-slider-thumb:hover .ds-slider-dot.is-active,
.ds-slider-thumb:focus-visible .ds-slider-dot.is-active {
  transform: scale(0.7);
}
@media (prefers-reduced-motion: reduce) {
  .ds-slider-dot {
    transition: none;
  }
}
</style>
