<script setup lang="ts">
/**
 * Screen — full-bleed tech frame. Wraps a board section in a restrained gradient
 * hairline (brighter down the two sides, like ScreenPanel) with a short corner
 * mark at each corner — a thin tick that follows the panel radius, no neon bloom.
 * The accent tints the corner marks; the edge itself stays quiet. Content goes in
 * the default slot — the frame draws over it. Built on the independent `--sb-*`.
 */
withDefaults(
  defineProps<{
    /** Corner-mark color. */
    accent?: 'cyan' | 'green' | 'amber' | 'red'
  }>(),
  { accent: 'cyan' },
)
</script>

<template>
  <div class="sb-tf" :class="accent">
    <slot />
    <span class="sb-tf-c tl" />
    <span class="sb-tf-c tr" />
    <span class="sb-tf-c bl" />
    <span class="sb-tf-c br" />
  </div>
</template>

<style scoped>
.sb-tf {
  position: relative;
  border-radius: var(--sb-radius);
  /* white top highlight (glass) — no colored bloom */
  box-shadow: inset 0 1px 0 var(--sb-highlight);
  --sb-mark: var(--sb-cyan);
}
/* gradient hairline edge — brighter down the two sides, dim top/bottom */
.sb-tf::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  padding: 1px;
  background: linear-gradient(
    90deg,
    rgba(120, 180, 235, 0.3),
    rgba(255, 255, 255, 0.05) 16%,
    rgba(255, 255, 255, 0.05) 84%,
    rgba(120, 180, 235, 0.3)
  );
  -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
  -webkit-mask-composite: xor;
  mask-composite: exclude;
  pointer-events: none;
}
.sb-tf.cyan {
  --sb-mark: var(--sb-cyan);
}
.sb-tf.green {
  --sb-mark: var(--sb-green);
}
.sb-tf.amber {
  --sb-mark: var(--sb-amber);
}
.sb-tf.red {
  --sb-mark: var(--sb-red);
}
/* corner marks — a short L tick that follows the panel radius (no abrupt angle),
   hairline, only a faint presence — not a glowing neon bracket */
.sb-tf-c {
  position: absolute;
  width: 14px;
  height: 14px;
  pointer-events: none;
  z-index: 1;
}
.sb-tf-c::before {
  content: '';
  position: absolute;
  inset: 0;
  border: 0 solid var(--sb-mark);
  opacity: 0.85;
}
.sb-tf-c.tl {
  top: -1px;
  left: -1px;
}
.sb-tf-c.tl::before {
  border-top-width: 1.5px;
  border-left-width: 1.5px;
  border-top-left-radius: var(--sb-radius);
}
.sb-tf-c.tr {
  top: -1px;
  right: -1px;
}
.sb-tf-c.tr::before {
  border-top-width: 1.5px;
  border-right-width: 1.5px;
  border-top-right-radius: var(--sb-radius);
}
.sb-tf-c.bl {
  bottom: -1px;
  left: -1px;
}
.sb-tf-c.bl::before {
  border-bottom-width: 1.5px;
  border-left-width: 1.5px;
  border-bottom-left-radius: var(--sb-radius);
}
.sb-tf-c.br {
  bottom: -1px;
  right: -1px;
}
.sb-tf-c.br::before {
  border-bottom-width: 1.5px;
  border-right-width: 1.5px;
  border-bottom-right-radius: var(--sb-radius);
}
</style>
