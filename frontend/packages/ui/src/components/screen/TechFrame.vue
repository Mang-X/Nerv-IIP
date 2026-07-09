<script setup lang="ts">
/**
 * Screen — full-bleed tech frame. A thin white-ish hairline edge with a white top
 * highlight; each corner carries a short accent stroke that FADES along both
 * edges into the hairline (a gradient L, not an abrupt bracket) so the corner
 * reads as part of the frame. The accent tints the corner strokes. Content goes
 * in the default slot. Built on the independent `--nv-scr-*` tokens.
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
  <div class="nv-scr-tf" :class="accent">
    <slot />
    <span class="nv-scr-tf-c tl" />
    <span class="nv-scr-tf-c tr" />
    <span class="nv-scr-tf-c bl" />
    <span class="nv-scr-tf-c br" />
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-tf {
    position: relative;
    border-radius: var(--nv-scr-radius);
    box-shadow: inset 0 1px 0 var(--nv-scr-highlight);
    --nv-scr-mark: var(--nv-scr-cyan);
  }
  /* thin white hairline edge — structure, no color */
  .nv-scr-tf::before {
    content: '';
    position: absolute;
    inset: 0;
    border-radius: inherit;
    padding: 1px;
    background: var(--nv-scr-edge-gradient);
    -webkit-mask:
      linear-gradient(#000 0 0) content-box,
      linear-gradient(#000 0 0);
    -webkit-mask-composite: xor;
    mask-composite: exclude;
    pointer-events: none;
  }
  .nv-scr-tf.cyan {
    --nv-scr-mark: var(--nv-scr-cyan);
  }
  .nv-scr-tf.green {
    --nv-scr-mark: var(--nv-scr-green);
  }
  .nv-scr-tf.amber {
    --nv-scr-mark: var(--nv-scr-amber);
  }
  .nv-scr-tf.red {
    --nv-scr-mark: var(--nv-scr-red);
  }
  /* corner accents — an L whose two arms fade along the edges into the hairline */
  .nv-scr-tf-c {
    position: absolute;
    width: 44px;
    height: 44px;
    pointer-events: none;
    z-index: 1;
    /* a small point of light at the corner so the mark reads on the dark board */
    filter: drop-shadow(0 0 3px var(--nv-scr-cyan-dim));
  }
  .nv-scr-tf-c.tl {
    top: 0;
    left: 0;
    background:
      linear-gradient(90deg, var(--nv-scr-mark), transparent) top left / 100% 1.5px no-repeat,
      linear-gradient(180deg, var(--nv-scr-mark), transparent) top left / 1.5px 100% no-repeat;
    border-top-left-radius: var(--nv-scr-radius);
  }
  .nv-scr-tf-c.tr {
    top: 0;
    right: 0;
    background:
      linear-gradient(270deg, var(--nv-scr-mark), transparent) top right / 100% 1.5px no-repeat,
      linear-gradient(180deg, var(--nv-scr-mark), transparent) top right / 1.5px 100% no-repeat;
    border-top-right-radius: var(--nv-scr-radius);
  }
  .nv-scr-tf-c.bl {
    bottom: 0;
    left: 0;
    background:
      linear-gradient(90deg, var(--nv-scr-mark), transparent) bottom left / 100% 1.5px no-repeat,
      linear-gradient(0deg, var(--nv-scr-mark), transparent) bottom left / 1.5px 100% no-repeat;
    border-bottom-left-radius: var(--nv-scr-radius);
  }
  .nv-scr-tf-c.br {
    bottom: 0;
    right: 0;
    background:
      linear-gradient(270deg, var(--nv-scr-mark), transparent) bottom right / 100% 1.5px no-repeat,
      linear-gradient(0deg, var(--nv-scr-mark), transparent) bottom right / 1.5px 100% no-repeat;
    border-bottom-right-radius: var(--nv-scr-radius);
  }
}
</style>
