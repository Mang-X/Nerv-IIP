import type { InjectionKey } from 'vue'

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
