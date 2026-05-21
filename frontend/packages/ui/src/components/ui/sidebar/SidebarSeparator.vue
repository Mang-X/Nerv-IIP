<script setup lang="ts">
import { cn } from '../../../lib/utils'
import Separator from '../separator/Separator.vue'
const props = defineProps<{ class?: string }>()
</script>
<template>
  <Separator
    data-slot="sidebar-separator"
    :class="cn('bg-sidebar-border mx-2 w-auto', props.class)"
  />
</template>
