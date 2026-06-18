<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import {
  DialogContent,
  DialogDescription,
  DialogOverlay,
  DialogPortal,
  DialogRoot,
  DialogTitle,
} from 'reka-ui'
import { computed, inject, ref, watch } from 'vue'
import { MOBILE_OVERLAY_TARGET } from '../../lib/overlay-target'
import { cn, rubberband } from '../../lib/utils'

// Defaults to body (full-screen PDA); a host (e.g. docs phone sim) can scope it.
const overlayTarget = inject(MOBILE_OVERLAY_TARGET, undefined)

const props = defineProps<{
  open: boolean
  title?: string
  description?: string
  class?: HTMLAttributes['class']
}>()
const emit = defineEmits<{ 'update:open': [value: boolean] }>()

/**
 * Drag-to-dismiss: grab the top bar (handle + title) and pull down to close.
 * While the finger is down the sheet tracks the pointer 1:1 (no transition); on
 * release it either snaps back or slides the rest of the way out — past a
 * distance threshold OR on a fast downward flick. Overlay click / Esc still
 * close it the normal way (reka's slide-out), so this is pure enhancement.
 */
const dragY = ref(0)
const dragging = ref(false)
const dismissing = ref(false)

/**
 * While dragging we force `transition: none` so the sheet tracks the finger 1:1
 * — without it the `duration-300` utility (there for reka's enter/exit) leaves
 * `transition-property: all` active and the transform would lag ~300ms. On
 * release the inline transition is dropped and `.ds-sheet-snap` takes over.
 */
const sheetStyle = computed(() => {
  if (!dragging.value && !dragY.value) return undefined
  return {
    transform: `translateY(${dragY.value}px)`,
    ...(dragging.value ? { transition: 'none' } : {}),
  }
})
let sheetEl: HTMLElement | null = null
let sheetH = 0
let startY = 0
let lastY = 0
let lastT = 0
let velocity = 0

function onGrabDown(e: PointerEvent) {
  sheetEl = (e.currentTarget as HTMLElement).closest('[role="dialog"]') as HTMLElement | null
  sheetH = sheetEl?.offsetHeight ?? 0
  startY = lastY = e.clientY
  lastT = e.timeStamp
  velocity = 0
  dragging.value = true
  dismissing.value = false
  try {
    ;(e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId)
  } catch {
    // synthetic / already-released pointers can't be captured — non-fatal
  }
}
function onGrabMove(e: PointerEvent) {
  if (!dragging.value || e.buttons === 0) return
  e.preventDefault()
  const dt = e.timeStamp - lastT
  if (dt > 0) velocity = (e.clientY - lastY) / dt // px/ms, positive = downward
  lastY = e.clientY
  lastT = e.timeStamp
  // pulling down tracks 1:1 (the dismiss gesture); pulling up past the open
  // position has nowhere to go, so it rubber-bands and springs back on release
  const raw = e.clientY - startY
  dragY.value = raw >= 0 ? raw : rubberband(raw)
}
function onGrabUp() {
  if (!dragging.value) return
  dragging.value = false
  const threshold = Math.min(140, sheetH * 0.3)
  const flung = velocity > 0.5 && dragY.value > 24
  if (dragY.value > threshold || flung) {
    // dismiss: finish the slide ourselves, then unmount (reka exit anim suppressed)
    dismissing.value = true
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      emit('update:open', false)
      return
    }
    dragY.value = sheetH || 640
    // safety net in case the transition is interrupted and transitionend never fires
    window.setTimeout(() => {
      if (dismissing.value) emit('update:open', false)
    }, 420)
  } else {
    dragY.value = 0 // snap back
  }
}
function onSheetTransitionEnd(e: TransitionEvent) {
  if (dismissing.value && e.propertyName === 'transform' && e.target === e.currentTarget) {
    emit('update:open', false)
  }
}

// Reset drag state when the sheet OPENS (not on close): after a drag-dismiss the
// sheet must stay slid-out (dragY = height, dismissing = true) so reka unmounts
// it off-screen instead of snapping it back to 0 and replaying the exit slide.
watch(
  () => props.open,
  (open) => {
    if (open) {
      dragY.value = 0
      dragging.value = false
      dismissing.value = false
    }
  },
)
</script>

<template>
  <DialogRoot :open="open" @update:open="emit('update:open', $event)">
    <DialogPortal :to="overlayTarget">
      <DialogOverlay
        class="data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-40 bg-black/50 backdrop-blur-[6px] duration-300"
      />
      <DialogContent
        data-slot="mobile-sheet-content"
        :style="sheetStyle"
        :class="
          cn(
            'data-[state=open]:animate-in data-[state=open]:slide-in-from-bottom data-[state=open]:[animation-timing-function:var(--ease-out-expo)] fixed inset-x-0 bottom-0 z-50 flex max-h-[85dvh] flex-col rounded-t-3xl border-t border-border bg-card pb-safe shadow-[0_-8px_40px_-12px_rgb(0_0_0/0.45)] duration-300 outline-none',
            // reka exit slide for normal closes only; drag-dismiss slides manually
            !dismissing &&
              'data-[state=closed]:animate-out data-[state=closed]:slide-out-to-bottom',
            // snap / dismiss transition — disabled while the finger is down so it tracks live
            !dragging && 'ds-sheet-snap',
            $props.class,
          )
        "
        @transitionend="onSheetTransitionEnd"
      >
        <!-- top grab bar: handle + title — pull down to dismiss -->
        <div
          class="ds-sheet-grab shrink-0 cursor-grab pt-3 pb-1 select-none active:cursor-grabbing"
          @pointerdown="onGrabDown"
          @pointermove="onGrabMove"
          @pointerup="onGrabUp"
          @pointercancel="onGrabUp"
        >
          <div class="mx-auto h-1.5 w-11 rounded-full bg-muted-foreground/30" aria-hidden="true" />
          <DialogTitle v-if="title" class="px-4 pt-2 text-base font-semibold text-foreground">
            {{ title }}
          </DialogTitle>
          <!-- 始终渲染描述以消除 reka-ui 缺失描述的控制台告警；无可见描述时用 sr-only 承载标题/占位。 -->
          <DialogDescription
            :class="description ? 'px-4 pt-1 text-sm text-muted-foreground' : 'sr-only'"
          >
            {{ description ?? title ?? '抽屉内容' }}
          </DialogDescription>
        </div>
        <div class="min-h-0 flex-1 overflow-y-auto px-4 pb-4">
          <slot />
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>

<style scoped>
.ds-sheet-grab {
  /* claim vertical gestures for the drag instead of letting them scroll the page */
  touch-action: none;
}
.ds-sheet-snap {
  transition: transform 0.32s var(--ease-out-expo, cubic-bezier(0.16, 1, 0.3, 1));
}
@media (prefers-reduced-motion: reduce) {
  .ds-sheet-snap {
    transition: none;
  }
}
</style>
