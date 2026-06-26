<script setup lang="ts">
import {
  ButtonPro,
  DropdownMenuPro,
  DropdownMenuProContent,
  DropdownMenuProTrigger,
} from '@nerv-iip/ui'
import { MoreHorizontalIcon } from 'lucide-vue-next'

withDefaults(
  defineProps<{
    disabled?: boolean
    label?: string
    widthClass?: string
  }>(),
  {
    disabled: false,
    label: '更多操作',
    widthClass: 'w-48',
  },
)
</script>

<template>
  <DropdownMenuPro>
    <DropdownMenuProTrigger as-child>
      <ButtonPro
        class="h-8 w-8 rounded-md p-0"
        :disabled="disabled"
        size="sm"
        type="button"
        variant="ghost"
        :aria-label="label"
        :title="label"
      >
        <MoreHorizontalIcon class="h-4 w-4" />
      </ButtonPro>
    </DropdownMenuProTrigger>
    <DropdownMenuProContent align="end" :class="[widthClass, 'max-w-72']">
      <slot />
    </DropdownMenuProContent>
  </DropdownMenuPro>
</template>
