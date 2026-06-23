<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { MoreHorizontalIcon } from 'lucide-vue-next'
import { Button } from '../../ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuTrigger } from '../../ui/dropdown-menu'
import { cn } from '../../../lib/utils'

withDefaults(
  defineProps<{
    disabled?: boolean
    /** Accessible label for the trigger (also the tooltip). */
    label?: string
    align?: 'start' | 'center' | 'end'
    contentClass?: HTMLAttributes['class']
  }>(),
  {
    disabled: false,
    label: '更多操作',
    align: 'end',
  },
)
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button
        class="size-8 p-0"
        :disabled="disabled"
        size="icon"
        type="button"
        variant="ghost"
        :aria-label="label"
        :title="label"
      >
        <MoreHorizontalIcon class="size-4" aria-hidden="true" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent :align="align" :class="cn('w-48 max-w-72', contentClass)">
      <slot />
    </DropdownMenuContent>
  </DropdownMenu>
</template>
