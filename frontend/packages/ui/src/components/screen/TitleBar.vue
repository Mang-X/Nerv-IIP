<script setup lang="ts">
/**
 * Screen — section title bar. A centered white title (optional sub-line) with a
 * short cyan underline accent, flanked by hairlines that gradate into a small
 * cyan diamond node with a soft point of light. Gives a section heading weight
 * without a panel. Built on the independent `--sb-*` tokens.
 */
withDefaults(
  defineProps<{
    /** Center heading, e.g. 产线综合监控. */
    title?: string
    /** Optional sub-line under the title. */
    sub?: string
  }>(),
  { title: '产线综合监控' },
)
</script>

<template>
  <div class="sb-tb">
    <span class="sb-tb-rule left"><i class="sb-tb-node" /></span>
    <div class="sb-tb-mid">
      <h2 class="sb-tb-t">{{ title }}</h2>
      <p v-if="sub" class="sb-tb-sub">{{ sub }}</p>
      <span class="sb-tb-underline" />
    </div>
    <span class="sb-tb-rule right"><i class="sb-tb-node" /></span>
  </div>
</template>

<style scoped>
.sb-tb {
  display: flex;
  align-items: center;
  gap: 18px;
  color: var(--sb-text);
}
.sb-tb-rule {
  position: relative;
  flex: 1;
  height: 1px;
}
.sb-tb-rule.left {
  background: linear-gradient(90deg, transparent, var(--sb-line-2) 55%, var(--sb-cyan-dim));
}
.sb-tb-rule.right {
  background: linear-gradient(90deg, var(--sb-cyan-dim), var(--sb-line-2) 45%, transparent);
}
/* small diamond node at the inner end, with a soft point of light */
.sb-tb-node {
  position: absolute;
  top: 50%;
  width: 6px;
  height: 6px;
  background: var(--sb-cyan);
  box-shadow: 0 0 6px var(--sb-cyan-dim);
  transform: translateY(-50%) rotate(45deg);
}
.sb-tb-rule.left .sb-tb-node {
  right: 0;
}
.sb-tb-rule.right .sb-tb-node {
  left: 0;
}
.sb-tb-mid {
  position: relative;
  flex: none;
  text-align: center;
  padding-bottom: 9px;
}
.sb-tb-t {
  font-size: 22px;
  font-weight: 600;
  letter-spacing: 0.1em;
  margin: 0;
  color: #fff;
}
.sb-tb-sub {
  font-size: 12px;
  color: var(--sb-muted);
  margin: 5px 0 0;
  letter-spacing: 0.08em;
}
/* a short cyan underline under the title — the precise accent */
.sb-tb-underline {
  position: absolute;
  bottom: 0;
  left: 50%;
  width: 42px;
  height: 2px;
  transform: translateX(-50%);
  border-radius: 2px;
  background: linear-gradient(90deg, transparent, var(--sb-cyan), transparent);
}
</style>
