<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Swiper slide wrapper — use inside <Swiper>'s default slot when supplying
 * markup directly rather than via :items. Each item occupies one full viewport.
 */
defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <div data-slot="swiper-item" :class="cn('w-full shrink-0', $props.class)">
    <slot />
  </div>
</template>
