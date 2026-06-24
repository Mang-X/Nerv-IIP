<script setup lang="ts">
/**
 * Screen — full-bleed tech frame. A thin white-ish hairline edge with a white top
 * highlight; each corner carries a short accent stroke that FADES along both
 * edges into the hairline (a gradient L, not an abrupt bracket) so the corner
 * reads as part of the frame. The accent tints the corner strokes. Content goes
 * in the default slot. Built on the independent `--sb-*` tokens.
 */
withDefaults(
  defineProps<{
    /** Corner-stroke color. */
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
  box-shadow: inset 0 1px 0 var(--sb-highlight);
  --sb-mark: var(--sb-cyan);
}
/* thin white hairline edge — structure, no color */
.sb-tf::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  padding: 1px;
  background: linear-gradient(
    90deg,
    rgba(255, 255, 255, 0.2),
    rgba(255, 255, 255, 0.09) 20%,
    rgba(255, 255, 255, 0.09) 80%,
    rgba(255, 255, 255, 0.2)
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
/* corner accents — an L whose two arms fade along the edges into the hairline */
.sb-tf-c {
  position: absolute;
  width: 30px;
  height: 30px;
  pointer-events: none;
  z-index: 1;
}
.sb-tf-c.tl {
  top: 0;
  left: 0;
  background:
    linear-gradient(90deg, var(--sb-mark), transparent) top left / 100% 1.5px no-repeat,
    linear-gradient(180deg, var(--sb-mark), transparent) top left / 1.5px 100% no-repeat;
  border-top-left-radius: var(--sb-radius);
}
.sb-tf-c.tr {
  top: 0;
  right: 0;
  background:
    linear-gradient(270deg, var(--sb-mark), transparent) top right / 100% 1.5px no-repeat,
    linear-gradient(180deg, var(--sb-mark), transparent) top right / 1.5px 100% no-repeat;
  border-top-right-radius: var(--sb-radius);
}
.sb-tf-c.bl {
  bottom: 0;
  left: 0;
  background:
    linear-gradient(90deg, var(--sb-mark), transparent) bottom left / 100% 1.5px no-repeat,
    linear-gradient(0deg, var(--sb-mark), transparent) bottom left / 1.5px 100% no-repeat;
  border-bottom-left-radius: var(--sb-radius);
}
.sb-tf-c.br {
  bottom: 0;
  right: 0;
  background:
    linear-gradient(270deg, var(--sb-mark), transparent) bottom right / 100% 1.5px no-repeat,
    linear-gradient(0deg, var(--sb-mark), transparent) bottom right / 1.5px 100% no-repeat;
  border-bottom-right-radius: var(--sb-radius);
}
</style>
