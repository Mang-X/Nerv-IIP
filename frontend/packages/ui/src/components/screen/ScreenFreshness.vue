<script setup lang="ts">
/**
 * Stable visual boundary for a screen data source's freshness. The caller owns
 * the source-specific clock and wording so unrelated feeds are never merged.
 */
withDefaults(
  defineProps<{
    tone?: 'live' | 'stale' | 'wait'
    label: string
  }>(),
  { tone: 'wait' },
)
</script>

<template>
  <span class="nv-scr-freshness" :class="tone" role="status" aria-atomic="true">
    <i class="nv-scr-freshness-dot" aria-hidden="true" />
    <span>{{ label }}</span>
  </span>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-freshness {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    flex: none;
    white-space: nowrap;
    color: var(--nv-scr-faint);
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-freshness-dot {
    width: 7px;
    height: 7px;
    flex: none;
    border-radius: 50%;
    background: currentColor;
  }
  .nv-scr-freshness.live {
    color: var(--nv-scr-green);
  }
  .nv-scr-freshness.live .nv-scr-freshness-dot {
    box-shadow: 0 0 7px var(--nv-scr-green);
    animation: nv-scr-freshness-breathe 4.5s ease-in-out infinite;
  }
  .nv-scr-freshness.stale {
    color: var(--nv-scr-amber);
  }
  .nv-scr-freshness.stale .nv-scr-freshness-dot {
    box-shadow: 0 0 7px var(--nv-scr-amber);
  }
  @keyframes nv-scr-freshness-breathe {
    0%,
    100% {
      opacity: 0.55;
    }
    50% {
      opacity: 1;
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-freshness.live .nv-scr-freshness-dot {
      animation: none;
    }
  }
}
</style>
