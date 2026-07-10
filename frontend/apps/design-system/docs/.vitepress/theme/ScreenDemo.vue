<script setup lang="ts">
/**
 * Dark live-preview wrapper for the screen (big-board) layer — the counterpart of
 * `<Demo>`. The screen components carry their own `--nv-scr-*` industrial-blue tokens
 * and only read well on a dark surface, so this stage paints the control-room
 * backdrop (radial industrial-blue + faint grid) rather than the light card `<Demo>`
 * uses. Slotted content lays itself out; pass `wide` to stretch a single full-width
 * module (charts, tables) instead of the default centered flex row.
 */
defineProps<{ wide?: boolean }>()
</script>

<template>
  <ClientOnly>
    <!-- `vp-raw` isolates the screen demo from VitePress base/vp-doc resets
         (ADR 0020 §4.2) — replaces the removed `.vp-doc .sb-tbl` counter-rules. -->
    <div class="ds-sd vp-raw">
      <div class="ds-sd-grid" />
      <div class="ds-sd-body" :class="{ wide }">
        <slot />
      </div>
    </div>
  </ClientOnly>
</template>

<style>
.ds-sd {
  position: relative;
  margin: 18px 0;
  border-radius: 14px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  background: radial-gradient(
    135% 130% at 50% -10%,
    #0a1830 0%,
    #07101f 38%,
    #050a14 68%,
    #03050b 100%
  );
  padding: 34px 32px;
  overflow: hidden;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04);
  color: var(--nv-scr-text, #fff);
  font-variant-numeric: tabular-nums;
}
.ds-sd-grid {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(125, 170, 255, 0.06) 1px, transparent 1px),
    linear-gradient(90deg, rgba(125, 170, 255, 0.06) 1px, transparent 1px);
  background-size: 44px 44px;
  -webkit-mask-image: radial-gradient(90% 90% at 50% 30%, #000 30%, transparent 100%);
  mask-image: radial-gradient(90% 90% at 50% 30%, #000 30%, transparent 100%);
  opacity: 0.45;
  pointer-events: none;
}
.ds-sd-body {
  position: relative;
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: 20px;
}
.ds-sd-body.wide {
  display: block;
}
/* Demo blocks live inside the docs article column — let wide modules breathe. */
.ds-sd-body.wide > * {
  width: 100%;
}
</style>
