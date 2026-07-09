<script setup lang="ts">
import type { AlertDialogCancelProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { AlertDialogCancel } from 'reka-ui'
import { NvButton } from '../button'

type ButtonProProps = InstanceType<typeof NvButton>['$props']

/**
 * Pro — alert-dialog cancel (dismiss). Wraps `ButtonPro variant="outline"` in
 * reka `AlertDialogCancel` via `as-child`.
 */
const props = withDefaults(
  defineProps<
    AlertDialogCancelProps & {
      class?: HTMLAttributes['class']
      variant?: ButtonProProps['variant']
      size?: ButtonProProps['size']
    }
  >(),
  {
    variant: 'outline',
    size: 'default',
  },
)

const delegatedProps = reactiveOmit(props, 'class', 'variant', 'size', 'asChild')
</script>

<template>
  <AlertDialogCancel as-child data-slot="alert-dialog-pro-cancel" v-bind="delegatedProps">
    <NvButton :variant="variant" :size="size" :class="props.class">
      <slot />
    </NvButton>
  </AlertDialogCancel>
</template>
