<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — copy-rebuilt loader set (does NOT touch原版 Spinner primitive).
 * Restrained, brand-aware loading indicators in four shapes. All animation
 * lives in scoped CSS and degrades to a static mark under reduced-motion.
 */
const props = withDefaults(
  defineProps<{
    variant?: 'ring' | 'dots' | 'bars' | 'pulse'
    size?: 'sm' | 'default' | 'lg'
    label?: string
    class?: HTMLAttributes['class']
  }>(),
  {
    variant: 'ring',
    size: 'default',
    label: '加载中',
  },
)

const px = computed(() => ({ sm: 16, default: 22, lg: 32 })[props.size])
</script>

<template>
  <span
    role="status"
    :aria-label="label"
    :class="cn('ds-loader inline-flex items-center justify-center align-middle', props.class)"
    :style="{ '--loader-size': `${px}px` }"
  >
    <!-- ring: brand arc sweeping over a faint track -->
    <span v-if="variant === 'ring'" class="ds-loader-ring" />

    <!-- dots: three dots fading in sequence -->
    <span v-else-if="variant === 'dots'" class="ds-loader-dots"> <i /><i /><i /> </span>

    <!-- bars: equalizer scaling on the y axis -->
    <span v-else-if="variant === 'bars'" class="ds-loader-bars"> <i /><i /><i /><i /> </span>

    <!-- pulse: a soft expanding brand halo -->
    <span v-else class="ds-loader-pulse"><i /><i /></span>

    <span class="sr-only">{{ label }}</span>
  </span>
</template>

<style scoped>
@layer nv-components {
  .ds-loader {
    width: var(--loader-size);
    height: var(--loader-size);
    color: var(--nv-brand);
  }

  /* ring */
  .ds-loader-ring {
    width: 100%;
    height: 100%;
    border-radius: 9999px;
    background: conic-gradient(
      from 0deg,
      transparent 0deg,
      color-mix(in oklch, var(--nv-brand) 90%, transparent) 300deg,
      var(--nv-brand) 360deg
    );
    -webkit-mask: radial-gradient(
      farthest-side,
      transparent calc(100% - 2.5px),
      #000 calc(100% - 2.5px)
    );
    mask: radial-gradient(farthest-side, transparent calc(100% - 2.5px), #000 calc(100% - 2.5px));
    animation: ds-spin 0.7s linear infinite;
  }

  /* dots */
  .ds-loader-dots {
    display: inline-flex;
    gap: calc(var(--loader-size) * 0.16);
  }
  .ds-loader-dots i {
    width: calc(var(--loader-size) * 0.24);
    height: calc(var(--loader-size) * 0.24);
    border-radius: 9999px;
    background: currentColor;
    animation: ds-dot 1s var(--nv-ease-in-out-quart, ease-in-out) infinite;
  }
  .ds-loader-dots i:nth-child(2) {
    animation-delay: 0.15s;
  }
  .ds-loader-dots i:nth-child(3) {
    animation-delay: 0.3s;
  }

  /* bars */
  .ds-loader-bars {
    display: inline-flex;
    align-items: center;
    gap: calc(var(--loader-size) * 0.12);
    height: 100%;
  }
  .ds-loader-bars i {
    width: calc(var(--loader-size) * 0.14);
    height: 100%;
    border-radius: 9999px;
    background: currentColor;
    transform-origin: center;
    animation: ds-bar 0.9s var(--nv-ease-in-out-quart, ease-in-out) infinite;
  }
  .ds-loader-bars i:nth-child(2) {
    animation-delay: 0.12s;
  }
  .ds-loader-bars i:nth-child(3) {
    animation-delay: 0.24s;
  }
  .ds-loader-bars i:nth-child(4) {
    animation-delay: 0.36s;
  }

  /* pulse */
  .ds-loader-pulse {
    position: relative;
    width: 100%;
    height: 100%;
  }
  .ds-loader-pulse i {
    position: absolute;
    inset: 0;
    border-radius: 9999px;
    background: currentColor;
    opacity: 0.5;
    animation: ds-pulse 1.4s var(--nv-ease-out-expo, ease-out) infinite;
  }
  .ds-loader-pulse i:nth-child(2) {
    animation-delay: 0.7s;
  }

  @keyframes ds-spin {
    to {
      transform: rotate(360deg);
    }
  }
  @keyframes ds-dot {
    0%,
    100% {
      opacity: 0.25;
      transform: translateY(0);
    }
    40% {
      opacity: 1;
      transform: translateY(calc(var(--loader-size) * -0.18));
    }
  }
  @keyframes ds-bar {
    0%,
    100% {
      transform: scaleY(0.4);
      opacity: 0.5;
    }
    50% {
      transform: scaleY(1);
      opacity: 1;
    }
  }
  @keyframes ds-pulse {
    0% {
      transform: scale(0.4);
      opacity: 0.6;
    }
    100% {
      transform: scale(1);
      opacity: 0;
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .ds-loader-ring {
      animation-duration: 1.6s;
    }
    .ds-loader-dots i,
    .ds-loader-bars i,
    .ds-loader-pulse i {
      animation: none;
      opacity: 0.7;
      transform: none;
    }
  }
}
</style>
