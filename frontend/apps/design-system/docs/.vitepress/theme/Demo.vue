<script setup lang="ts">
// Live-preview container for component pages (shadcn / Arco style): renders the
// slotted component demo in a bordered, padded surface using our design tokens.
// Wrapped in ClientOnly so interactive components (overlays, charts, gestures)
// never break the SSR build.
// `mobile` renders the demo in a narrow, centered phone-width column so PDA
// components are previewed at a realistic width.
// `popout` lets a panel that opens below the demo (NavigationMenu mega-menu,
// dropdowns) escape the preview box: the box drops `overflow:hidden`, top-aligns
// its control, and reserves empty space underneath so the floating panel doesn't
// cover the next section.
defineProps<{ title?: string; center?: boolean; mobile?: boolean; popout?: boolean }>()
</script>

<template>
  <ClientOnly>
    <div class="ds-demo" :class="{ 'ds-demo-popout-box': popout }">
      <div class="ds-demo-preview" :class="{ 'ds-demo-center': center, 'ds-demo-mobile': mobile, 'ds-demo-popout': popout }">
        <div v-if="mobile" class="ds-demo-phone"><slot /></div>
        <slot v-else />
      </div>
    </div>
    <template #fallback>
      <div class="ds-demo ds-demo-loading">预览加载中…</div>
    </template>
  </ClientOnly>
</template>

<style>
/* The demo lives inside VitePress's `.vp-doc`, whose prose typography would
   inject article margins/borders/sizes onto any heading/paragraph/list in the
   demo (e.g. `.vp-doc h3 { margin-top: 32px }`). Neutralise that bleed so demo
   content is governed only by its own utilities — like Tailwind's `not-prose`.
   High specificity (.vp-doc .ds-demo …) to beat VitePress's `.vp-doc h3` etc. */
.vp-doc .ds-demo :is(h1, h2, h3, h4, h5, h6, p, ul, ol, li) {
  margin: 0;
  padding: 0;
  border: 0;
  list-style: none;
}
/* Links inside a demo are component links (breadcrumb, nav, …), not prose links —
   strip VitePress's brand-blue + underline so the component's own styling shows. */
.vp-doc .ds-demo a {
  color: inherit;
  font-weight: inherit;
  text-decoration: none;
}
.ds-demo {
  margin: 1.25rem 0;
  border: 1px solid var(--border);
  border-radius: 12px;
  /* page-surface background so surface components (Card, Descriptions, …) sit on
     the same base they do in the app and pop with their ring/shadow, instead of
     blending into a same-coloured --card panel. */
  background: var(--background);
  overflow: hidden;
}
.ds-demo-preview {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  padding: 1.75rem 1.5rem;
}
.ds-demo-center {
  justify-content: center;
}
/* let a panel that opens below the demo (NavigationMenu mega-menu) ESCAPE the
   box: drop the clip and reserve empty space below so the floating panel doesn't
   cover the next section. The control is pinned top-left. */
.ds-demo-popout-box {
  overflow: visible;
  margin-bottom: 11rem;
}
.ds-demo-popout {
  align-items: flex-start;
}
.ds-demo-mobile {
  justify-content: center;
  background:
    repeating-linear-gradient(
      45deg,
      color-mix(in oklch, var(--muted) 50%, transparent) 0 1px,
      transparent 1px 10px
    );
}
.ds-demo-phone {
  width: 100%;
  max-width: 390px;
  /* generous inner padding so atomic decorations (badge bubbles, focus rings,
     shadows) have room and are never clipped; also keeps full-bleed children
     inside the rounded border without needing overflow:hidden. */
  padding: 1.5rem 1.25rem;
  border: 1px solid var(--border);
  border-radius: 16px;
  background: var(--background);
}

.ds-demo-loading {
  padding: 1.75rem 1.5rem;
  color: var(--muted-foreground);
  font-size: 0.875rem;
}
</style>
