import type { InjectionKey } from 'vue'

/**
 * Mobile overlay stacking contract. Regular modal overlays own the base pair;
 * input overlays must use the priority pair so a teleported keyboard remains
 * interactive when it is opened from inside a modal sheet.
 */
export const MOBILE_OVERLAY_LAYER = {
  backdrop: 40,
  surface: 50,
  inputBackdrop: 60,
  inputSurface: 70,
} as const

const PRIORITY_MOBILE_OVERLAY_SELECTOR = '[data-mobile-overlay-layer^="input-"]'

export function isPriorityMobileOverlayTarget(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest(PRIORITY_MOBILE_OVERLAY_SELECTOR) !== null
}

/**
 * Teleport target for mobile overlays (BottomSheet, Dialog, NumberKeyboard,
 * Toast). When nothing provides it, overlays teleport to `body` — the correct
 * place in a real full-screen PDA app. A host can `provide` this (e.g. the docs'
 * phone simulator) to keep overlays inside a bounded frame instead of covering
 * the whole page. Pass a CSS selector or an element.
 */
export const MOBILE_OVERLAY_TARGET: InjectionKey<string | HTMLElement> = Symbol(
  'nerv-mobile-overlay-target',
)
