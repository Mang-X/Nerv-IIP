<script setup lang="ts">
// Mobile component doc layout (Arco Design Mobile style): documentation prose +
// code on the LEFT, a sticky phone simulator on the RIGHT showing the live
// components. Use `layout: page` so the two columns have room.
//   <MobileDoc>
//     <template #phone> …live components… </template>
//     …markdown prose / code / props… (default slot)
//   </MobileDoc>
import { provide } from 'vue'
import { MOBILE_OVERLAY_TARGET } from '@nerv-iip/ui-mobile'
import SceneBadge from './SceneBadge.vue'

// Keep mobile overlays (NvBottomSheet, NvPicker, DatePicker, NvActionSheet, Dialog,
// NvNumberKeyboard, Toast) inside the phone frame instead of covering the page.
// `.nv-mdoc-screen` is given a containing block (transform) below so their
// `position: fixed` anchors to the phone, not the viewport.
provide(MOBILE_OVERLAY_TARGET, '.nv-mdoc-screen')
</script>

<template>
  <div class="nv-mdoc">
    <div class="nv-mdoc-main vp-doc">
      <!-- PDA pages are `layout: page`, so the Layout `#doc-before` badge doesn't
           fire here — render the scene-availability badge at the top of the prose
           column instead. Auto-detects the mobile family from the route. -->
      <SceneBadge />
      <slot />
    </div>
    <aside class="nv-mdoc-aside">
      <div class="nv-mdoc-sticky">
        <ClientOnly>
          <!-- `vp-raw` isolates the phone preview from VitePress base/vp-doc
               resets (ADR 0020 §4.2); the prose column keeps its `vp-doc`. -->
          <div class="nv-mdoc-phone vp-raw">
            <div class="nv-mdoc-statusbar">
              <span class="font-semibold tabular-nums">9:41</span>
              <span class="nv-mdoc-notch" aria-hidden="true" />
              <span class="nv-mdoc-signal" aria-hidden="true" />
            </div>
            <div class="nv-mdoc-screen">
              <slot name="phone" />
            </div>
          </div>
        </ClientOnly>
      </div>
    </aside>
  </div>
</template>

<style>
.nv-mdoc {
  display: grid;
  gap: 2.5rem;
  align-items: start;
  /* generous gutters (Arco docs style): breathing room above + on both sides so
     content/phone don't hug the sidebar and right edge. */
  padding: 1.75rem 2rem 3rem;
}
/* Two columns only when there's real room (≥1200px); below that the phone
   stacks under the docs at full width instead of cramping a narrow split. */
@media (min-width: 1200px) {
  .nv-mdoc {
    grid-template-columns: minmax(0, 1fr) 380px;
    gap: 3.5rem;
    padding: 2.25rem 3rem 3.5rem;
  }
}
@media (min-width: 1600px) {
  .nv-mdoc {
    padding: 2.5rem 4rem 4rem;
  }
}
.nv-mdoc-main {
  min-width: 0;
}
.nv-mdoc-sticky {
  position: static;
}
@media (min-width: 1200px) {
  .nv-mdoc-sticky {
    position: sticky;
    top: 5.5rem;
  }
}
.nv-mdoc-phone {
  width: 100%;
  max-width: 360px;
  margin-inline: auto;
  border: 1px solid var(--border);
  border-radius: 28px;
  background: var(--background);
  overflow: hidden;
  box-shadow:
    0 1px 0 0 color-mix(in oklch, white 6%, transparent) inset,
    0 20px 50px -20px color-mix(in oklch, black 60%, transparent);
}
.nv-mdoc-statusbar {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 2.25rem;
  padding-inline: 1.25rem;
  font-size: 0.75rem;
  color: var(--foreground);
}
.nv-mdoc-notch {
  position: absolute;
  left: 50%;
  top: 0.5rem;
  height: 1.1rem;
  width: 5rem;
  transform: translateX(-50%);
  border-radius: 9999px;
  background: color-mix(in oklch, var(--foreground) 88%, transparent);
}
.nv-mdoc-signal {
  width: 1.1rem;
  height: 0.6rem;
  border-radius: 2px;
  background: color-mix(in oklch, var(--foreground) 70%, transparent);
}
.nv-mdoc-screen {
  height: 560px;
  overflow-y: auto;
  background: var(--background);
  scrollbar-width: thin;
  /* Reserve the scrollbar gutter permanently. Bottom overlays (NvBottomSheet,
     NvActionSheet, NvPicker, Dialog, Toast) teleport here and animate up from
     translateY(100%); mid-animation they briefly extend past the bottom, which
     would pop a scrollbar and squeeze the content width, then release it. A
     stable gutter keeps the width fixed so there's no reflow flash. */
  scrollbar-gutter: stable;
  /* containing block for overlays teleported here (MOBILE_OVERLAY_TARGET): a
     transform makes this element the containing block for `position: fixed`
     descendants, so sheets/dialogs/keypads/toasts anchor to the phone screen
     (clipped by overflow) instead of the viewport. (`contain: paint` is the
     non-GPU alternative but Chromium doesn't reliably anchor fixed children to
     it here, so we use transform.) */
  transform: translateZ(0);
}
/* tidy default rhythm for stacked demos inside the phone */
.nv-mdoc-screen :where(section) {
  padding: 1rem;
}
.nv-mdoc-screen :where(section) + :where(section) {
  border-top: 1px solid var(--border);
}
.nv-mdoc-label {
  margin-bottom: 0.625rem;
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.02em;
  color: var(--muted-foreground);
}
</style>
