<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { SearchIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import { NvInput } from '../../pro/input'

withDefaults(
  defineProps<{
    /** Show the built-in search input (bound via v-model:search). */
    search?: string
    showSearch?: boolean
    searchPlaceholder?: string
    searchLabel?: string
    class?: HTMLAttributes['class']
  }>(),
  {
    showSearch: true,
    searchPlaceholder: '搜索',
    searchLabel: '搜索',
  },
)

defineEmits<{ 'update:search': [value: string] }>()
</script>

<template>
  <div :class="cn('flex flex-col gap-3 sm:flex-row sm:items-center', $props.class)">
    <NvInput
      v-if="showSearch"
      :model-value="search"
      type="search"
      class="w-full sm:max-w-xs"
      :placeholder="searchPlaceholder"
      :aria-label="searchLabel"
      @update:model-value="$emit('update:search', String($event))"
    >
      <template #leading><SearchIcon aria-hidden="true" /></template>
    </NvInput>

    <div v-if="$slots.filters" class="flex flex-wrap items-center gap-2">
      <slot name="filters" />
    </div>

    <div v-if="$slots.actions" class="flex flex-wrap items-center gap-2 sm:ml-auto">
      <slot name="actions" />
    </div>
  </div>
</template>
