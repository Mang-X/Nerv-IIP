<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { SearchIcon } from 'lucide-vue-next'
import { Input } from '../../ui/input'
import { cn } from '../../../lib/utils'

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
    <div v-if="showSearch" class="relative w-full sm:max-w-xs">
      <SearchIcon
        class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
        aria-hidden="true"
      />
      <Input
        :model-value="search"
        type="search"
        class="h-9 pl-8"
        :placeholder="searchPlaceholder"
        :aria-label="searchLabel"
        @update:model-value="$emit('update:search', String($event))"
      />
    </div>

    <div v-if="$slots.filters" class="flex flex-wrap items-center gap-2">
      <slot name="filters" />
    </div>

    <div v-if="$slots.actions" class="flex flex-wrap items-center gap-2 sm:ml-auto">
      <slot name="actions" />
    </div>
  </div>
</template>
