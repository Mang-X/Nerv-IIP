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
  const active =
    root.querySelector('[data-slot="navigation-menu-pro-trigger"][data-state="open"]') ??
    root.querySelector('button[aria-expanded="true"]')
  const viewport = root.querySelector<HTMLElement>('[data-slot="navigation-menu-pro-viewport"]')
  if (!list || !active || !viewport) return
  const left = (active as HTMLElement).getBoundingClientRect().left - list.getBoundingClientRect().left
  // set `left` inline (not via a Tailwind arbitrary class — those aren't always
  // generated for this package in the docs build); the scoped transition glides it
  viewport.style.left = `${Math.max(0, Math.round(left))}px`
}

onMounted(() => {
  const el = instance?.proxy?.$el as HTMLElement | null
  root = (el && el.nodeType === 1 ? el.closest('[data-slot="navigation-menu-pro"]') : null) as HTMLElement | null
  if (!root) return
  // reka flips data-state / aria-expanded on the trigger when a panel opens;
  // align synchronously on those mutations (no rAF — it can be throttled in
  // headless/background renderers) so the panel follows the active trigger.
  observer = new MutationObserver(alignToTrigger)
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
