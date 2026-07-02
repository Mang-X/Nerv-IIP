<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { useSlots } from 'vue'
import { ChevronLeft } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Mobile NavBar — top app bar (tdesign-mobile style). Centered title, optional
 * back affordance on the left, free action slot on the right.
 */
withDefaults(
  defineProps<{
    title?: string
    back?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { back: false },
)
const emit = defineEmits<{ back: [] }>()
const slots = useSlots()
</script>

<template>
  <div
    data-slot="nav-bar"
    :class="cn('relative flex h-12 items-center justify-center gap-2 px-2', $props.class)"
  >
    <div class="absolute left-1 flex items-center">
      <button
        v-if="back"
        type="button"
        class="flex size-9 items-center justify-center rounded-full text-foreground active:bg-accent"
        aria-label="返回"
        @click="emit('back')"
      >
        <ChevronLeft class="size-6" aria-hidden="true" />
      </button>
      <slot name="left" />
    </div>

    <div class="truncate px-12 text-base font-semibold">
      <slot>{{ title }}</slot>
    </div>

    <div class="absolute right-1 flex items-center gap-1">
      <slot name="right" />
    </div>
  </div>
</template>
