<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * PageSection — a vertical content section with an optional centered header
 * (eyebrow + title + description) above the default slot (Nuxt UI style).
 */
const props = defineProps<{
  eyebrow?: string
  title?: string
  description?: string
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <section data-slot="page-section" :class="cn('py-12 sm:py-16', props.class)">
    <div
      v-if="eyebrow || title || description || $slots.header"
      class="mx-auto max-w-2xl text-center"
    >
      <p v-if="eyebrow" class="text-sm font-semibold tracking-wide text-brand-strong">
        {{ eyebrow }}
      </p>
      <h2 v-if="title" class="mt-2 text-2xl font-semibold tracking-tight text-balance sm:text-3xl">
        {{ title }}
      </h2>
      <p v-if="description" class="mt-3 text-base text-muted-foreground text-pretty">
        {{ description }}
      </p>
      <slot name="header" />
    </div>
    <div :class="(eyebrow || title || description || $slots.header) && 'mt-10'">
      <slot />
    </div>
  </section>
</template>
