<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { ChevronLeft, ChevronRight } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — carousel (does NOT touch原版). The desktop counterpart of the mobile
 * Swiper: pointer-draggable track that snaps to the nearest slide, plus PC-only
 * affordances — prev/next arrow buttons and autoplay that pauses on hover. Slides
 * come from `:items` (scoped slot per item) or a free default slot. Self-contained.
 */
const props = withDefaults(
  defineProps<{
    /** Data-driven slides; omit to use the default slot's children. */
    items?: unknown[]
    /** Auto-advance interval in ms (0 disables); pauses while hovered. */
    autoplay?: number
    /** Wrap last → first. */
    loop?: boolean
    /** Dot indicator. */
    dots?: boolean
    /** Prev/next arrow buttons (fade in on hover). */
    arrows?: boolean
    /** Rounded muted viewport backing (for image/banner slides). */
    frame?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { autoplay: 0, loop: false, dots: true, arrows: true, frame: true },
)
const emit = defineEmits<{ change: [index: number] }>()

const active = defineModel<number>('index', { default: 0 })

const viewportEl = ref<HTMLElement>()
const trackEl = ref<HTMLElement>()
const count = ref(0)
const width = ref(0)
const offset = ref(0)
const dragging = ref(false)

let startX = 0
let startOffset = 0
let down = false
let timer: number | undefined

function sync() {
  count.value = trackEl.value?.children.length ?? 0
  width.value = viewportEl.value?.offsetWidth ?? 0
  offset.value = -active.value * width.value
}

const trackStyle = computed(() => ({ transform: `translate3d(${offset.value}px, 0, 0)` }))
const canPrev = computed(() => props.loop || active.value > 0)
const canNext = computed(() => props.loop || active.value < count.value - 1)

function goTo(index: number, animate = true) {
  if (count.value === 0) return
  const next = props.loop
    ? (index + count.value) % count.value
    : Math.min(Math.max(index, 0), count.value - 1)
  active.value = next
  offset.value = -next * width.value
  if (animate) emit('change', next)
}

function onDown(e: PointerEvent) {
  stopAutoplay()
  width.value = viewportEl.value?.offsetWidth ?? 0
  startX = e.clientX
  startOffset = offset.value
  down = true
}
function onMove(e: PointerEvent) {
  if (!down || e.buttons === 0) return
  const dx = e.clientX - startX
  if (!dragging.value && Math.abs(dx) < 6) return
  if (!dragging.value) {
    dragging.value = true
    try {
      ;(e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId)
    } catch {
      // synthetic / released pointers can't be captured — non-fatal
    }
  }
  const raw = startOffset + dx
  const min = -(count.value - 1) * width.value
  // in-range tracks 1:1; clamp hard at the ends (no rubber-band on desktop)
  offset.value = props.loop ? raw : Math.min(0, Math.max(min, raw))
}
function onUp() {
  if (dragging.value && width.value) {
    const moved = offset.value - startOffset
    const threshold = width.value * 0.18
    goTo(active.value + (moved < -threshold ? 1 : moved > threshold ? -1 : 0))
  } else if (down) {
    offset.value = -active.value * width.value
  }
  down = false
  dragging.value = false
  startAutoplay()
}

function startAutoplay() {
  stopAutoplay()
  if (!props.autoplay || count.value <= 1) return
  timer = window.setInterval(() => {
    if (active.value >= count.value - 1) {
      // loop wraps to the first slide; non-loop stops at the last slide — matches
      // the arrows (disabled at the end) and the `loop` contract, so there's no
      // "manual stops but autoplay loops" mismatch.
      if (props.loop) goTo(0)
      else stopAutoplay()
    } else {
      goTo(active.value + 1)
    }
  }, props.autoplay)
}
function stopAutoplay() {
  if (timer) window.clearInterval(timer)
  timer = undefined
}

let ro: ResizeObserver | undefined
onMounted(() => {
  sync()
  // a mount-time index beyond the slide count clamps to the last slide
  if (active.value > count.value - 1) active.value = Math.max(0, count.value - 1)
  startAutoplay()
  if (typeof ResizeObserver !== 'undefined' && viewportEl.value) {
    ro = new ResizeObserver(() => {
      width.value = viewportEl.value?.offsetWidth ?? 0
      offset.value = -active.value * width.value
    })
    ro.observe(viewportEl.value)
  }
})
onBeforeUnmount(() => {
  stopAutoplay()
  ro?.disconnect()
})
// External `index` updates (v-model:index reset, URL sync, an outside indicator)
// must move the track too — re-derive the offset, and clamp an index left out of
// range (e.g. after `items` shrinks) instead of scrolling into empty space.
watch(active, (v) => {
  const max = Math.max(0, count.value - 1)
  if (v < 0 || v > max) {
    active.value = Math.min(Math.max(v, 0), max)
    return
  }
  offset.value = -v * width.value
})
// Runtime `items` change (async load, [] → many): re-measure, clamp the current
// page to the new count, then (re)start autoplay — a carousel mounted empty would
// otherwise never begin playing once its data arrives (count was ≤ 1 at start).
watch(
  () => props.items,
  () =>
    requestAnimationFrame(() => {
      sync()
      const max = Math.max(0, count.value - 1)
      if (active.value > max) active.value = max
      startAutoplay()
    }),
  { deep: true },
)
watch(() => props.autoplay, startAutoplay)
</script>

<template>
  <div class="ds-carousel group/carousel w-full" data-slot="carousel-pro">
    <div
      ref="viewportEl"
      :class="
        cn(
          'ds-carousel-viewport relative w-full overflow-hidden',
          frame && 'rounded-xl bg-muted',
          $props.class,
        )
      "
      @pointerenter="stopAutoplay"
      @pointerleave="startAutoplay"
    >
      <div
        ref="trackEl"
        class="ds-carousel-track flex h-full items-stretch"
        :class="!dragging && 'ds-carousel-snap'"
        :style="trackStyle"
        @pointerdown="onDown"
        @pointermove="onMove"
        @pointerup="onUp"
        @pointercancel="onUp"
      >
        <template v-if="items">
          <div v-for="(item, i) in items" :key="i" class="ds-carousel-item w-full shrink-0">
            <slot :item="item" :index="i" />
          </div>
        </template>
        <slot v-else />
      </div>

      <template v-if="arrows && count > 1">
        <button
          type="button"
          class="ds-carousel-arrow ds-carousel-arrow-prev"
          :disabled="!canPrev"
          aria-label="上一张"
          @click="goTo(active - 1)"
        >
          <ChevronLeft class="size-5" aria-hidden="true" />
        </button>
        <button
          type="button"
          class="ds-carousel-arrow ds-carousel-arrow-next"
          :disabled="!canNext"
          aria-label="下一张"
          @click="goTo(active + 1)"
        >
          <ChevronRight class="size-5" aria-hidden="true" />
        </button>
      </template>

      <div
        v-if="dots && count > 1"
        class="ds-carousel-dots pointer-events-none absolute inset-x-0 bottom-3 flex justify-center gap-1.5"
        aria-hidden="true"
      >
        <span
          v-for="i in count"
          :key="i"
          class="ds-carousel-dot"
          :class="i - 1 === active && 'ds-carousel-dot-active'"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .ds-carousel-track {
    touch-action: pan-y;
    cursor: grab;
  }
  .ds-carousel-track:active {
    cursor: grabbing;
  }
  .ds-carousel-snap {
    transition: transform 0.34s var(--nv-ease-out-expo);
  }
  .ds-carousel-arrow {
    position: absolute;
    top: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    width: 2rem;
    height: 2rem;
    border-radius: 9999px;
    background: color-mix(in oklch, var(--background) 75%, transparent);
    color: var(--foreground);
    box-shadow: 0 1px 4px color-mix(in oklch, black 30%, transparent);
    opacity: 0;
    outline: none;
    transition:
      opacity 0.2s var(--nv-ease-out-quart, ease-out),
      background-color 0.18s ease;
  }
  .ds-carousel-arrow-prev {
    left: 0.75rem;
    transform: translateY(-50%);
  }
  .ds-carousel-arrow-next {
    right: 0.75rem;
    transform: translateY(-50%);
  }
  /* fade the arrows in on hover/focus (PC affordance); always shown on keyboard focus */
  .group\/carousel:hover .ds-carousel-arrow,
  .ds-carousel-arrow:focus-visible {
    opacity: 1;
  }
  /* Explicit high-contrast focus ring — the arrows clear the UA outline, so
   keyboard users need a visible treatment to tell which arrow holds focus over a
   busy slide. */
  .ds-carousel-arrow:focus-visible {
    box-shadow:
      0 1px 4px color-mix(in oklch, black 30%, transparent),
      0 0 0 3px color-mix(in oklch, var(--ring) 50%, transparent);
  }
  .ds-carousel-arrow:hover:not(:disabled) {
    background: var(--background);
  }
  .ds-carousel-arrow:disabled {
    opacity: 0 !important;
    pointer-events: none;
  }
  .ds-carousel-dot {
    height: 6px;
    width: 6px;
    border-radius: 9999px;
    background: color-mix(in oklch, var(--foreground) 38%, transparent);
    box-shadow: 0 0 2px color-mix(in oklch, var(--background) 55%, transparent);
    transition:
      width 0.3s var(--nv-ease-out-expo),
      background-color 0.3s ease;
  }
  .ds-carousel-dot-active {
    width: 16px;
    background: var(--nv-brand);
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-carousel-snap,
    .ds-carousel-arrow,
    .ds-carousel-dot {
      transition: none;
    }
  }
}
</style>
