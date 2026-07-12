<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { MoreHorizontalIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import { NvButton } from '../../pc/button'
import {
  NvDropdownMenu,
  NvDropdownMenuContent,
  NvDropdownMenuTrigger,
} from '../../pc/dropdown-menu'

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
  <NvDropdownMenu>
    <NvDropdownMenuTrigger as-child>
      <NvButton
        class="size-8 p-0"
        :disabled="disabled"
        size="icon"
        type="button"
        variant="ghost"
        :aria-label="label"
        :title="label"
      >
        <MoreHorizontalIcon class="size-4" aria-hidden="true" />
      </NvButton>
    </NvDropdownMenuTrigger>
    <NvDropdownMenuContent :align="align" :class="cn('w-48 max-w-72', contentClass)">
      <slot />
    </NvDropdownMenuContent>
  </NvDropdownMenu>
</template>
