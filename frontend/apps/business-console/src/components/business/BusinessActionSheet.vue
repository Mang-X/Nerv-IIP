<script setup lang="ts">
import {
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
} from '@nerv-iip/ui'

defineProps<{
  description?: string
  title: string
}>()

const open = defineModel<boolean>('open', { required: true })
</script>

<template>
  <NvSheet v-model:open="open">
    <NvSheetContent class="w-full overflow-y-auto sm:max-w-2xl">
      <NvSheetHeader class="border-b">
        <NvSheetTitle>{{ title }}</NvSheetTitle>
        <NvSheetDescription v-if="description">{{ description }}</NvSheetDescription>
      </NvSheetHeader>
      <div class="grid gap-4 px-4 pb-4">
        <slot />
      </div>
    </NvSheetContent>
  </NvSheet>
</template>
