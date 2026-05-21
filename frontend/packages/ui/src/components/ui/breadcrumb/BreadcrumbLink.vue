<script setup lang="ts">
import { cn } from '../../../lib/utils'
import { Primitive } from 'reka-ui'
const props = defineProps<{ asChild?: boolean; href?: string; class?: string }>()
</script>
<template>
  <Primitive
    data-slot="breadcrumb-link"
    :as="asChild ? 'template' : 'a'"
    :as-child="asChild"
    :href="href"
    :class="cn('hover:text-foreground transition-colors', props.class)"
  >
    <slot />
  </Primitive>
</template>
