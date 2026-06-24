<script setup lang="ts">
/**
 * Screen — decorative bordered panel. A hairline box with short glowing accent
 * lines at the corners and a single bright notch centered on the top edge; an
 * optional title sits inline with the notch. Restraint over neon — the marks are
 * short and single-layer. Body content goes in the default slot.
 */
defineProps<{
  /** Optional heading shown in the top-edge band. */
  title?: string
}>()
</script>

<template>
  <section class="sb-bp">
    <!-- top center notch -->
    <span class="sb-bp-notch" />
    <!-- corner accent strokes -->
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
  border: 1px solid var(--sb-line);
  border-radius: var(--sb-radius);
  padding: 18px 20px;
  box-shadow: var(--sb-sheen);
  color: var(--sb-text);
}
/* bright top-center notch — the one allowed accent flourish */
.sb-bp-notch {
  position: absolute;
  top: -1px;
  left: 50%;
  width: 56px;
  height: 2px;
  transform: translateX(-50%);
  border-radius: 0 0 2px 2px;
  background: linear-gradient(90deg, transparent, var(--sb-cyan), transparent);
  box-shadow: 0 0 8px var(--sb-cyan-dim);
}
/* corner accents: two short hairlines forming an inset right-angle tick */
.sb-bp-c {
  position: absolute;
  width: 14px;
  height: 14px;
  pointer-events: none;
}
.sb-bp-c::before {
  content: '';
  position: absolute;
  inset: 0;
  border: 0 solid var(--sb-cyan-dim);
}
.sb-bp-c.tl {
  top: 5px;
  left: 5px;
}
.sb-bp-c.tl::before {
  border-top-width: 1px;
  border-left-width: 1px;
}
.sb-bp-c.tr {
  top: 5px;
  right: 5px;
}
.sb-bp-c.tr::before {
  border-top-width: 1px;
  border-right-width: 1px;
}
.sb-bp-c.bl {
  bottom: 5px;
  left: 5px;
}
.sb-bp-c.bl::before {
  border-bottom-width: 1px;
  border-left-width: 1px;
}
.sb-bp-c.br {
  bottom: 5px;
  right: 5px;
}
.sb-bp-c.br::before {
  border-bottom-width: 1px;
  border-right-width: 1px;
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
