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
defineProps<{
  title?: string
  center?: boolean
  mobile?: boolean
  popout?: boolean
  /** Full-width column layout so wide components (NvDataTable, NvDescriptions) fill
   *  the preview instead of shrinking to content inside the default flex row. */
  block?: boolean
}>()
</script>

<template>
  <ClientOnly>
    <!-- `vp-raw` isolates this subtree from VitePress's base/vp-doc resets
         (ADR 0020 §4.2, via postcssIsolateStyles in config.mts). -->
    <div class="ds-demo vp-raw" :class="{ 'ds-demo-popout-box': popout }">
      <div v-if="title" class="ds-demo-title">
        {{ title }}
      </div>
      <div
        class="ds-demo-preview"
        :class="{
          'ds-demo-center': center,
          'ds-demo-mobile': mobile,
          'ds-demo-popout': popout,
          'ds-demo-block': block,
        }"
      >
        <div v-if="mobile" class="ds-demo-phone"><slot /></div>
        <slot v-else />
      </div>
    </div>
    <template #fallback>
      <div class="ds-demo vp-raw ds-demo-loading">预览加载中…</div>
    </template>
  </ClientOnly>
</template>

<style>
/* Demo bleed from VitePress's `.vp-doc` prose typography (heading margins, list
   markers, brand-blue links, table borders) is now neutralised at the source by
   `postcssIsolateStyles` + the `vp-raw` class on the demo root (ADR 0020 §4.2) —
   the old `.vp-doc .ds-demo …` counter-rules are no longer needed and were removed. */
.ds-demo {
  margin: 1.25rem 0;
  border: 1px solid var(--border);
  border-radius: 12px;
  /* page-surface background so surface components (Card, NvDescriptions, …) sit on
     the same base they do in the app and pop with their ring/shadow, instead of
     blending into a same-coloured --card panel. */
  background: var(--background);
  overflow: hidden;
}
.ds-demo-title {
  border-bottom: 1px solid var(--border);
  background: var(--card);
  padding: 0.625rem 0.875rem;
  color: var(--foreground);
  font-size: 0.8125rem;
  font-weight: 600;
  line-height: 1.25rem;
}
.ds-demo-preview {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  padding: 1.75rem 1.5rem;
}
/* `block` — full-width column for wide components (NvDataTable, NvDescriptions) so
   they fill the preview instead of shrinking to content in the flex row. */
.ds-demo-block {
  display: block;
}
.ds-demo-block > * + * {
  margin-top: 0.875rem;
}
.ds-demo-center {
  justify-content: center;
}
/* let a panel that opens below the demo (NavigationMenu mega-menu) ESCAPE the
   box and float OVER the content below (like a real dropdown) — drop the clip so
   it isn't cut off; it overlays via z-index, so no reserved gap is needed. The
   control is pinned top-left. */
.ds-demo-popout-box {
  overflow: visible;
}
.ds-demo-popout {
  align-items: flex-start;
}
.ds-demo-mobile {
  justify-content: center;
  background: repeating-linear-gradient(
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
