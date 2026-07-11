<script setup lang="ts">
import type { AlertDialogActionProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { AlertDialogAction } from 'reka-ui'
import { NvButton } from '../button'

type ButtonProProps = InstanceType<typeof NvButton>['$props']

/**
 * Pro — alert-dialog action (primary confirm). Wraps `ButtonPro` (solid
 * default/brand/destructive) in reka `AlertDialogAction` via `as-child`.
 */
const props = withDefaults(
  defineProps<
    AlertDialogActionProps & {
      class?: HTMLAttributes['class']
      variant?: ButtonProProps['variant']
      size?: ButtonProProps['size']
    }
  >(),
  {
    variant: 'default',
    size: 'default',
  },
)

const delegatedProps = reactiveOmit(props, 'class', 'variant', 'size', 'asChild')
</script>

<template>
  <AlertDialogAction as-child data-slot="alert-dialog-pro-action" v-bind="delegatedProps">
    <NvButton :variant="variant" :size="size" :class="props.class">
      <slot />
    </NvButton>
  </AlertDialogAction>
</template>
