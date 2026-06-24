<script setup lang="ts">
/**
 * Screen — full-bleed tech frame. Wraps a board in a thin, faintly glowing edge
 * with a minimal L-bracket at each corner (the "현대 vs 廉价" line: short marks,
 * no stacked neon). The accent color tints the edge and the brackets; default is
 * the live-data cyan. Content goes in the default slot — frame draws over it.
 */
withDefaults(
  defineProps<{
    /** Edge + corner-bracket color. */
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
  border: 1px solid var(--sb-edge);
  border-radius: var(--sb-radius);
  /* faint inner + outer glow on the edge, kept restrained */
  box-shadow:
    inset 0 0 0 1px rgba(255, 255, 255, 0.02),
    0 0 16px -4px var(--sb-edge);
  /* token defaults to cyan; tone classes below override */
  --sb-edge: var(--sb-cyan-dim);
  --sb-mark: var(--sb-cyan);
}
.sb-tf.cyan {
  --sb-edge: var(--sb-cyan-dim);
  --sb-mark: var(--sb-cyan);
}
.sb-tf.green {
  --sb-edge: rgba(0, 230, 118, 0.5);
  --sb-mark: var(--sb-green);
}
.sb-tf.amber {
  --sb-edge: rgba(255, 214, 0, 0.5);
  --sb-mark: var(--sb-amber);
}
.sb-tf.red {
  --sb-edge: rgba(255, 23, 68, 0.5);
  --sb-mark: var(--sb-red);
}
/* corner L-brackets — two borders per pseudo make the right-angle */
.sb-tf-c {
  position: absolute;
  width: 16px;
  height: 16px;
  pointer-events: none;
}
.sb-tf-c::before {
  content: '';
  position: absolute;
  inset: 0;
  border: 0 solid var(--sb-mark);
  filter: drop-shadow(0 0 3px var(--sb-edge));
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
