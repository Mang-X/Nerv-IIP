<script setup lang="ts">
/**
 * Screen — decorative bordered panel. A near-black gradient body inside the same
 * gradient hairline + white top highlight as ScreenPanel, with a single bright
 * notch centered on the top edge and short corner marks that follow the panel
 * radius (no abrupt right-angles against a rounded box). An optional title sits
 * in the top band. Body content goes in the default slot. Built on `--sb-*`.
 */
defineProps<{
  /** Optional heading shown in the top band. */
  title?: string
}>()
</script>

<template>
  <section class="sb-bp">
    <span class="sb-bp-notch" />
    <span class="sb-bp-c tl" />
    <span class="sb-bp-c tr" />
    <span class="sb-bp-c bl" />
    <span class="sb-bp-c br" />
    <div v-if="title || $slots.title" class="sb-bp-h">
      <span class="sb-bp-t"><slot name="title">{{ title }}</slot></span>
    </div>
    <div class="sb-bp-body"><slot /></div>
  </section>
</template>

<style scoped>
.sb-bp {
  position: relative;
  background: linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
  border-radius: var(--sb-radius);
  padding: 18px 20px;
  color: var(--sb-text);
  isolation: isolate;
  box-shadow:
    inset 0 1px 0 var(--sb-highlight),
    0 10px 30px -18px rgba(0, 0, 0, 0.9);
}
.sb-bp::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  padding: 1px;
  background: linear-gradient(
    90deg,
    rgba(120, 180, 235, 0.28),
    rgba(255, 255, 255, 0.05) 16%,
    rgba(255, 255, 255, 0.05) 84%,
    rgba(120, 180, 235, 0.28)
  );
  -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
  -webkit-mask-composite: xor;
  mask-composite: exclude;
  pointer-events: none;
  z-index: 0;
}
.sb-bp > * {
  position: relative;
  z-index: 1;
}
/* bright top-center notch — the one allowed accent flourish, no bloom */
.sb-bp-notch {
  position: absolute;
  top: -1px;
  left: 50%;
  width: 56px;
  height: 2px;
  transform: translateX(-50%);
  border-radius: 0 0 2px 2px;
  background: linear-gradient(90deg, transparent, var(--sb-cyan), transparent);
  z-index: 2;
}
/* corner marks — short L ticks that follow the panel radius (rounded, not abrupt) */
.sb-bp-c {
  position: absolute;
  width: 12px;
  height: 12px;
  pointer-events: none;
  z-index: 2;
}
.sb-bp-c::before {
  content: '';
  position: absolute;
  inset: 0;
  border: 0 solid var(--sb-cyan-dim);
  opacity: 0.8;
}
.sb-bp-c.tl {
  top: -1px;
  left: -1px;
}
.sb-bp-c.tl::before {
  border-top-width: 1.5px;
  border-left-width: 1.5px;
  border-top-left-radius: var(--sb-radius);
}
.sb-bp-c.tr {
  top: -1px;
  right: -1px;
}
.sb-bp-c.tr::before {
  border-top-width: 1.5px;
  border-right-width: 1.5px;
  border-top-right-radius: var(--sb-radius);
}
.sb-bp-c.bl {
  bottom: -1px;
  left: -1px;
}
.sb-bp-c.bl::before {
  border-bottom-width: 1.5px;
  border-left-width: 1.5px;
  border-bottom-left-radius: var(--sb-radius);
}
.sb-bp-c.br {
  bottom: -1px;
  right: -1px;
}
.sb-bp-c.br::before {
  border-bottom-width: 1.5px;
  border-right-width: 1.5px;
  border-bottom-right-radius: var(--sb-radius);
}
.sb-bp-h {
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 12px;
}
.sb-bp-t {
  font-size: 15px;
  font-weight: 500;
  color: var(--sb-text-2);
  letter-spacing: 0.02em;
}
.sb-bp-body {
  position: relative;
}
</style>
