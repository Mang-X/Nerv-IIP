<script setup lang="ts">
import {
  SheetPro,
  SheetProContent,
  SheetProDescription,
  SheetProHeader,
  SheetProTitle,
} from '@nerv-iip/ui'

defineProps<{
  description?: string
  title: string
}>()

const open = defineModel<boolean>('open', { required: true })
</script>

<template>
  <SheetPro v-model:open="open">
    <SheetProContent class="w-full overflow-y-auto sm:max-w-2xl">
      <SheetProHeader class="border-b">
        <SheetProTitle>{{ title }}</SheetProTitle>
        <SheetProDescription v-if="description">{{ description }}</SheetProDescription>
      </SheetProHeader>
      <div class="grid gap-4 px-4 pb-4">
        <slot />
      </div>
    </SheetProContent>
  </SheetPro>
</template>
