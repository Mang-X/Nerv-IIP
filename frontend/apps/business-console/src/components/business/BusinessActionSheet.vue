<script setup lang="ts">
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@nerv-iip/ui'

defineProps<{
  description?: string
  title: string
}>()

const open = defineModel<boolean>('open', { required: true })
</script>

<template>
  <Sheet v-model:open="open">
    <SheetContent class="w-full overflow-y-auto sm:max-w-2xl">
      <SheetHeader class="border-b">
        <SheetTitle>{{ title }}</SheetTitle>
        <SheetDescription v-if="description">{{ description }}</SheetDescription>
      </SheetHeader>
      <div class="grid gap-4 px-4 pb-4">
        <slot />
      </div>
    </SheetContent>
  </Sheet>
</template>
