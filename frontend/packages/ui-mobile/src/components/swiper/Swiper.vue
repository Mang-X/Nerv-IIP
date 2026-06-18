<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { clampRubber, cn } from '../../lib/utils'

/**
 * Mobile Swiper — horizontal carousel (Arco Design Mobile `Swiper` form).
 * Pointer-driven: the track tracks the finger 1:1 within range and rubber-bands
 * at the ends when not looping; on release it snaps to the nearest slide. Slides
 * come from `:items` (default scoped slot per item) or a free default slot.
 * Self-contained — no external carousel lib.
 */
const props = withDefaults(
  defineProps<{
    /** Optional data-driven slides; if omitted, the default slot's children are used. */
    items?: unknown[]
    /** Auto-advance interval in ms (0 / undefined disables). */
    autoplay?: number
    /** Wrap around from last → first (and enable infinite autoplay). */
    loop?: boolean
    /** Show the dot indicator. */
    dots?: boolean
    /**
     * Indicator placement. `overlay` floats dots over the bottom of the slide
     * (good for image / banner carousels). `outside` puts them in their own row
     * below the slide so they never cover interactive content (buttons, links).
     */
    indicator?: 'overlay' | 'outside'
    class?: HTMLAttributes['class']
  }>(),
  { autoplay: 0, loop: false, dots: true, indicator: 'overlay' },
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
let startY = 0
let startOffset = 0
let axis: 'h' | 'v' | null = null
let timer: number | undefined

function syncCount() {
  count.value = trackEl.value?.children.length ?? 0
  width.value = viewportEl.value?.offsetWidth ?? 0
  offset.value = -active.value * width.value
}

const trackStyle = computed(() => ({
  transform: `translate3d(${offset.value}px, 0, 0)`,
}))

function goTo(index: number, animate = true) {
  if (count.value === 0) return
  let next = index
  if (props.loop) {
    next = (index + count.value) % count.value
  } else {
    next = Math.min(Math.max(index, 0), count.value - 1)
  }
  active.value = next
  offset.value = -next * width.value
  if (animate) emit('change', next)
}

function onDown(e: PointerEvent) {
  stopAutoplay()
  width.value = viewportEl.value?.offsetWidth ?? 0
  startX = e.clientX
  startY = e.clientY
  startOffset = offset.value
  axis = null
}
function onMove(e: PointerEvent) {
  if (e.buttons === 0) return
  const dx = e.clientX - startX
  const dy = e.clientY - startY
  if (axis === null) {
    if (Math.abs(dx) < 6 && Math.abs(dy) < 6) return
    axis = Math.abs(dx) > Math.abs(dy) ? 'h' : 'v'
    if (axis === 'h') {
      dragging.value = true
      try {
        ;(e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId)
      } catch {
        // synthetic / already-released pointers can't be captured — non-fatal
      }
    }
  }
  if (axis === 'h') {
    e.preventDefault()
    const raw = startOffset + dx
    if (props.loop) {
      offset.value = raw
    } else {
      // in-range tracks 1:1; past either end rubber-bands
      const min = -(count.value - 1) * width.value
      offset.value = clampRubber(raw, min, 0)
    }
  }
}
function onUp() {
  if (axis === 'h' && width.value) {
    const moved = offset.value - startOffset
    const threshold = width.value * 0.2
    let delta = 0
    if (moved < -threshold) delta = 1
    else if (moved > threshold) delta = -1
    goTo(active.value + delta)
  }
  dragging.value = false
  axis = null
  startAutoplay()
}

function startAutoplay() {
  stopAutoplay()
  if (!props.autoplay || count.value <= 1) return
  timer = window.setInterval(() => {
    if (!props.loop && active.value >= count.value - 1) goTo(0)
    else goTo(active.value + 1)
  }, props.autoplay)
}
function stopAutoplay() {
  if (timer) window.clearInterval(timer)
  timer = undefined
}

let ro: ResizeObserver | undefined
onMounted(() => {
  syncCount()
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
watch(() => props.items, () => requestAnimationFrame(syncCount), { deep: true })
watch(() => props.autoplay, startAutoplay)
</script>

<template>
  <div class="ds-swiper w-full" data-slot="swiper">
    <div
      ref="viewportEl"
      :class="
        cn('ds-swiper-viewport relative w-full overflow-hidden rounded-2xl bg-muted', $props.class)
      "
    >
      <!-- h-full + items-stretch make every slide fill the viewport height -->
      <div
        ref="trackEl"
        class="ds-swiper-track flex h-full items-stretch"
        :class="!dragging && 'ds-swiper-snap'"
        :style="trackStyle"
        @pointerdown="onDown"
        @pointermove="onMove"
        @pointerup="onUp"
        @pointercancel="onUp"
      >
        <template v-if="items">
          <div v-for="(item, i) in items" :key="i" class="ds-swiper-item w-full shrink-0">
            <slot :item="item" :index="i" />
          </div>
        </template>
        <slot v-else />
      </div>

      <!-- overlay dots float over the slide (banner / image carousels) -->
      <div
        v-if="dots && count > 1 && indicator === 'overlay'"
        class="ds-swiper-dots pointer-events-none absolute inset-x-0 bottom-3 flex justify-center gap-1.5"
        aria-hidden="true"
      >
        <span
          v-for="i in count"
          :key="i"
          class="ds-swiper-dot"
          :class="i - 1 === active ? 'ds-swiper-dot-active' : ''"
        />
      </div>
    </div>

    <!-- outside dots sit below the slide so they never cover interactive content -->
    <div
      v-if="dots && count > 1 && indicator === 'outside'"
      class="ds-swiper-dots ds-swiper-dots-outside mt-2.5 flex justify-center gap-1.5"
      aria-hidden="true"
    >
      <span
        v-for="i in count"
        :key="i"
        class="ds-swiper-dot"
        :class="i - 1 === active ? 'ds-swiper-dot-active' : ''"
      />
    </div>
  </div>
</template>

<style scoped>
.ds-swiper-track {
  touch-action: pan-y;
}
.ds-swiper-snap {
  transition: transform 0.34s var(--ease-out-expo, cubic-bezier(0.16, 1, 0.3, 1));
}
.ds-swiper-dot {
  height: 6px;
  width: 6px;
  border-radius: 9999px;
  background: color-mix(in oklch, var(--card) 65%, transparent);
  box-shadow: 0 0 0 1px color-mix(in oklch, var(--foreground) 8%, transparent);
  transition:
    width 0.3s var(--ease-out-expo, cubic-bezier(0.16, 1, 0.3, 1)),
    background-color 0.3s ease;
}
.ds-swiper-dot-active {
  width: 16px;
  background: var(--brand);
}
/* outside dots sit on the page surface, not over an image — use a muted fill */
.ds-swiper-dots-outside .ds-swiper-dot {
  background: color-mix(in oklch, var(--muted-foreground) 32%, transparent);
  box-shadow: none;
}
.ds-swiper-dots-outside .ds-swiper-dot-active {
  background: var(--brand);
}
@media (prefers-reduced-motion: reduce) {
  .ds-swiper-snap,
  .ds-swiper-dot {
    transition: none;
  }
}
</style>
