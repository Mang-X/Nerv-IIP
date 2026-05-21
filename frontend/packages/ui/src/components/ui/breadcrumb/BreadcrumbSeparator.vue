<script setup lang="ts">
import { cn } from '../../../lib/utils'
import { ChevronRightIcon } from 'lucide-vue-next'
const props = defineProps<{ class?: string }>()
</script>
<template>
  <li
    data-slot="breadcrumb-separator"
    role="presentation"
    aria-hidden="true"
    :class="cn('[&>svg]:size-3.5', props.class)"
  >
    <slot><ChevronRightIcon /></slot>
  </li>
</template>
