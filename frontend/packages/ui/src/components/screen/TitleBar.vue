<script setup lang="ts">
/**
 * Screen — section title bar. A centered title (optional sub-line) flanked by
 * fading hairlines that each terminate in a small accent diamond node. The
 * symmetric rules give a section heading weight without a panel; the diamonds
 * are the single cyan touch. Title is the only meaningful prop.
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
  background: linear-gradient(90deg, transparent, var(--sb-line-2));
}
.sb-tb-rule.right {
  background: linear-gradient(90deg, var(--sb-line-2), transparent);
}
/* accent diamond on the inner end of each rule */
.sb-tb-node {
  position: absolute;
  top: 50%;
  width: 7px;
  height: 7px;
  background: var(--sb-cyan);
  box-shadow: 0 0 7px var(--sb-cyan-dim);
  transform: translateY(-50%) rotate(45deg);
}
.sb-tb-rule.left .sb-tb-node {
  right: 0;
}
.sb-tb-rule.right .sb-tb-node {
  left: 0;
}
.sb-tb-mid {
  flex: none;
  text-align: center;
}
.sb-tb-t {
  font-size: 20px;
  font-weight: 600;
  letter-spacing: 0.08em;
  margin: 0;
}
.sb-tb-sub {
  font-size: 12px;
  color: var(--sb-muted);
  margin: 4px 0 0;
  letter-spacing: 0.04em;
}
</style>
