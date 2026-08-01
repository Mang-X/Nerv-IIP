<script setup lang="ts">
import { NvCell } from '@nerv-iip/ui-mobile'
import { RouterLink } from 'vue-router'

const props = defineProps<{
  to: string
  title: string
  note?: string
  accessibleName?: string
}>()
</script>

<template>
  <RouterLink
    :to="props.to"
    :aria-label="props.accessibleName ?? props.title"
    class="pda-navigation-cell"
  >
    <NvCell :title="props.title" :note="props.note">
      <template v-if="$slots.icon" #icon><slot name="icon" /></template>
    </NvCell>
  </RouterLink>
</template>

<style scoped>
.pda-navigation-cell {
  display: block;
  color: inherit;
  text-decoration: none;
}

.pda-navigation-cell:focus-visible {
  outline: 2px solid var(--ring);
  outline-offset: -2px;
}
</style>
