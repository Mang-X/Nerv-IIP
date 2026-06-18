<script setup lang="ts">
import type { NavigationMenuViewportProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuViewport, useForwardProps } from 'reka-ui'
import { getCurrentInstance, onBeforeUnmount, onMounted } from 'vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — the shared panel container. Reka hoists the active Content into it and
 * exposes measured width/height so the surface animates its size to fit. The
 * panel follows the active trigger: a MutationObserver watches the triggers'
 * open state and sets the viewport's `left` to the open trigger's offset (reka
 * only exposes a CENTER offset, which throws a wide panel off-canvas under edge
 * triggers — left-edge alignment keeps it under its trigger and on screen).
 * Rendered automatically by NavigationMenuPro.
 */
const props = defineProps<NavigationMenuViewportProps & { class?: HTMLAttributes['class'] }>()
const forwarded = useForwardProps(reactiveOmit(props, 'class'))

// The viewport component's root IS the `.ds-nav-viewport-wrap` div, so read it
// from the instance ($el) rather than a template ref. Cache the menu root once.
const instance = getCurrentInstance()
let root: HTMLElement | null = null
let observer: MutationObserver | undefined

function alignToTrigger() {
  if (!root) return
  const list = root.querySelector('[data-slot="navigation-menu-pro-list"]')
  const active = (root.querySelector('[data-slot="navigation-menu-pro-trigger"][data-state="open"]') ??
    root.querySelector('button[aria-expanded="true"]')) as HTMLElement | null
  if (!list || !active) return
  const listRect = list.getBoundingClientRect()
  const tr = active.getBoundingClientRect()
  const triggerCenter = tr.left + tr.width / 2

  // Indicator: glide under the active trigger (reka's own position var is stuck
  // here, so drive it ourselves). Width = trigger width; the diamond is
  // centre-justified, so it ends up pointing at the trigger centre. Inline styles
  // win over the (stuck) reka vars; the scoped transition makes it slide.
  const indicator = root.querySelector<HTMLElement>('[data-slot="navigation-menu-pro-indicator"]')
  if (indicator) {
    indicator.style.width = `${Math.round(tr.width)}px`
    indicator.style.transform = `translateX(${Math.round(tr.left - listRect.left)}px)`
  }

  // Panel: centre under the trigger (like a popover), then clamp to the viewport
  // so a wide panel under an edge trigger stays on screen. The indicator keeps
  // pointing at the trigger even when the panel is clamped off-centre.
  const viewport = root.querySelector<HTMLElement>('[data-slot="navigation-menu-pro-viewport"]')
  const wrap = viewport?.parentElement
  if (viewport && wrap) {
    const wrapLeft = wrap.getBoundingClientRect().left
    // use reka's TARGET width var, not offsetWidth — the panel width is still
    // animating when this runs, so offsetWidth would mis-centre it.
    const panelW =
      Number.parseFloat(getComputedStyle(viewport).getPropertyValue('--reka-navigation-menu-viewport-width')) ||
      viewport.offsetWidth
    const gutter = 8
    let pageLeft = triggerCenter - panelW / 2
    pageLeft = Math.min(Math.max(pageLeft, gutter), window.innerWidth - panelW - gutter)
    viewport.style.left = `${Math.round(pageLeft - wrapLeft)}px`
  }
}

onMounted(() => {
  const el = instance?.proxy?.$el as HTMLElement | null
  root = (el && el.nodeType === 1 ? el.closest('[data-slot="navigation-menu-pro"]') : null) as HTMLElement | null
  if (!root) return
  // reka flips data-state / aria-expanded on the trigger when a panel opens;
  // defer the align with setTimeout (not rAF — throttled in headless renderers)
  // so reka has set the new panel's measured width var before we centre on it.
  observer = new MutationObserver(() => setTimeout(alignToTrigger, 60))
  observer.observe(root, { attributes: true, attributeFilter: ['data-state', 'aria-expanded'], subtree: true })
  alignToTrigger()
})
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <div class="ds-nav-viewport-wrap absolute top-full left-0 isolate z-50">
    <NavigationMenuViewport
      data-slot="navigation-menu-pro-viewport"
      v-bind="forwarded"
      :class="
        cn(
          'ds-nav-viewport data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-90 absolute top-2 left-0 h-(--reka-navigation-menu-viewport-height) w-(--reka-navigation-menu-viewport-width) origin-top overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-lg duration-200',
          props.class,
        )
      "
    />
  </div>
</template>

<style scoped>
/* Absolutely positioned; `left` is set inline to the active trigger's offset (see
   alignToTrigger) and sized to reka's measured content box. Animating `left`
   makes the panel glide to follow the trigger you hover; width/height animate the
   size change between panels. */
.ds-nav-viewport {
  transition:
    left 0.28s var(--ease-out-expo, ease-out),
    width 0.3s var(--ease-out-expo, ease-out),
    height 0.3s var(--ease-out-expo, ease-out);
  /* Glass: faint top highlight over the translucent popover fill. */
  box-shadow:
    0 18px 48px -16px color-mix(in oklch, black 50%, transparent),
    inset 0 1px 0 0 color-mix(in oklch, white 8%, transparent);
}
@media (prefers-reduced-motion: reduce) {
  .ds-nav-viewport {
    transition: none;
  }
}
</style>
