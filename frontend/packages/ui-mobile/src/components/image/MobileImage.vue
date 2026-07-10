<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref, watch } from 'vue'
import { ImageOff } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Mobile Image (Arco Design Mobile `Image` form) — lazy-loaded image with a
 * muted shimmer placeholder while loading and a broken-image fallback on error.
 * Supports object-fit, corner radius and a fixed aspect ratio.
 */
const props = withDefaults(
  defineProps<{
    src: string
    alt?: string
    /** object-fit of the image inside its box. */
    fit?: 'cover' | 'contain'
    /** Corner radius token. */
    radius?: 'none' | 'sm' | 'md' | 'lg' | 'full'
    /** Aspect ratio (width / height), e.g. 16/9 or '4/3'. Reserves layout space. */
    ratio?: number | string
    class?: HTMLAttributes['class']
  }>(),
  { fit: 'cover', radius: 'md' },
)

const status = ref<'loading' | 'loaded' | 'error'>('loading')

watch(
  () => props.src,
  () => {
    status.value = 'loading'
  },
)

const radiusClass = computed(
  () =>
    ({
      none: 'rounded-none',
      sm: 'rounded-md',
      md: 'rounded-lg',
      lg: 'rounded-2xl',
      full: 'rounded-full',
    })[props.radius],
)

const boxStyle = computed(() =>
  props.ratio != null ? { aspectRatio: String(props.ratio) } : undefined,
)
</script>

<template>
  <div
    data-slot="mobile-image"
    :class="cn('ds-mimg relative block overflow-hidden bg-muted', radiusClass, $props.class)"
    :style="boxStyle"
  >
    <!-- shimmer placeholder while loading -->
    <div
      v-show="status === 'loading'"
      class="ds-mimg-skeleton absolute inset-0"
      aria-hidden="true"
    />
    <!-- broken-image fallback -->
    <div
      v-if="status === 'error'"
      class="absolute inset-0 flex flex-col items-center justify-center gap-1 text-muted-foreground"
    >
      <ImageOff class="size-6" aria-hidden="true" />
      <span class="text-xs">加载失败</span>
    </div>
    <img
      v-show="status !== 'error'"
      :src="src"
      :alt="alt"
      loading="lazy"
      decoding="async"
      class="block h-full w-full transition-opacity duration-300"
      :class="[
        fit === 'contain' ? 'object-contain' : 'object-cover',
        status === 'loaded' ? 'opacity-100' : 'opacity-0',
      ]"
      @load="status = 'loaded'"
      @error="status = 'error'"
    />
  </div>
</template>

<style scoped>
@layer nv-components {
  .ds-mimg-skeleton {
    background: linear-gradient(
      100deg,
      transparent 30%,
      color-mix(in oklch, var(--foreground) 6%, transparent) 50%,
      transparent 70%
    );
    background-size: 220% 100%;
    animation: ds-mimg-shimmer 1.4s var(--nv-ease-out-expo) infinite;
  }
  @keyframes ds-mimg-shimmer {
    from {
      background-position: 180% 0;
    }
    to {
      background-position: -80% 0;
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-mimg-skeleton {
      animation: none;
    }
  }
}
</style>
