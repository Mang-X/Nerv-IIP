<script setup lang="ts">
import { cn } from '../../../lib/utils'
const props = defineProps<{ class?: string }>()
</script>
<template>
  <nav aria-label="breadcrumb" data-slot="breadcrumb" :class="props.class"><slot /></nav>
</template>
