<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref, useSlots, watch } from 'vue'
import { User } from '@lucide/vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Avatar — image with graceful fallback. When `src` is missing or fails
 * to load, falls back to `name` initials (or a user icon) on a muted tint.
 * Circle by default; square supported for catalog/equipment thumbnails.
 */
type Size = 'sm' | 'md' | 'lg'
type Shape = 'circle' | 'square'

const props = withDefaults(
  defineProps<{
    src?: string
    name?: string
    alt?: string
    size?: Size
    shape?: Shape
    class?: HTMLAttributes['class']
  }>(),
  { size: 'md', shape: 'circle' },
)
const slots = useSlots()

const failed = ref(false)
watch(
  () => props.src,
  () => {
    failed.value = false
  },
)
const showImage = computed(() => !!props.src && !failed.value)

/** First two CJK chars or up to two initials from latin words. */
const initials = computed(() => {
  const n = props.name?.trim()
  if (!n) return ''
  if (/[一-龥]/.test(n)) return n.slice(0, 2)
  return n
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0])
    .join('')
    .toUpperCase()
})

const sizeClass: Record<Size, string> = {
  sm: 'size-8 text-xs',
  md: 'size-10 text-sm',
  lg: 'size-14 text-base',
}
</script>

<template>
  <span
    data-slot="avatar"
    :class="
      cn(
        'relative inline-flex shrink-0 select-none items-center justify-center overflow-hidden bg-muted font-medium text-muted-foreground',
        shape === 'circle' ? 'rounded-full' : 'rounded-[10px]',
        sizeClass[size],
        $props.class,
      )
    "
  >
    <img
      v-if="showImage"
      :src="src"
      :alt="alt ?? name ?? ''"
      class="size-full object-cover"
      @error="failed = true"
    />
    <template v-else>
      <slot>
        <span v-if="initials" class="leading-none">{{ initials }}</span>
        <User v-else class="size-[55%]" stroke-width="1.75" aria-hidden="true" />
      </slot>
    </template>
  </span>
</template>
