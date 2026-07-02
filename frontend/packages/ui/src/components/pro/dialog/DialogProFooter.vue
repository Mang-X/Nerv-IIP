<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { DialogClose } from 'reka-ui'
import { cn } from '../../../lib/utils'
import { ButtonPro } from '../button'

/**
 * Pro — dialog footer. Mirrors base `DialogFooter`'s API so it drops in: pass
 * `show-close-button` for an auto outline "关闭" button, or compose your own
 * actions in the default slot.
 */
const props = withDefaults(
  defineProps<{
    class?: HTMLAttributes['class']
    showCloseButton?: boolean
  }>(),
  {
    showCloseButton: false,
  },
)
</script>

<template>
  <div
    data-slot="dialog-pro-footer"
    :class="cn('flex flex-col-reverse gap-2 sm:flex-row sm:justify-end', props.class)"
  >
    <slot />
    <DialogClose v-if="showCloseButton" as-child>
      <ButtonPro variant="outline">关闭</ButtonPro>
    </DialogClose>
  </div>
</template>
