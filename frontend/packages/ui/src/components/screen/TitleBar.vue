<script setup lang="ts">
/**
 * Screen — section title bar. A centered white title (optional sub-line) with a
 * short cyan underline accent, flanked by hairlines that gradate into a small
 * cyan diamond node with a soft point of light. Gives a section heading weight
 * without a panel. Built on the independent `--nv-scr-*` tokens.
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
  <div class="nv-scr-tb">
    <span class="nv-scr-tb-rule left"><i class="nv-scr-tb-node" /></span>
    <div class="nv-scr-tb-mid">
      <h2 class="nv-scr-tb-t">{{ title }}</h2>
      <p v-if="sub" class="nv-scr-tb-sub">{{ sub }}</p>
      <span class="nv-scr-tb-underline" />
    </div>
    <span class="nv-scr-tb-rule right"><i class="nv-scr-tb-node" /></span>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-tb {
    display: flex;
    align-items: center;
    gap: 18px;
    color: var(--nv-scr-text);
  }
  .nv-scr-tb-rule {
    position: relative;
    flex: 1;
    height: 1px;
  }
  .nv-scr-tb-rule.left {
    background: linear-gradient(
      90deg,
      transparent,
      var(--nv-scr-line-2) 55%,
      var(--nv-scr-cyan-dim)
    );
  }
  .nv-scr-tb-rule.right {
    background: linear-gradient(
      90deg,
      var(--nv-scr-cyan-dim),
      var(--nv-scr-line-2) 45%,
      transparent
    );
  }
  /* small diamond node at the inner end, with a soft point of light */
  .nv-scr-tb-node {
    position: absolute;
    top: 50%;
    width: 6px;
    height: 6px;
    background: var(--nv-scr-cyan);
    box-shadow: 0 0 6px var(--nv-scr-cyan-dim);
    transform: translateY(-50%) rotate(45deg);
  }
  .nv-scr-tb-rule.left .nv-scr-tb-node {
    right: 0;
  }
  .nv-scr-tb-rule.right .nv-scr-tb-node {
    left: 0;
  }
  .nv-scr-tb-mid {
    position: relative;
    flex: none;
    text-align: center;
    padding-bottom: 9px;
  }
  .nv-scr-tb-t {
    font-size: 22px;
    font-weight: 600;
    letter-spacing: 0.1em;
    margin: 0;
    color: #fff;
  }
  .nv-scr-tb-sub {
    font-size: 12px;
    color: var(--nv-scr-muted);
    margin: 5px 0 0;
    letter-spacing: 0.08em;
  }
  /* a short cyan underline under the title — the precise accent */
  .nv-scr-tb-underline {
    position: absolute;
    bottom: 0;
    left: 50%;
    width: 42px;
    height: 2px;
    transform: translateX(-50%);
    border-radius: 2px;
    background: linear-gradient(90deg, transparent, var(--nv-scr-cyan), transparent);
  }
}
</style>
