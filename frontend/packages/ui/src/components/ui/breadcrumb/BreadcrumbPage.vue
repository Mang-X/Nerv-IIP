<script setup lang="ts">
import { cn } from '../../../lib/utils'
const props = defineProps<{ class?: string }>()
</script>
<template>
  <span
    data-slot="breadcrumb-page"
    role="link"
    aria-disabled="true"
    aria-current="page"
    :class="cn('text-foreground font-normal', props.class)"
  >
    <slot />
  </span>
</template>
