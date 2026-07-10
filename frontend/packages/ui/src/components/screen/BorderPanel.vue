<script setup lang="ts">
/**
 * Screen — decorative bordered panel. A near-black gradient body inside the same
 * gradient hairline + white top highlight as ScreenPanel, with a single bright
 * notch centered on the top edge and short corner marks that follow the panel
 * radius (no abrupt right-angles against a rounded box). An optional title sits
 * in the top band. Body content goes in the default slot. Built on `--nv-scr-*`.
 */
defineProps<{
  /** Optional heading shown in the top band. */
  title?: string
}>()
</script>

<template>
  <section class="nv-scr-bp">
    <span class="nv-scr-bp-notch" />
    <span class="nv-scr-bp-c tl" />
    <span class="nv-scr-bp-c tr" />
    <span class="nv-scr-bp-c bl" />
    <span class="nv-scr-bp-c br" />
    <div v-if="title || $slots.title" class="nv-scr-bp-h">
      <span class="nv-scr-bp-t"
        ><slot name="title">{{ title }}</slot></span
      >
    </div>
    <div class="nv-scr-bp-body"><slot /></div>
  </section>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-bp {
    position: relative;
    background: linear-gradient(180deg, var(--nv-scr-panel-a), var(--nv-scr-panel-b));
    border-radius: var(--nv-scr-radius);
    padding: 18px 20px;
    color: var(--nv-scr-text);
    isolation: isolate;
    box-shadow:
      inset 0 1px 0 var(--nv-scr-highlight),
      0 10px 30px -18px rgba(0, 0, 0, 0.9);
  }
  .nv-scr-bp::before {
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
    z-index: 0;
  }
  .nv-scr-bp > * {
    position: relative;
    z-index: 1;
  }
  /* bright top-center notch — the one allowed accent flourish, no bloom */
  .nv-scr-bp-notch {
    position: absolute;
    top: -1px;
    left: 50%;
    width: 56px;
    height: 2px;
    transform: translateX(-50%);
    border-radius: 0 0 2px 2px;
    background: linear-gradient(90deg, transparent, var(--nv-scr-cyan), transparent);
    z-index: 2;
  }
  /* corner marks — short L ticks that follow the panel radius (rounded, not abrupt) */
  .nv-scr-bp-c {
    position: absolute;
    width: 12px;
    height: 12px;
    pointer-events: none;
    z-index: 2;
  }
  .nv-scr-bp-c::before {
    content: '';
    position: absolute;
    inset: 0;
    border: 0 solid var(--nv-scr-cyan-dim);
    opacity: 0.8;
  }
  .nv-scr-bp-c.tl {
    top: -1px;
    left: -1px;
  }
  .nv-scr-bp-c.tl::before {
    border-top-width: 1.5px;
    border-left-width: 1.5px;
    border-top-left-radius: var(--nv-scr-radius);
  }
  .nv-scr-bp-c.tr {
    top: -1px;
    right: -1px;
  }
  .nv-scr-bp-c.tr::before {
    border-top-width: 1.5px;
    border-right-width: 1.5px;
    border-top-right-radius: var(--nv-scr-radius);
  }
  .nv-scr-bp-c.bl {
    bottom: -1px;
    left: -1px;
  }
  .nv-scr-bp-c.bl::before {
    border-bottom-width: 1.5px;
    border-left-width: 1.5px;
    border-bottom-left-radius: var(--nv-scr-radius);
  }
  .nv-scr-bp-c.br {
    bottom: -1px;
    right: -1px;
  }
  .nv-scr-bp-c.br::before {
    border-bottom-width: 1.5px;
    border-right-width: 1.5px;
    border-bottom-right-radius: var(--nv-scr-radius);
  }
  .nv-scr-bp-h {
    display: flex;
    align-items: center;
    justify-content: center;
    margin-bottom: 12px;
  }
  .nv-scr-bp-t {
    font-size: 15px;
    font-weight: 500;
    color: var(--nv-scr-text-2);
    letter-spacing: 0.02em;
  }
  .nv-scr-bp-body {
    position: relative;
  }
}
</style>
