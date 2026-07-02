<script setup lang="ts">
import type { Component, HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Container — center and constrain page content with responsive gutters
 * (Nuxt UI style). The width preset maps to our layout scale.
 */
withDefaults(
  defineProps<{
    as?: string | Component
    size?: 'sm' | 'md' | 'lg' | 'xl' | 'full'
    class?: HTMLAttributes['class']
  }>(),
  { as: 'div', size: 'lg' },
)

const sizes = {
  sm: 'max-w-3xl',
  md: 'max-w-5xl',
  lg: 'max-w-6xl',
  xl: 'max-w-7xl',
  full: 'max-w-none',
}
</script>

<template>
  <component
    :is="as"
    data-slot="container"
    :class="cn('mx-auto w-full px-4 sm:px-6 lg:px-8', sizes[size], $props.class)"
  >
    <slot />
  </component>
</template>
