<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, onMounted, ref } from 'vue'
import { clampRubber, cn } from '../../lib/utils'

/**
 * Mobile SwipeCell — swipe a row left to reveal right-side actions (Vant /
 * tdesign-mobile style). Pointer-driven (works with touch + mouse), snaps
 * open/closed, only engages on a horizontal drag so vertical scroll is intact.
 */
export interface SwipeAction {
  label: string
  value: string
  tone?: 'default' | 'brand' | 'danger'
}

defineProps<{ actions: SwipeAction[]; class?: HTMLAttributes['class'] }>()
const emit = defineEmits<{ select: [value: string] }>()

const actionsEl = ref<HTMLElement>()
const offset = ref(0)
const dragging = ref(false)
const actionsWidth = ref(0)
let startX = 0
let startY = 0
let startOffset = 0
let axis: 'h' | 'v' | null = null

/**
 * Action opacity tracks the left-swipe fraction (−offset / width): faded out at
 * rest-closed and on any rightward rubber-band, full at fully-open. On release
 * it transitions in lock-step with the content slide (same duration + easing),
 * so the action colour fades away *as* the cell closes — never a visible frame
 * of red after it's shut.
 */
const actionsOpacity = computed(() =>
  actionsWidth.value
    ? Math.min(1, Math.max(0, -offset.value) / actionsWidth.value)
    : offset.value < 0
      ? 1
      : 0,
)
function onDown(e: PointerEvent) {
  actionsWidth.value = actionsEl.value?.offsetWidth ?? 0
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
    // in-range tracks the finger 1:1; dragging past either edge rubber-bands
    offset.value = clampRubber(startOffset + dx, -actionsWidth.value, 0)
  }
}
function onUp() {
  if (axis === 'h') {
    offset.value = offset.value < -actionsWidth.value / 2 ? -actionsWidth.value : 0
  }
  dragging.value = false
  axis = null
}
function pick(value: string) {
  emit('select', value)
  offset.value = 0
}

onMounted(() => {
  actionsWidth.value = actionsEl.value?.offsetWidth ?? 0
})
</script>

<template>
  <div
    data-slot="swipe-cell"
    :class="cn('nv-m-swipe relative overflow-hidden bg-card', $props.class)"
  >
    <!-- right actions (revealed) — opacity tracks the open fraction so the colour
         is fully gone at rest-closed (nothing sits behind the content's
         anti-aliased rounded edge → no 1px corner fringe) and fades in lock-step
         with the slide on open/close, never a visible frame of red after shut. -->
    <div
      ref="actionsEl"
      class="nv-m-swipe-actions absolute inset-y-0 right-0 flex"
      :class="!dragging && 'nv-m-swipe-actions-snap'"
      :style="{ opacity: actionsOpacity }"
    >
      <button
        v-for="action in actions"
        :key="action.value"
        type="button"
        :class="
          cn(
            'flex h-full min-w-[76px] items-center justify-center px-4 text-[15px] font-medium text-white active:opacity-80',
            action.tone === 'danger'
              ? 'bg-destructive'
              : action.tone === 'brand'
                ? 'bg-brand'
                : 'bg-muted-foreground',
          )
        "
        @click="pick(action.value)"
      >
        {{ action.label }}
      </button>
    </div>
    <!-- swipeable content -->
    <div
      class="nv-m-swipe-content relative z-10 bg-card"
      :class="!dragging && 'nv-m-swipe-snap'"
      :style="{ transform: `translateX(${offset}px)` }"
      @pointerdown="onDown"
      @pointermove="onMove"
      @pointerup="onUp"
      @pointercancel="onUp"
    >
      <slot />
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  /* No own rounding: the parent group's single `overflow:hidden + rounded` clip
   handles both the closed cover and the revealed actions. (Rounding the cell
   itself added a second, slightly different arc — the 1px crescent between the
   two radii is where the action colour leaked at the corners.) */
  .nv-m-swipe-content {
    touch-action: pan-y;
  }
  .nv-m-swipe-snap {
    transition: transform 0.26s var(--nv-ease-out-expo);
  }
  /* During a drag, opacity tracks the offset live (no transition). On release the
   snap class fades it with the SAME duration + easing as the content slide, so
   the colour and the position stay perfectly in step. */
  .nv-m-swipe-actions-snap {
    transition: opacity 0.26s var(--nv-ease-out-expo);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-m-swipe-snap,
    .nv-m-swipe-actions-snap {
      transition: none;
    }
  }
}
</style>
