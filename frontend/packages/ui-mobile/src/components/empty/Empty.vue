<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { useSlots } from 'vue'
import { Inbox } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Mobile Empty — no-data placeholder (Vant / tdesign-mobile style): muted icon,
 * description, optional action slot. Distinct from Result (success/error).
 */
defineProps<{ description?: string; class?: HTMLAttributes['class'] }>()
const slots = useSlots()
</script>

<template>
  <div
    data-slot="empty"
    :class="
      cn('flex flex-col items-center justify-center gap-3 px-6 py-12 text-center', $props.class)
    "
  >
    <span class="text-muted-foreground/50">
      <slot name="icon"><Inbox class="size-14" stroke-width="1.25" aria-hidden="true" /></slot>
    </span>
    <p v-if="description" class="text-sm text-muted-foreground">{{ description }}</p>
    <div v-if="slots.default" class="pt-1"><slot /></div>
  </div>
</template>
