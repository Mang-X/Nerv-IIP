<script setup lang="ts">
import { cn } from '../../../lib/utils'
const props = defineProps<{ class?: string }>()
</script>
<template>
  <li data-slot="sidebar-menu-sub-item" :class="cn('', props.class)">
    <slot />
  </li>
</template>
