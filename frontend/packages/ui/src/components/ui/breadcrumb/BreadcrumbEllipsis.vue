<script setup lang="ts">
import { cn } from '../../../lib/utils'
import { MoreHorizontalIcon } from 'lucide-vue-next'
const props = defineProps<{ class?: string }>()
</script>
<template>
  <span
    data-slot="breadcrumb-ellipsis"
    role="presentation"
    aria-hidden="true"
    :class="cn('flex size-9 items-center justify-center', props.class)"
  >
    <MoreHorizontalIcon class="size-4" />
    <span class="sr-only">More</span>
  </span>
</template>
