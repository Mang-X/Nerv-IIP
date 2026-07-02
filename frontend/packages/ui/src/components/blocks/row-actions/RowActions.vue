<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { MoreHorizontalIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import { ButtonPro } from '../../pro/button'
import { DropdownMenuPro, DropdownMenuProContent, DropdownMenuProTrigger } from '../../pro/dropdown-menu'

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
  <DropdownMenuPro>
    <DropdownMenuProTrigger as-child>
      <ButtonPro
        class="size-8 p-0"
        :disabled="disabled"
        size="icon"
        type="button"
        variant="ghost"
        :aria-label="label"
        :title="label"
      >
        <MoreHorizontalIcon class="size-4" aria-hidden="true" />
      </ButtonPro>
    </DropdownMenuProTrigger>
    <DropdownMenuProContent :align="align" :class="cn('w-48 max-w-72', contentClass)">
      <slot />
    </DropdownMenuProContent>
  </DropdownMenuPro>
</template>
